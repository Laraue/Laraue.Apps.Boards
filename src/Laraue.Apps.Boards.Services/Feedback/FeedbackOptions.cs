using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.Services.Feedback;

/// <summary>
/// Settings for calling Laraue.Apps.Feedback's internal gRPC API, which stores the feedback users leave
/// in Boards and notifies the team.
/// </summary>
public class FeedbackOptions
{
    /// <summary>
    /// gRPC endpoint address, e.g. <c>http://localhost:5463</c> in local dev
    /// (<c>Laraue.Apps.Feedback.InternalApiHost</c>'s <c>Kestrel:GrpcPort</c>).
    /// </summary>
    [Required]
    [Url]
    public required string GrpcUrl { get; set; }
}
