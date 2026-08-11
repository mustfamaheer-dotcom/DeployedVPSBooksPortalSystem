using iText.Kernel.Pdf;

namespace PrintingBooksPortal.Services;

public static class PdfPageSelection
{
    public static int CountPages(byte[] bytes)
    {
        using var reader = new PdfReader(new MemoryStream(bytes));
        using var doc = new PdfDocument(reader);
        return doc.GetNumberOfPages();
    }

    public static int CountPages(string filePath)
    {
        using var reader = new PdfReader(filePath);
        using var doc = new PdfDocument(reader);
        return doc.GetNumberOfPages();
    }

    /// <summary>
    /// Parses a page selection string. Accepts null/empty/"all" for every page,
    /// otherwise a comma-separated list of numbers and ranges, e.g. "1-5, 8, 11-13".
    /// Fails closed: any invalid token, reversed range or out-of-bounds page is rejected.
    /// </summary>
    public static bool TryParse(string? input, int totalPages, out List<int> pages, out string error)
    {
        pages = new List<int>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
            return true;

        var trimmed = input.Trim();
        if (trimmed.Equals("all", StringComparison.OrdinalIgnoreCase))
            return true;

        var seen = new HashSet<int>();
        foreach (var rawToken in trimmed.Split(','))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                error = "Invalid page selection: empty entry (e.g. \"1,,3\").";
                return false;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(token, @"^\d+(-\d+)?$"))
            {
                error = $"Invalid page selection: '{token}'. Use numbers and ranges, e.g. 1-5, 8, 11-13.";
                return false;
            }

            var parts = token.Split('-');
            var start = int.Parse(parts[0]);
            var end = parts.Length == 2 ? int.Parse(parts[1]) : start;

            if (start < 1 || end < 1)
            {
                error = "Page numbers must be 1 or greater.";
                return false;
            }

            if (start > end)
            {
                error = $"Invalid range '{token}': start must not be greater than end.";
                return false;
            }

            if (end > totalPages)
            {
                error = $"Page {end} is out of range: this book has {totalPages} pages.";
                return false;
            }

            for (var i = start; i <= end; i++)
                seen.Add(i);
        }

        if (seen.Count == 0)
        {
            error = "Please enter at least one page to print.";
            return false;
        }

        pages = seen.OrderBy(p => p).ToList();
        return true;
    }

    /// <summary>
    /// Extracts the selected pages from a PDF into a new PDF, preserving order.
    /// Copies contiguous page ranges, which is far more efficient than page-by-page.
    /// </summary>
    public static byte[] ExtractPages(byte[] source, IReadOnlyList<int> pages)
    {
        if (pages.Count == 0)
            return source;

        var ranges = GetRanges(pages);

        using var sourcePdf = new PdfDocument(new PdfReader(new MemoryStream(source)));
        using var output = new MemoryStream();
        using (var destPdf = new PdfDocument(new PdfWriter(output)))
        {
            foreach (var range in ranges)
                sourcePdf.CopyPagesTo(range.Start, range.End, destPdf);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Canonical summary of the selection: "All" for every page, otherwise e.g. "1-5, 8, 11-13".
    /// </summary>
    public static string FormatPages(IReadOnlyList<int> pages)
    {
        if (pages.Count == 0)
            return "All";

        var parts = new List<string>();
        foreach (var range in GetRanges(pages))
            parts.Add(range.Start == range.End ? range.Start.ToString() : $"{range.Start}-{range.End}");
        return string.Join(", ", parts);
    }

    private static List<(int Start, int End)> GetRanges(IReadOnlyList<int> pages)
    {
        var ranges = new List<(int Start, int End)>();
        if (pages.Count == 0)
            return ranges;

        var start = pages[0];
        var end = pages[0];
        for (var i = 1; i < pages.Count; i++)
        {
            if (pages[i] == end + 1)
            {
                end = pages[i];
                continue;
            }

            ranges.Add((start, end));
            start = end = pages[i];
        }

        ranges.Add((start, end));
        return ranges;
    }
}
