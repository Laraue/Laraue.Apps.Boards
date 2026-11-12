namespace Laraue.Apps.Boards.WebApiHost;

public class AuthenticateViaStringInitDataRequest
{
    public required string InitData { get; set; }

    /// <summary>
    /// The first page the user opened on the site and the site that sent them there (BRD-295), saved by the frontend.
    /// The Mini App's own <c>start_param</c> (from <see cref="InitData"/>, signed by Telegram) is an explicit source
    /// and wins.
    /// </summary>
    public string? SignUpLandingUrl { get; set; }

    /// <inheritdoc cref="SignUpLandingUrl"/>
    public string? SignUpReferrer { get; set; }
}