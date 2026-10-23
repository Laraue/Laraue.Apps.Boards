using System.Diagnostics.CodeAnalysis;
using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.Services;

public class ExtensionUtility
{
    /// <summary>
    /// The extension a cached file is stored with. Images and videos keep the mime subtype ('jpeg',
    /// 'mp4') the cache has always used; documents get their own extension, since a subtype like
    /// 'vnd.openxmlformats-officedocument.wordprocessingml.document' isn't one.
    /// </summary>
    public static string? GetExtension([NotNullIfNotNull(nameof(mimeType))] string? mimeType)
    {
        if (AttachmentFileTypes.FindByMimeType(mimeType) is { AttachmentType: AttachmentType.Document } document)
            return document.Extension.TrimStart('.');

        return mimeType?.Split("/")[1];
    }
}
