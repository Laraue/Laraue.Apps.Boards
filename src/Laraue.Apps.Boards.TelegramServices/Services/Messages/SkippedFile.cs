using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices.Resources;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

/// <summary>
/// A document sent to the bot that wasn't saved as an attachment, and why.
/// </summary>
public sealed record SkippedFile(string FileName, SkippedFileReason Reason)
{
    /// <summary>
    /// The notice for skipped files: a line per file, then what attachments can be. Plain text.
    /// </summary>
    public static string FormatNotice(IEnumerable<SkippedFile> skippedFiles)
    {
        var lines = skippedFiles
            .Select(x => string.Format(
                x.Reason == SkippedFileReason.TooLarge ? Phrases.FileSkippedTooLarge : Phrases.FileSkippedUnsupportedType,
                x.FileName))
            .ToList();

        lines.Add(string.Format(
            Phrases.FileSkippedLimits,
            AttachmentFileTypes.ImageNames,
            AttachmentFileTypes.MaxImageSizeMegabytes,
            AttachmentFileTypes.DocumentNames,
            AttachmentFileTypes.MaxDocumentSizeMegabytes));

        return string.Join('\n', lines);
    }
}
