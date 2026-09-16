using Grpc.Core;
using Laraue.Apps.Billing.Internal.Contracts;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// Reserve/commit/cancel AI token spend against the Billing service. The one public entry point
/// for reserving, <see cref="ReserveTokensAsync"/>, takes a plain <c>organizationId</c> rather than
/// exposing Billing's own personal-vs-organization rpc split - Boards' callers (an AI-summarize
/// call site) don't know in advance whether a given organization is personal or a real team, so
/// resolving that fork from live data is this wrapper's job, not a flag the caller has to supply.
/// This doesn't reintroduce the "isOrganization bool" shape Billing itself rejected for its own
/// wire contract - that decision was about a surface with multiple callers who each already know
/// which kind they mean; here there's exactly one caller-facing operation ("spend tokens for this
/// org's AI call") and the personal/team resolution is an internal implementation detail.
/// </summary>
public interface IBillingTokenClient
{
    /// <summary>
    /// Reserves tokens for <paramref name="organizationId"/>, billing to
    /// <paramref name="userId"/> if the organization is personal or to the organization's
    /// <see cref="Organization.BillingId"/> if it's a real team. Throws
    /// <see cref="InsufficientTokenBalanceException"/> if Billing rejects the reservation for
    /// insufficient balance.
    /// </summary>
    Task<Guid> ReserveTokensAsync(
        long organizationId,
        Guid userId,
        int inputTokensCount,
        int maxOutputTokensCount,
        CancellationToken cancellationToken);

    Task CommitTokensSpentAsync(Guid tokenTransactionId, int actualOutputTokensCount, CancellationToken cancellationToken);

    Task CancelTokensReservationAsync(Guid tokenTransactionId, string error, CancellationToken cancellationToken);
}

public class BillingTokenClient(DatabaseContext context, TokenService.TokenServiceClient client) : IBillingTokenClient
{
    public async Task<Guid> ReserveTokensAsync(
        long organizationId,
        Guid userId,
        int inputTokensCount,
        int maxOutputTokensCount,
        CancellationToken cancellationToken)
    {
        var organization = await context.Organizations
            .Where(o => o.Id == organizationId)
            .Select(o => new { o.Type, o.BillingId })
            .SingleAsync(cancellationToken);

        try
        {
            var response = organization.Type == OrganizationType.Personal
                ? await client.ReservePersonalTokensAsync(
                    new ReservePersonalTokensRequest
                    {
                        UserId = userId.ToString(),
                        InputTokensCount = inputTokensCount,
                        MaxOutputTokensCount = maxOutputTokensCount,
                    },
                    cancellationToken: cancellationToken)
                : await client.ReserveOrganizationTokensAsync(
                    new ReserveOrganizationTokensRequest
                    {
                        OrganizationId = organization.BillingId!.Value.ToString(),
                        InputTokensCount = inputTokensCount,
                        MaxOutputTokensCount = maxOutputTokensCount,
                    },
                    cancellationToken: cancellationToken);

            return Guid.Parse(response.TokenTransactionId);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition)
        {
            throw new InsufficientTokenBalanceException(ex.Status.Detail);
        }
    }

    public Task CommitTokensSpentAsync(Guid tokenTransactionId, int actualOutputTokensCount, CancellationToken cancellationToken)
    {
        return client.CommitTokensSpentAsync(
            new CommitTokensSpentRequest
            {
                TokenTransactionId = tokenTransactionId.ToString(),
                ActualOutputTokensCount = actualOutputTokensCount,
            },
            cancellationToken: cancellationToken).ResponseAsync;
    }

    public Task CancelTokensReservationAsync(Guid tokenTransactionId, string error, CancellationToken cancellationToken)
    {
        return client.CancelTokensReservationAsync(
            new CancelTokensReservationRequest
            {
                TokenTransactionId = tokenTransactionId.ToString(),
                Error = error,
            },
            cancellationToken: cancellationToken).ResponseAsync;
    }
}
