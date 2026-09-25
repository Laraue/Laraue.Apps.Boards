using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Settings for calling Laraue.Apps.Identity's internal gRPC API (global user identity - see
/// <see cref="ICoreUserService.CreateIfTelegramIdNotExists"/>).
/// </summary>
public class IdentityOptions
{
    /// <summary>
    /// Base address of Laraue.Apps.Identity.InternalApiHost's gRPC endpoint.
    /// </summary>
    [Required]
    [Url]
    public required string GrpcUrl { get; set; }
}
