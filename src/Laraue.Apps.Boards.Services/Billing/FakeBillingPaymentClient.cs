namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// Stands in for <see cref="BillingPaymentClient"/> when running locally without a live Billing
/// instance (see "MockExternalServices" in <c>WebApplicationBuilderExtensions.AddCoreServices</c>).
/// Returns a made-up checkout address, nothing is charged.
/// </summary>
public class FakeBillingPaymentClient : IBillingPaymentClient
{
    public Task<BillingCheckout> CreateCheckoutAsync(
        long organizationId,
        Guid userId,
        BillingItemKind kind,
        Guid itemId,
        string currencyCode,
        CancellationToken cancellationToken)
    {
        var paymentId = Guid.NewGuid();

        return Task.FromResult(new BillingCheckout(
            paymentId,
            $"https://localhost/fake-checkout?paymentId={paymentId}&item={itemId}&currency={currencyCode}"));
    }
}
