using System.IO.Compression;
using Laraue.Apps.Boards.Services;

namespace Laraue.Apps.Boards.IntegrationTests;

public class AttachmentFileTypesTests
{
    [Theory]
    [InlineData("photo.jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData("photo.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData("anim.gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
    [InlineData("pic.webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData("spec.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, "application/pdf")]
    [InlineData("old.doc", new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }, "application/msword")]
    [InlineData("notes.md", new byte[] { 0x23, 0x20, 0x48, 0x69 }, "text/markdown")]
    [InlineData("notes.TXT", new byte[] { 0x48, 0x69 }, "text/plain")]
    [InlineData("config.json", new byte[] { 0x7B, 0x7D }, "application/json")]
    public void Detect_ShouldRecognizeType_WhenContentIsSupported(string fileName, byte[] content, string expectedMimeType)
    {
        Assert.Equal(expectedMimeType, AttachmentFileTypes.Detect(fileName, content)?.MimeType);
    }

    [Fact]
    public void Detect_ShouldGoByContent_WhenExtensionIsWrong()
    {
        // A PNG renamed to .pdf is still a PNG.
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        Assert.Equal(AttachmentFileTypes.Png, AttachmentFileTypes.Detect("report.pdf", png));
    }

    [Theory]
    [InlineData("logo.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
    [InlineData("page.html", "<html><script>alert(1)</script></html>")]
    [InlineData("script.sh", "echo hi")]
    public void Detect_ShouldReject_WhenTextHasUnsupportedExtension(string fileName, string content)
    {
        Assert.Null(AttachmentFileTypes.Detect(fileName, System.Text.Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void Detect_ShouldReject_WhenTextFileIsBinary()
    {
        Assert.Null(AttachmentFileTypes.Detect("notes.txt", [0x4D, 0x5A, 0x00, 0x01]));
        Assert.Null(AttachmentFileTypes.Detect("notes.txt", [0xC3, 0x28]));
    }

    [Fact]
    public void Detect_ShouldReject_WhenOleFileIsNotDoc()
    {
        Assert.Null(AttachmentFileTypes.Detect("sheet.xls", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]));
    }

    [Fact]
    public void Detect_ShouldRecognizeDocx_WhenZipHasWordDocument()
    {
        Assert.Equal(AttachmentFileTypes.Docx, AttachmentFileTypes.Detect("any.bin", Zip("word/document.xml")));
    }

    [Fact]
    public void Detect_ShouldReject_WhenZipIsNotWordDocument()
    {
        Assert.Null(AttachmentFileTypes.Detect("archive.docx", Zip("readme.txt")));
        Assert.Null(AttachmentFileTypes.Detect("macro.docx", Zip("word/document.xml", "word/vbaProject.bin")));
    }

    [Theory]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData("C:\\Users\\me\\report.pdf", "report.pdf")]
    [InlineData("we\"ird<na>me?.md", "weirdname.md")]
    [InlineData(".txt", "file.txt")]
    [InlineData(null, "file.txt")]
    public void SanitizeFileName_ShouldStripUnsafeParts(string? fileName, string expected)
    {
        Assert.Equal(expected, AttachmentFileTypes.SanitizeFileName(fileName, AttachmentFileTypes.PlainText));
    }

    [Fact]
    public void SanitizeFileName_ShouldKeepExtension_WhenNameIsTooLong()
    {
        var name = AttachmentFileTypes.SanitizeFileName(new string('a', 300) + ".pdf", AttachmentFileTypes.Pdf);

        Assert.Equal(255, name.Length);
        Assert.EndsWith(".pdf", name);
    }

    private static byte[] Zip(params string[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
                archive.CreateEntry(entry);
        }

        return stream.ToArray();
    }
}
