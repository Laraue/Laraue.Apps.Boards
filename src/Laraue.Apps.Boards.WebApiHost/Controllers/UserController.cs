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
