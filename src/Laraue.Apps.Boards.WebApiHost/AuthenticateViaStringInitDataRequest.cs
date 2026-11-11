namespace Laraue.Apps.Boards.WebApiHost;

public class AuthenticateViaStringInitDataRequest
{
    public required string InitData { get; set; }

    /// <summary>
    /// Where the user came from (BRD-295), saved by the frontend. Without it, the Mini App's own <c>start_param</c>
    /// (from <see cref="InitData"/>, signed by Telegram) is used.
    /// </summary>
    public string? SignUpSource { get; set; }
}