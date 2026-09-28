using System.Net;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// Thrown when Laraue.Apps.Identity, which holds the user's global profile, can't be reached - maps
/// to 503 since it's a downstream failure, not a client mistake.
/// </summary>
public class ProfileServiceUnavailableException(string message)
    : HttpException(HttpStatusCode.ServiceUnavailable, message);
