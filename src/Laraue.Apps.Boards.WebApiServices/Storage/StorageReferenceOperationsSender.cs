using System.Diagnostics.Metrics;
using Grpc.Core;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.Metrics;
using Laraue.Apps.Storage.Internal.Contracts;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Boards.WebApiServices.Storage;

/// <summary>
/// Sends the queued <see cref="StorageReferenceOperation"/>s to Laraue.Apps.Storage after Boards' commits, every
/// <see cref="Interval"/>: a row is deleted once Storage accepts it, otherwise retried after a pause that doubles from
/// 10 s up to an hour. A confirm must get through within Storage's day for pending uploads, or the upload is deleted -
/// <c>boards_storage_operations_pending</c> and <c>boards_storage_operations_oldest_age_seconds</c> show a queue that
/// doesn't drain. WebApiHost only, one instance.
/// </summary>
public sealed class StorageReferenceOperationsSender : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    public const int BatchSize = 50;

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);
    private const int LastErrorMaxLength = 1024;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<StorageReferenceOperationsSender> _logger;

    private long _pending;
    private double _oldestAgeSeconds;

    public StorageReferenceOperationsSender(
        IMeterFactory meterFactory,
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<StorageReferenceOperationsSender> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;

        var meter = meterFactory.Create(BoardsMetrics.MeterName);
        meter.CreateObservableGauge(
            "boards.storage_operations.pending",
            () => Interlocked.Read(ref _pending),
            description: "Confirms and deletes queued for Laraue.Apps.Storage and not sent yet.");
        meter.CreateObservableGauge(
            "boards.storage_operations.oldest_age_seconds",
            () => Volatile.Read(ref _oldestAgeSeconds),
            description: "How long the oldest queued Storage operation has waited; a confirm must go through within a day.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Sending Storage operations failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Sends the operations due now, a batch at a time, and returns how many Storage accepted.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var storageClient = scope.ServiceProvider.GetRequiredService<StorageService.StorageServiceClient>();

        var sent = 0;
        var now = _dateTimeProvider.UtcNow;
        var due = await context.StorageReferenceOperations
            .Where(x => x.NextAttemptAt <= now)
            .OrderBy(x => x.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var operation in due)
        {
            if (await TrySend(storageClient, operation, cancellationToken))
            {
                context.StorageReferenceOperations.Remove(operation);
                sent++;
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        await RefreshGauges(context, cancellationToken);

        return sent;
    }

    /// <summary>
    /// True when the row is done (sent, or can never succeed); false when it's rescheduled.
    /// </summary>
    private async Task<bool> TrySend(
        StorageService.StorageServiceClient storageClient,
        StorageReferenceOperation operation,
        CancellationToken cancellationToken)
    {
        var referenceId = operation.ReferenceId.ToString();
        try
        {
            if (operation.Kind == StorageReferenceOperationKind.Confirm)
            {
                await storageClient.ConfirmReferenceAsync(
                    new ConfirmReferenceRequest { ReferenceId = referenceId },
                    cancellationToken: cancellationToken);
            }
            else
            {
                await storageClient.DeleteReferenceAsync(
                    new DeleteReferenceRequest { ReferenceId = referenceId },
                    cancellationToken: cancellationToken);
            }

            return true;
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.NotFound && operation.Kind == StorageReferenceOperationKind.Confirm)
        {
            // Storage already deleted the upload (it stayed pending for a day): retrying can't bring it back.
            _logger.LogError(
                "Storage reference {ReferenceId} was gone when confirming it: its attachment has lost its file",
                operation.ReferenceId);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            operation.Attempts++;
            operation.LastError = e.Message.Length > LastErrorMaxLength ? e.Message[..LastErrorMaxLength] : e.Message;
            operation.NextAttemptAt = _dateTimeProvider.UtcNow + GetRetryDelay(operation.Attempts);

            _logger.LogWarning(
                e,
                "Storage {Kind} of reference {ReferenceId} failed (attempt {Attempts}), retrying at {NextAttemptAt}",
                operation.Kind,
                operation.ReferenceId,
                operation.Attempts,
                operation.NextAttemptAt);
            return false;
        }
    }

    private static TimeSpan GetRetryDelay(int attempts)
    {
        var delay = FirstRetryDelay * Math.Pow(2, Math.Min(attempts - 1, 20));
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }

    private async Task RefreshGauges(DatabaseContext context, CancellationToken cancellationToken)
    {
        var pending = await context.StorageReferenceOperations.LongCountAsync(cancellationToken);
        var oldest = await context.StorageReferenceOperations
            .OrderBy(x => x.CreatedAt)
            .Select(x => (DateTime?)x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        Interlocked.Exchange(ref _pending, pending);
        Volatile.Write(ref _oldestAgeSeconds, oldest is null ? 0 : (_dateTimeProvider.UtcNow - oldest.Value).TotalSeconds);
    }
}
