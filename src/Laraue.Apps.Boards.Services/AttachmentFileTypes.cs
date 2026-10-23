using System.IO.Compression;
using System.Text;
using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// A file type an attachment may have. <see cref="Name"/> is how it's listed to people (e.g. in an
/// "unsupported file" error), <see cref="MimeType"/> what's stored and served for it - whatever the
/// uploader declared.
/// </summary>
public sealed record AttachmentFileType(
    string Name,
    string MimeType,
    string Extension,
    AttachmentType AttachmentType,
    bool IsText)
{
    /// <summary>
    /// Images keep the old 3MB limit, documents get more. Both stay well under the 20MB a bot can
    /// download through the Bot API, since Telegram is the storage.
    /// </summary>
    public int MaxSizeBytes => AttachmentType == AttachmentType.Image
        ? AttachmentFileTypes.MaxImageSizeBytes
        : AttachmentFileTypes.MaxDocumentSizeBytes;
}

/// <summary>
/// The file types attachments accept, recognized by their content - the declared mime type and, for
/// binary formats, the extension are ignored, so a renamed file is what its bytes say it is.
/// Text formats have no signature, so for them the extension decides, provided the content is UTF-8
/// text. SVG (scripts), HEIC/AVIF (browsers can't show them) and anything else are rejected.
/// </summary>
public static class AttachmentFileTypes
{
    public const int MaxImageSizeBytes = 3_000_000;
    public const int MaxDocumentSizeBytes = 10_000_000;

    /// <summary>The largest file of any type - what to check before reading a file at all.</summary>
    public const int MaxFileSizeBytes = MaxDocumentSizeBytes;

    public static readonly AttachmentFileType Jpeg = new("JPEG", "image/jpeg", ".jpg", AttachmentType.Image, false);
    public static readonly AttachmentFileType Png = new("PNG", "image/png", ".png", AttachmentType.Image, false);
    public static readonly AttachmentFileType Webp = new("WebP", "image/webp", ".webp", AttachmentType.Image, false);
    public static readonly AttachmentFileType Gif = new("GIF", "image/gif", ".gif", AttachmentType.Image, false);
    public static readonly AttachmentFileType Pdf = new("PDF", "application/pdf", ".pdf", AttachmentType.Document, false);
    public static readonly AttachmentFileType Docx = new(
        "DOCX",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".docx",
        AttachmentType.Document,
        false);
    public static readonly AttachmentFileType Doc = new("DOC", "application/msword", ".doc", AttachmentType.Document, false);
    public static readonly AttachmentFileType Markdown = new("Markdown", "text/markdown", ".md", AttachmentType.Document, true);
    public static readonly AttachmentFileType PlainText = new("TXT", "text/plain", ".txt", AttachmentType.Document, true);
    public static readonly AttachmentFileType Json = new("JSON", "application/json", ".json", AttachmentType.Document, true);

    public static readonly IReadOnlyList<AttachmentFileType> All =
        [Jpeg, Png, Webp, Gif, Pdf, Docx, Doc, Markdown, PlainText, Json];

    public static readonly IReadOnlyList<AttachmentFileType> Images = All
        .Where(x => x.AttachmentType == AttachmentType.Image)
        .ToArray();

    public static readonly IReadOnlyList<AttachmentFileType> Documents = All
        .Where(x => x.AttachmentType == AttachmentType.Document)
        .ToArray();

    /// <summary>"JPEG, PNG, WebP, GIF" - for error messages listing what's accepted.</summary>
    public static string ImageNames => string.Join(", ", Images.Select(x => x.Name));

    /// <summary>"PDF, DOCX, DOC, Markdown, TXT, JSON" - for error messages listing what's accepted.</summary>
    public static string DocumentNames => string.Join(", ", Documents.Select(x => x.Name));

    public static int MaxImageSizeMegabytes => MaxImageSizeBytes / 1_000_000;
    public static int MaxDocumentSizeMegabytes => MaxDocumentSizeBytes / 1_000_000;

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> OleSignature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly UTF8Encoding StrictUtf8= new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The type a stored file was saved as. "image/jpg" is what the first uploads recorded for JPEG.
    /// </summary>
    public static AttachmentFileType? FindByMimeType(string? mimeType)
    {
        if (mimeType == "image/jpg")
            return Jpeg;

        return All.FirstOrDefault(x => x.MimeType == mimeType);
    }

    /// <summary>
    /// Recognizes the file's type from its content, or returns null when it isn't a supported one.
    /// </summary>
    public static AttachmentFileType? Detect(string fileName, byte[] content)
    {
        var span = content.AsSpan();

        if (span.StartsWith(JpegSignature))
            return Jpeg;

        if (span.StartsWith(PngSignature))
            return Png;

        if (span.StartsWith("GIF87a"u8) || span.StartsWith("GIF89a"u8))
            return Gif;

        if (span.Length >= 12 && span.StartsWith("RIFF"u8) && span[8..12].SequenceEqual("WEBP"u8))
            return Webp;

        if (span.StartsWith("%PDF-"u8))
            return Pdf;

        if (span.StartsWith("PK\x03\x04"u8))
            return IsWordDocument(content) ? Docx : null;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        // OLE compound files are also .xls, .ppt, .msi..., so only the extension tells a .doc apart.
        if (span.StartsWith(OleSignature))
            return extension == Doc.Extension ? Doc : null;

        var textType = extension switch
        {
            ".md" or ".markdown" => Markdown,
            ".txt" => PlainText,
            ".json" => Json,
            _ => null,
        };

        return textType is not null && IsUtf8Text(span) ? textType : null;
    }

    /// <summary>
    /// A file name safe to store and to send back in a Content-Disposition header: no directories,
    /// control characters or quotes, at most 255 characters with the extension kept, never empty.
    /// </summary>
    public static string SanitizeFileName(string? fileName, AttachmentFileType type)
    {
        var name = fileName ?? string.Empty;
        name = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        name = new string(name
            .Where(c => !char.IsControl(c) && c is not '"' and not '<' and not '>' and not ':' and not '*' and not '?' and not '|')
            .ToArray())
            .Trim();

        // ".txt" or "..." has no name to keep; otherwise leading dots only hide the file.
        name = string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(name).Trim('.'))
            ? "file" + type.Extension
            : name.TrimStart('.');

        const int maxLength = 255;
        if (name.Length > maxLength)
        {
            var extension = Path.GetExtension(name);
            if (extension.Length > 16)
                extension = string.Empty;

            name = name[..(maxLength - extension.Length)] + extension;
        }

        return name;
    }

    /// <summary>
    /// A .docx is a zip with the main document part. Macro-enabled ones (.docm) carry a VBA project
    /// and are rejected.
    /// </summary>
    private static bool IsWordDocument(byte[] content)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);

            return archive.GetEntry("word/document.xml") is not null
                && archive.GetEntry("word/vbaProject.bin") is null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool IsUtf8Text(ReadOnlySpan<byte> content)
    {
        if (content.Contains((byte)0))
            return false;

        try
        {
            StrictUtf8.GetCharCount(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
