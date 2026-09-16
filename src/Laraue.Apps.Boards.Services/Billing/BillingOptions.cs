namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// Settings for the internal gRPC connection to the Billing service
/// (<c>Laraue.Apps.Billing.InternalApiHost</c>), used to reserve/commit/cancel AI token spend.
/// </summary>
public class BillingOptions
{
    /// <summary>
    /// gRPC endpoint address, e.g. <c>http://localhost:5263</c> in local dev
    /// (<c>InternalApiHost</c>'s <c>Kestrel:GrpcPort</c>).
    /// </summary>
    public required string GrpcUrl { get; set; }
}
