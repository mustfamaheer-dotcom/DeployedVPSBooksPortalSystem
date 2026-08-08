using System.Text.RegularExpressions;
using PdfSharpCore.Fonts;

namespace PrintingBooksPortal.Services;

/// <summary>
/// PdfSharpCore font resolver for headless Linux containers (no fontconfig,
/// no Arial glyphs). Maps Arial/Helvetica to metrically-compatible Liberation
/// Sans (fonts-liberation) or DejaVu Sans, scanning the standard font
/// directories at first use.
/// </summary>
public sealed class PdfFontResolver : IFontResolver
{
    private static readonly object Sync = new();
    private static List<FontCandidate>? _candidates;

    private sealed class FontCandidate
    {
        public required string Path { get; init; }
        public required string Family { get; init; }
        public bool Bold { get; init; }
        public bool Italic { get; init; }
    }

    public string DefaultFontName => "Arial";

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var best = Select(familyName, isBold, isItalic);
        return best == null ? null : new FontResolverInfo(best.Path);
    }

    public byte[]? GetFont(string faceName)
    {
        try
        {
            return File.Exists(faceName) ? File.ReadAllBytes(faceName) : null;
        }
        catch
        {
            return null;
        }
    }

    private static FontCandidate? Select(string familyName, bool isBold, bool isItalic)
    {
        var family = Normalize(familyName);
        var alias = family is "arial" or "helvetica" ? "liberation sans" : family;

        var candidates = GetCandidates();
        var bucket = candidates.Where(c => Normalize(c.Family) == alias || Normalize(c.Family) == family).ToList();

        if (bucket.Count == 0 && alias != family)
            bucket = candidates.Where(c => Normalize(c.Family) == family).ToList();

        if (bucket.Count == 0)
        {
            // Last resort: any sans-serif as a readable fallback
            bucket = candidates.Where(c => Normalize(c.Family) is "liberation sans" or "dejavu sans").ToList();
        }

        var exact = bucket.FirstOrDefault(c => c.Bold == isBold && c.Italic == isItalic);
        if (exact != null)
            return exact;

        var boldOk = bucket.FirstOrDefault(c => c.Bold == isBold);
        if (boldOk != null)
            return boldOk;

        return bucket.FirstOrDefault() ?? bucket.FirstOrDefault();
    }

    private static List<FontCandidate> GetCandidates()
    {
        lock (Sync)
        {
            if (_candidates != null)
                return _candidates;

            var list = new List<FontCandidate>();
            foreach (var root in FontRoots())
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (var file in Directory.EnumerateFiles(root, "*.tt?", SearchOption.AllDirectories).Concat(
                             Directory.EnumerateFiles(root, "*.otf", SearchOption.AllDirectories)))
                {
                    if (!file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) &&
                        !file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parsed = ParseFileName(Path.GetFileNameWithoutExtension(file));
                    if (string.IsNullOrWhiteSpace(parsed.Family))
                        continue;

                    list.Add(new FontCandidate
                    {
                        Path = file,
                        Family = parsed.Family,
                        Bold = parsed.Bold,
                        Italic = parsed.Italic
                    });
                }
            }

            _candidates = list;
            return list;
        }
    }

    private static string[] FontRoots()
    {
        var roots = new List<string>
        {
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            "/usr/share/fonts/truetype"
        };

        if (OperatingSystem.IsWindows())
        {
            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windir))
                roots.Add(Path.Combine(windir, "Fonts"));
        }

        return roots.ToArray();
    }

    private static (string Family, bool Bold, bool Italic) ParseFileName(string name)
    {
        var lower = name.ToLowerInvariant();
        var bold = lower.Contains("bold") || lower.Contains("semibold") || lower.Contains("heavy");
        var italic = lower.Contains("italic") || lower.Contains("oblique");

        var family = Regex.Replace(
            name,
            @"(?i)[\s\-_]*(bold|italic|oblique|semibold|black|light|regular|medium|extralight|thin|book|demi|heavy|narrow|condensed)*(?:[\s\-_]*(bold|italic|oblique|semibold|medium|regular))*$",
            string.Empty).Trim();

        return (family, bold, italic);
    }

    private static string Normalize(string family)
        => family.Trim().ToLowerInvariant();
}