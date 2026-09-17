using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[Authorize(AuthenticationSchemes = AuthSchemas.Organization)]
[ApiController]
[Route("/api/billing")]
public class BillingController(IBillingService billingService) : ControllerBase
{
    [HttpGet("summary")]
    public Task<BillingSummary> GetSummary(CancellationToken cancellationToken = default)
    {
        return billingService.GetSummary(HttpContext.User.GetOrganizationAuthData(), cancellationToken);
    }

    [HttpPost("transactions")]
    public Task<ShortPaginatedResult<BillingTransaction>> GetTransactions(
        [FromBody] GetBillingTransactionsRequest request,
        CancellationToken cancellationToken = default)
    {
        return billingService.GetTransactions(
            request with
            {
                AuthData = HttpContext.User.GetOrganizationAuthData(),
            },
            cancellationToken);
    }
}
