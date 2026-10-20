using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// "Connect this machine" for the agent runner (<see cref="AgentSignInRequest"/>, BRD-309): the runner starts a
/// sign-in and polls with its device code, a person allows or denies it on the Allow page by the user code, and the
/// first poll after Allow creates the API key and returns it once. Every state change is one conditional
/// <c>UPDATE ... WHERE State = expected</c>, so a request is decided once and its key handed out once. No permission
/// checks, like every core service: the caller checks that the person may allow it for the organization.
/// </summary>
public interface ICoreAgentSignInService
{
    /// <summary>
    /// Starts a sign-in for the agent <paramref name="clientName"/> on <paramref name="machineName"/>. <paramref name="replacesApiKey"/> is the raw key
    /// the runner held so far, if any: that exact key is revoked once the new one is issued, provided it belongs to the
    /// same user and organization. An unknown or revoked key is ignored, so the answer doesn't tell whether a key exists.
    /// </summary>
    Task<AgentSignInStartResult> Start(
        string clientName,
        string machineName,
        string? replacesApiKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Pending -> Approved for the user and organization. False when no pending, unexpired request has this code.
    /// </summary>
    Task<bool> Approve(
        string userCode,
        Guid userId,
        long organizationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Pending -> Denied. False when no pending, unexpired request has this code.
    /// </summary>
    Task<bool> Deny(
        string userCode,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The runner's poll. On the first poll after Allow it creates the API key - revoking the key the runner said it
    /// replaces, see <see cref="Start"/> - and returns it. Null when no request has this device code. The caller must
    /// have opened a transaction.
    /// </summary>
    Task<AgentSignInPollResult?> Poll(
        string deviceCode,
        CancellationToken cancellationToken);
}

/// <param name="DeviceCode">The runner's secret for polling - never stored, only its hash.</param>
/// <param name="UserCode">The code to show to the person and put into the Allow page's link.</param>
public sealed record AgentSignInStartResult(
    string DeviceCode,
    string UserCode,
    DateTime ExpiresAt,
    TimeSpan PollInterval);

/// <param name="ApiKey">Only for <see cref="AgentSignInPollOutcome.Approved"/>.</param>
/// <param name="OrganizationId">Only for <see cref="AgentSignInPollOutcome.Approved"/>.</param>
/// <param name="UserId">Only for <see cref="AgentSignInPollOutcome.Approved"/>.</param>
public sealed record AgentSignInPollResult(
    AgentSignInPollOutcome Outcome,
    ApiKeyCreationResult? ApiKey = null,
    long? OrganizationId = null,
    Guid? UserId = null);

/// <summary>
/// What a poll tells the runner.
/// </summary>
public enum AgentSignInPollOutcome
{
    /// <summary>
    /// Nobody answered yet - poll again after the interval.
    /// </summary>
    Pending,

    /// <summary>
    /// The runner polled faster than the interval - wait longer between polls.
    /// </summary>
    SlowDown,

    /// <summary>
    /// The person denied it.
    /// </summary>
    Denied,

    /// <summary>
    /// The sign-in timed out, or its key was already handed out - start a new one.
    /// </summary>
    Expired,

    /// <summary>
    /// The person allowed it: the result carries the new API key. Returned once.
    /// </summary>
    Approved,
}

public class CoreAgentSignInService(
    DatabaseContext context,
    ICoreApiKeysService coreApiKeysService,
    IDateTimeProvider dateTimeProvider)
    : ICoreAgentSignInService
{
    /// <summary>
    /// How long the person has to click Allow, and the runner to pick up the key after that.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How often the runner is asked to poll.
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Polls closer together than this are told to slow down - a little under <see cref="PollInterval"/>, so a runner
    /// polling on time isn't refused for network jitter.
    /// </summary>
    private static readonly TimeSpan MinPollInterval = PollInterval - TimeSpan.FromSeconds(1);

    public async Task<AgentSignInStartResult> Start(
        string clientName,
        string machineName,
        string? replacesApiKey,
        CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;

        Guid? replacesApiKeyId = null;
        if (!string.IsNullOrEmpty(replacesApiKey))
        {
            var replacedHash = SecretHash.Compute(replacesApiKey);
            replacesApiKeyId = await context.ApiKeys
                .Where(x => x.KeyHash == replacedHash && x.RevokedAt == null)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var deviceCode = StringGenerator.GenerateAgentSignInDeviceCode();

        // A user code equal to an earlier one (about 1 in 10^12 per existing row) fails on the unique index: the runner
        // reports the error and setup is run again.
        var request = new AgentSignInRequest
        {
            Id = Guid.CreateVersion7(),
            DeviceCodeHash = SecretHash.Compute(deviceCode),
            UserCode = StringGenerator.GenerateAgentSignInUserCode(),
            ClientName = clientName,
            MachineName = machineName,
            State = AgentSignInState.Pending,
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
            ReplacesApiKeyId = replacesApiKeyId,
        };

        context.AgentSignInRequests.Add(request);
        await context.SaveChangesAsync(cancellationToken);

        return new AgentSignInStartResult(deviceCode, request.UserCode, request.ExpiresAt, PollInterval);
    }

    public async Task<bool> Approve(
        string userCode,
        Guid userId,
        long organizationId,
        CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;

        var updatedCount = await PendingRequest(userCode, now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.State, AgentSignInState.Approved)
                    .SetProperty(x => x.UserId, userId)
                    .SetProperty(x => x.OrganizationId, organizationId)
                    .SetProperty(x => x.DecidedAt, now),
                cancellationToken);

        return updatedCount > 0;
    }

    public async Task<bool> Deny(
        string userCode,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;

        var updatedCount = await PendingRequest(userCode, now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.State, AgentSignInState.Denied)
                    .SetProperty(x => x.UserId, userId)
                    .SetProperty(x => x.DecidedAt, now),
                cancellationToken);

        return updatedCount > 0;
    }

    public async Task<AgentSignInPollResult?> Poll(
        string deviceCode,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var deviceCodeHash = SecretHash.Compute(deviceCode);

        var request = await context.AgentSignInRequests
            .Where(x => x.DeviceCodeHash == deviceCodeHash)
            .Select(x => new
            {
                x.Id,
                x.State,
                x.ExpiresAt,
                x.LastPolledAt,
                x.ClientName,
                x.MachineName,
                x.UserId,
                x.OrganizationId,
                x.ReplacesApiKeyId,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (request is null)
            return null;

        var now = dateTimeProvider.UtcNow;

        await context.AgentSignInRequests
            .Where(x => x.Id == request.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.LastPolledAt, now),
                cancellationToken);

        if (request.ExpiresAt <= now || request.State == AgentSignInState.Completed)
            return new AgentSignInPollResult(AgentSignInPollOutcome.Expired);

        if (request.State == AgentSignInState.Denied)
            return new AgentSignInPollResult(AgentSignInPollOutcome.Denied);

        if (request.State == AgentSignInState.Pending)
        {
            return now - request.LastPolledAt < MinPollInterval
                ? new AgentSignInPollResult(AgentSignInPollOutcome.SlowDown)
                : new AgentSignInPollResult(AgentSignInPollOutcome.Pending);
        }

        // Approved: hand the key out once - a concurrent poll that loses this update sees the request as used up.
        var completedCount = await context.AgentSignInRequests
            .Where(x => x.Id == request.Id && x.State == AgentSignInState.Approved)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.State, AgentSignInState.Completed)
                    .SetProperty(x => x.CompletedAt, now),
                cancellationToken);

        if (completedCount == 0)
            return new AgentSignInPollResult(AgentSignInPollOutcome.Expired);

        var organizationId = request.OrganizationId!.Value;
        var userId = request.UserId!.Value;
        // RevokeAsync only touches the user's own key in this organization, so a key of someone else, or of another
        // organization the person picked this time, is left alone.
        if (request.ReplacesApiKeyId is { } replacedKeyId)
        {
            await coreApiKeysService.RevokeAsync(
                organizationId,
                userId,
                replacedKeyId,
                cancellationToken);
        }

        var apiKey = await coreApiKeysService.CreateAsync(
            organizationId,
            userId,
            KeyName(request.ClientName, request.MachineName),
            cancellationToken);

        await context.AgentSignInRequests
            .Where(x => x.Id == request.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ApiKeyId, apiKey.Id),
                cancellationToken);

        return new AgentSignInPollResult(AgentSignInPollOutcome.Approved, apiKey, organizationId, userId);
    }

    /// <summary>
    /// The issued key's name - what the key list and issue history show, e.g. "Claude Code agent (dev-laptop)".
    /// </summary>
    public static string KeyName(string clientName, string machineName)
    {
        return $"{clientName} ({machineName})";
    }

    private IQueryable<AgentSignInRequest> PendingRequest(string userCode, DateTime now)
    {
        return context.AgentSignInRequests
            .Where(x => x.UserCode == userCode
                && x.State == AgentSignInState.Pending
                && x.ExpiresAt > now);
    }
}
