using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// "Connect this machine" for the agent runner (BRD-309): `boards-agent setup` starts a sign-in and polls for its API
/// key, while the person allows it on the web app's Allow page for one of their organizations. See
/// <see cref="ICoreAgentSignInService"/>. Not OAuth - a general sign-in server belongs to Laraue.Apps.Identity.
/// </summary>
public interface IAgentSignInService
{
    /// <summary>
    /// Starts a sign-in. Anonymous - the runner has no credentials yet.
    /// </summary>
    Task<StartAgentSignInResponse> Start(
        StartAgentSignInRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// The runner's poll. Anonymous - the device code is the secret. 404 for an unknown device code.
    /// </summary>
    Task<PollAgentSignInResponse> Poll(
        PollAgentSignInRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// The sign-in for the Allow page. 404 when it doesn't exist, expired or was already answered.
    /// </summary>
    Task<AgentSignInDto> Get(
        string userCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Allows the sign-in for one of the user's organizations. 404 when the user isn't a member of it, or the sign-in
    /// doesn't exist, expired or was already answered.
    /// </summary>
    Task Approve(
        string userCode,
        Guid userId,
        ApproveAgentSignInRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Denies the sign-in. 404 when it doesn't exist, expired or was already answered.
    /// </summary>
    Task Deny(
        string userCode,
        Guid userId,
        CancellationToken cancellationToken);
}

public class AgentSignInService(
    DatabaseContext context,
    ICoreAgentSignInService coreAgentSignInService,
    IAccessService accessService,
    IDateTimeProvider dateTimeProvider)
    : IAgentSignInService
{
    public async Task<StartAgentSignInResponse> Start(
        StartAgentSignInRequest request,
        CancellationToken cancellationToken)
    {
        var result = await coreAgentSignInService.Start(
            request.MachineName.Trim(),
            cancellationToken);

        return new StartAgentSignInResponse
        {
            DeviceCode = result.DeviceCode,
            UserCode = result.UserCode,
            VerificationPath = $"/agent-sign-in?code={Uri.EscapeDataString(result.UserCode)}",
            ExpiresAt = result.ExpiresAt,
            IntervalSeconds = (int)result.PollInterval.TotalSeconds,
        };
    }

    public async Task<PollAgentSignInResponse> Poll(
        PollAgentSignInRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var result = await coreAgentSignInService.Poll(request.DeviceCode, cancellationToken)
            ?? throw new NotFoundException(string.Format(ErrorMessages.EntityNotFound, "AgentSignIn", "device code"));

        await transaction.CommitAsync(cancellationToken);

        if (result.Outcome != AgentSignInPollOutcome.Approved)
            return new PollAgentSignInResponse { Outcome = result.Outcome };

        var organization = await context.Organizations
            .Where(x => x.Id == result.OrganizationId)
            .Select(x => new { x.Id, x.Name })
            .FirstAsync(cancellationToken);

        return new PollAgentSignInResponse
        {
            Outcome = result.Outcome,
            ApiKey = result.ApiKey!.RawKey,
            ApiKeyId = result.ApiKey.Id,
            OrganizationId = organization.Id,
            OrganizationName = organization.Name,
            UserId = result.UserId,
        };
    }

    public async Task<AgentSignInDto> Get(
        string userCode,
        CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeUserCode(userCode);
        var now = dateTimeProvider.UtcNow;

        var request = await context.AgentSignInRequests
            .Where(x => x.UserCode == normalizedCode
                && x.State == AgentSignInState.Pending
                && x.ExpiresAt > now)
            .Select(x => new
            {
                x.UserCode,
                x.MachineName,
                x.CreatedAt,
                x.ExpiresAt,
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFound(userCode);

        return new AgentSignInDto
        {
            UserCode = request.UserCode,
            MachineName = request.MachineName,
            KeyName = CoreAgentSignInService.KeyName(request.MachineName),
            CreatedAt = request.CreatedAt,
            ExpiresAt = request.ExpiresAt,
        };
    }

    public async Task Approve(
        string userCode,
        Guid userId,
        ApproveAgentSignInRequest request,
        CancellationToken cancellationToken)
    {
        var isMember = await accessService.GetOrganizations(
            userId,
            organizationUsers => organizationUsers
                .AnyAsync(
                    x => x.OrganizationId == request.OrganizationId
                        && x.Organization!.DeletedAt == null,
                    cancellationToken));

        if (!isMember)
        {
            throw new NotFoundException(
                string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Organization", request.OrganizationId));
        }

        var approved = await coreAgentSignInService.Approve(
            NormalizeUserCode(userCode),
            userId,
            request.OrganizationId,
            cancellationToken);

        if (!approved)
            throw NotFound(userCode);
    }

    public async Task Deny(
        string userCode,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var denied = await coreAgentSignInService.Deny(
            NormalizeUserCode(userCode),
            userId,
            cancellationToken);

        if (!denied)
            throw NotFound(userCode);
    }

    /// <summary>
    /// Accepts the code as a person might type it: any case, surrounding spaces.
    /// </summary>
    private static string NormalizeUserCode(string userCode)
    {
        return userCode.Trim().ToUpperInvariant();
    }

    private static NotFoundException NotFound(string userCode)
    {
        return new NotFoundException(string.Format(ErrorMessages.EntityNotFound, "AgentSignIn", userCode));
    }
}

public record StartAgentSignInRequest
{
    /// <summary>
    /// The runner's machine - shown on the Allow page and put in the key's name.
    /// </summary>
    [Required]
    [MinLength(1)]
    [MaxLength(Constraints.MaxAgentSignInMachineNameLength)]
    public required string MachineName { get; init; }
}

public record StartAgentSignInResponse
{
    /// <summary>
    /// The runner's secret for polling. Keep it to the runner - whoever has it gets the key.
    /// </summary>
    public required string DeviceCode { get; init; }

    /// <summary>
    /// The code to show in the terminal, so the person can compare it with the Allow page.
    /// </summary>
    public required string UserCode { get; init; }

    /// <summary>
    /// The Allow page, relative to the Boards web app's address, e.g. <c>/agent-sign-in?code=WXYZ-2345</c>.
    /// </summary>
    public required string VerificationPath { get; init; }

    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// How long to wait between polls.
    /// </summary>
    public required int IntervalSeconds { get; init; }
}

public record PollAgentSignInRequest
{
    [Required]
    public required string DeviceCode { get; init; }
}

public record PollAgentSignInResponse
{
    public required AgentSignInPollOutcome Outcome { get; init; }

    /// <summary>
    /// The raw API key - only with <see cref="AgentSignInPollOutcome.Approved"/>, and only this once.
    /// </summary>
    public string? ApiKey { get; init; }

    public Guid? ApiKeyId { get; init; }

    public long? OrganizationId { get; init; }

    public string? OrganizationName { get; init; }

    public Guid? UserId { get; init; }
}

public record AgentSignInDto
{
    public required string UserCode { get; init; }

    public required string MachineName { get; init; }

    /// <summary>
    /// The name the API key will get, as the person will later see it in their key list.
    /// </summary>
    public required string KeyName { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime ExpiresAt { get; init; }
}

public record ApproveAgentSignInRequest
{
    /// <summary>
    /// The organization the key is for - one the user is a member of.
    /// </summary>
    public required long OrganizationId { get; init; }
}
