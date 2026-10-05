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
    [HttpGet("tariff")]
    public Task<TariffName> GetTariffName(CancellationToken cancellationToken = default)
    {
        return billingService.GetTariffName(HttpContext.User.GetOrganizationAuthData(), cancellationToken);
    }

    [HttpGet("summary")]
    public Task<BillingSummary> GetSummary(CancellationToken cancellationToken = default)
    {
        return billingService.GetSummary(HttpContext.User.GetOrganizationAuthData(), cancellationToken);
    }

    /// <summary>
    /// Starts a payment and returns the address to send the customer to. Owner only.
    /// </summary>
    [HttpPost("checkout")]
    public Task<CheckoutDto> CreateCheckout(
        [FromBody] CreateCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        return billingService.CreateCheckout(
            HttpContext.User.GetOrganizationAuthData(),
            request,
            cancellationToken);
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
