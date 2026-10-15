using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

/// <summary>
/// Both schemes: the user token alone is enough, and the organization token, when the browser has
/// one, says which organization the user was in.
/// </summary>
[Authorize(AuthenticationSchemes = $"{AuthSchemas.User},{AuthSchemas.Organization}")]
[ApiController]
[Route("/api/feedback")]
public class FeedbackController(IUserFeedbackService service) : ControllerBase
{
    [HttpPost]
    public Task<SendFeedbackResponse> Send(
        [FromBody] SendFeedbackRequest request,
        CancellationToken ct)
    {
        return service.Send(
            HttpContext.User.GetId(),
            HttpContext.User.FindOrganizationId(),
            request,
            ct);
    }
}
