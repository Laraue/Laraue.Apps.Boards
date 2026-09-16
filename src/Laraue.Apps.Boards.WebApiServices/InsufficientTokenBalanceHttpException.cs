using System.Net;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// Thrown when Billing rejects an AI-token reservation for insufficient balance
/// (<see cref="Services.Billing.InsufficientTokenBalanceException"/>) - maps to 402, since it's a
/// client-side "you're out of quota" condition, not a server failure.
/// </summary>
public class InsufficientTokenBalanceHttpException(string message)
    : HttpException(HttpStatusCode.PaymentRequired, message);
