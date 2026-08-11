using System.Text.RegularExpressions;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Extgstate;
using iText.IO.Font;
using iText.IO.Font.Constants;

namespace PrintingBooksPortal.Services;

public class WatermarkService : IWatermarkService
{
    public byte[] AddHeavyWatermark(byte[] pdfBytes, string shopName, string userName, DateTime timestamp)
    {
        return ApplyWatermark(pdfBytes, shopName, userName, timestamp, enabled: true);
    }

    public byte[] ApplyWatermark(byte[] pdfBytes, string shopName, string userName, DateTime timestamp, bool enabled)
    {
        return ApplyWatermark(pdfBytes, shopName, userName, timestamp, enabled, null);
    }

    public byte[] ApplyWatermark(byte[] pdfBytes, string shopName, string userName, DateTime timestamp, bool enabled, string? customText)
    {
        if (!enabled)
        {
            return pdfBytes;
        }

        string watermarkText = customText ?? $"LICENSED TO: {shopName}\nUSER: {userName}\nDATE: {timestamp:yyyy-MM-dd HH:mm}\nDO NOT DISTRIBUTE";

        // Replace placeholders
        watermarkText = watermarkText
            .Replace("{shopName}", shopName)
            .Replace("{userName}", userName)
            .Replace("{date}", timestamp.ToString("yyyy-MM-dd HH:mm"))
            .Replace("{timestamp}", timestamp.ToString("yyyy-MM-dd HH:mm:ss"));

        return ApplyWatermarkCore(pdfBytes, watermarkText);
    }

    public byte[] ApplyWatermarkWithTenant(byte[] pdfBytes, string tenantName, string shopName, string userName, DateTime timestamp, bool enabled, string? customText)
    {
        if (!enabled)
        {
            return pdfBytes;
        }

        string watermarkText = customText ?? $"LICENSED TO: {tenantName} / {shopName}\nUSER: {userName}\nDATE: {timestamp:yyyy-MM-dd HH:mm}\nDO NOT DISTRIBUTE";

        // Replace placeholders
        watermarkText = watermarkText
            .Replace("{tenantName}", string.IsNullOrEmpty(tenantName) ? "NA" : tenantName)
            .Replace("{shopName}", shopName)
            .Replace("{userName}", userName)
            .Replace("{date}", timestamp.ToString("yyyy-MM-dd HH:mm"))
            .Replace("{timestamp}", timestamp.ToString("yyyy-MM-dd HH:mm:ss"));

        return ApplyWatermarkCore(pdfBytes, watermarkText);
    }

    private static byte[] ApplyWatermarkCore(byte[] pdfBytes, string watermarkText)
    {
        // iText7 (not PdfSharpCore): handles xref-stream PDFs (Acrobat 6+/Word/Canva)
        // natively and loads watermark fonts by absolute path, so watermarking no
        // longer 500s on the Linux container (see PdfFontResolver for the
        // PdfSharpCore-side fallback used by the receipt generator).
        using var inputStream = new MemoryStream(pdfBytes);
        using var outputStream = new MemoryStream();

        var reader = new PdfReader(inputStream);
        var writer = new PdfWriter(outputStream);
        using var document = new PdfDocument(reader, writer);

        var font = LoadWatermarkFont();
        var fontSize = 40f;
        var lineHeight = fontSize * 1.35f;
        var lines = watermarkText.Split('\n');

        // Widest line drives horizontal placement so the block stays centered.
        float blockWidth = 0f;
        foreach (var line in lines)
            blockWidth = Math.Max(blockWidth, font.GetWidth(line, fontSize));
        var blockHeight = lines.Length * lineHeight;

        for (var pageIndex = 1; pageIndex <= document.GetNumberOfPages(); pageIndex++)
        {
            var page = document.GetPage(pageIndex);
            var pageSize = page.GetPageSize();
            var cx = pageSize.GetWidth() / 2f;
            var cy = pageSize.GetHeight() / 2f;

            var canvas = new PdfCanvas(page);
            canvas.SaveState();

            // 50% opaque gray fill (matches previous PdfSharpCore rendering).
            canvas.SetExtGState(new PdfExtGState().SetFillOpacity(0.5f));
            canvas.SetColor(ColorConstants.GRAY, false);

            // Rotate text by -45° around the page center via a text matrix.
            var sin = (float)Math.Sin(-Math.PI / 4);
            var cos = (float)Math.Cos(-Math.PI / 4);
            float a = cos, b = sin, c = -sin, d = cos;
            float e = cx - cos * blockWidth / 2f - sin * blockHeight / 2f;
            float f = cy + cos * blockHeight / 2f - sin * blockWidth / 2f;

            canvas.BeginText();
            canvas.SetFontAndSize(font, fontSize);
            for (var i = 0; i < lines.Length; i++)
            {
                // Next line sits lineHeight below in text space; map through the
                // same rotation matrix (c,d columns carry the y-translation).
                var lineY = -i * lineHeight;
                canvas.SetTextMatrix(a, b, c, d, e + c * lineY, f + d * lineY);
                canvas.ShowText(lines[i]);
            }
            canvas.EndText();
            canvas.RestoreState();
        }

        document.Close();
        return outputStream.ToArray();
    }

    /// <summary>
    /// Loads a bold sans-serif for the watermark from the fonts installed in the
    /// container (Dockerfile installs fonts-liberation + fonts-dejavu-core), with
    /// a Windows Arial fallback for local dev. Never throws when unreadable:
    /// the last resort is the built-in Helvetica-Bold.
    /// </summary>
    private static PdfFont LoadWatermarkFont()
    {
        var candidates = new List<string>
        {
            "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
            "/usr/share/fonts/truetype/liberation2/LiberationSans-Bold.ttf",
            "/usr/share/fonts/liberation/LiberationSans-Bold.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
            "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf",
        };

        if (OperatingSystem.IsWindows())
        {
            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            candidates.InsertRange(0, new[]
            {
                Path.Combine(windir, "Fonts", "arialbd.ttf"),
                Path.Combine(windir, "Fonts", "DejaVuSans-Bold.ttf"),
            });
        }

        foreach (var path in candidates)
        {
            try
            {
                if (File.Exists(path))
                    return PdfFontFactory.CreateFont(path, PdfEncodings.IDENTITY_H);
            }
            catch
            {
                // try the next candidate
            }
        }

        return PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
    }
}