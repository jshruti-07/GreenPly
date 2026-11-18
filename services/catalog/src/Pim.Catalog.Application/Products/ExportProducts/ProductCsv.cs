using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Pim.Catalog.Application.Products.ListProducts;

namespace Pim.Catalog.Application.Products.ExportProducts;

/// <summary>
/// CSV rendering of grid rows.
///
/// CsvHelper owns RFC 4180 — quoting, embedded-quote doubling, newlines inside
/// fields. That is a solved problem and the stack matrix specified the library
/// from the start; the hand-written version it replaces was correct but was one
/// more escaping implementation to keep correct.
///
/// FORMULA INJECTION IS STILL OURS. CsvHelper deliberately does not do it —
/// it is a spreadsheet concern, not a CSV one — and a field beginning
/// <c>= + - @</c> TAB or CR is executed as a formula when the file is opened in
/// Excel or Sheets. Product codes and owner names are user-controlled, so this
/// is a real exposure and the sanitiser below is the only reason it is not one.
/// </summary>
public static class ProductCsv
{
    private static readonly string[] Headers =
    [
        "Code", "Name", "Category", "Lifecycle", "Workflow",
        "Completeness %", "Languages", "Variants", "Owner", "Updated (UTC)",
    ];

    public const string ContentType = "text/csv; charset=utf-8";

    /// <summary>
    /// Characters that make a spreadsheet treat a cell as a formula. TAB and CR
    /// are included because Excel strips leading whitespace before deciding.
    /// </summary>
    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

    public static string Render(IEnumerable<ProductListItemDto> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // The previous implementation emitted Environment.NewLine via
            // AppendLine, which produced CRLF on Windows and LF here. Pinning it
            // keeps the output byte-identical wherever the service runs — the
            // smoke suite counts lines.
            NewLine = "\r\n",
            HasHeaderRecord = false,

            // Quote on ANY of comma, quote, CR or LF — the same rule the
            // hand-written writer used.
            //
            // CsvHelper's default check looks for the CONFIGURED NewLine as a
            // sequence, so with NewLine = "\r\n" a field containing a bare LF
            // is not detected and is written unquoted, which silently splits
            // one record into two. A product name pasted from a Word document
            // is exactly how a bare LF gets into this data.
            // A null field is written as empty and never needs quoting.
            ShouldQuote = args =>
                args.Field is { } field
                && (field.AsSpan().IndexOfAny(',', '"', '\n') >= 0
                    || field.Contains('\r', StringComparison.Ordinal)),
        };

        var builder = new StringBuilder();
        using var writer = new StringWriter(builder, CultureInfo.InvariantCulture);
        using var csv = new CsvWriter(writer, configuration);

        foreach (var header in Headers)
        {
            csv.WriteField(header);
        }

        csv.NextRecord();

        foreach (var item in items)
        {
            csv.WriteField(Sanitize(item.Code));
            csv.WriteField(Sanitize(item.Name));
            csv.WriteField(Sanitize(item.Category));
            csv.WriteField(Sanitize(item.LifecycleState));
            csv.WriteField(Sanitize(item.WorkflowState));
            csv.WriteField(item.CompletenessPct.ToString(CultureInfo.InvariantCulture));
            csv.WriteField(Sanitize(string.Join(' ', item.Languages)));
            csv.WriteField(item.VariantCount.ToString(CultureInfo.InvariantCulture));
            csv.WriteField(Sanitize(item.Owner ?? string.Empty));
            csv.WriteField(item.UpdatedAtUtc.UtcDateTime.ToString(
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            csv.NextRecord();
        }

        writer.Flush();
        return builder.ToString();
    }

    /// <summary>
    /// Prefixes a leading formula trigger with an apostrophe so the spreadsheet
    /// treats the cell as text. Applied BEFORE CsvHelper sees the value, so the
    /// apostrophe is itself quoted correctly if the field also needs quoting.
    /// </summary>
    private static string Sanitize(string value) =>
        value.Length > 0 && Array.IndexOf(FormulaTriggers, value[0]) >= 0
            ? "'" + value
            : value;
}
