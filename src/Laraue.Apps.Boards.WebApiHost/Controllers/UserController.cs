using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.User)]
[ApiController]
[Route("/api/user")]
public class UserController(IUserService service) : ControllerBase
{
    [HttpGet("onboarding/{onboardingId}")]
    public async Task<GetOnboardingStatusResponse> GetOnboardingStatus(
        OnboardingId onboardingId,
        CancellationToken cancellationToken)
    {
        return new GetOnboardingStatusResponse
        {
            Status = (await service.GetOnboardingStatus(
                HttpContext.User.GetId(),
                onboardingId,
                cancellationToken))?.ToString(),
        };
    }

    [HttpPut("onboarding/{onboardingId}")]
    public Task SetOnboardingStatus(
        OnboardingId onboardingId,
        [FromBody] SetOnboardingStatusRequest request,
        CancellationToken cancellationToken)
    {
        return service.SetOnboardingStatus(
            HttpContext.User.GetId(),
            onboardingId,
            request.Status,
            cancellationToken);
    }

    [HttpGet]
    public Task<UserDto> GetAsync(CancellationToken ct)
    {
        return service.GetUser(HttpContext.User.GetId(), ct);
    }
    
    [HttpPut("settings/epic-sort-order/{epicSortOrder}")]
    public Task UpdateEpicSortOrder(
        [FromRoute] EpicSortOrder epicSortOrder,
        CancellationToken cancellationToken)
    {
        return service.UpdateEpicSortOrder(HttpContext.User.GetId(), epicSortOrder, cancellationToken);
    }
}
