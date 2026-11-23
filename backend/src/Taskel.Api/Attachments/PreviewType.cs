namespace Taskel.Api.Attachments;

/// <summary>
/// Which stored files Taskel shows instead of only offering them for download: PDF documents and the common
/// picture formats. The type is read from the file's first bytes, never taken from its name or from the type the
/// uploader declared, so a page or a script renamed to .png is still only downloaded. SVG is left out on purpose:
/// it is a document that can carry scripts.
/// </summary>
public static class PreviewType
{
    public const string Pdf = "application/pdf";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    /// <summary>The bytes read to recognise a file.</summary>
    private const int HeaderLength = 12;

    /// <summary>The type to show the file as, or null when it is not one Taskel shows. Leaves the stream where it was.</summary>
    public static async Task<string?> OfAsync(Stream content, CancellationToken ct)
    {
        var header = new byte[HeaderLength];
        var read = 0;
        while (read < header.Length)
        {
            var n = await content.ReadAsync(header.AsMemory(read), ct);
            if (n == 0) break;
            read += n;
        }
        content.Seek(0, SeekOrigin.Begin);
        return Detect(header.AsSpan(0, read));
    }

    public static string? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith("%PDF-"u8)) return Pdf;
        if (header.StartsWith(PngSignature)) return "image/png";
        if (header.StartsWith(JpegSignature)) return "image/jpeg";
        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8)) return "image/gif";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }
}
