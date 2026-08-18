using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.User)]
[ApiController]
[Route("/api/user/onboarding")]
public class UserOnboardingController(IUserOnboardingService service) : ControllerBase
{
    [HttpGet("{onboardingId}")]
    public async Task<GetOnboardingStatusResponse> GetStatus(
        OnboardingId onboardingId,
        CancellationToken cancellationToken)
    {
        return new GetOnboardingStatusResponse
        {
            Status = (await service.GetStatus(
                HttpContext.User.GetId(),
                onboardingId,
                cancellationToken))?.ToString(),
        };
    }

    [HttpPut("{onboardingId}")]
    public Task SetStatus(
        OnboardingId onboardingId,
        [FromBody] SetOnboardingStatusRequest request,
        CancellationToken cancellationToken)
    {
        return service.SetStatus(
            HttpContext.User.GetId(),
            onboardingId,
            request.Status,
            cancellationToken);
    }
}
