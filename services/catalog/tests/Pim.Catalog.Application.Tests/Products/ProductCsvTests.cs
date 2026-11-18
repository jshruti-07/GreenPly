using Pim.Catalog.Application.Products.ExportProducts;
using Pim.Catalog.Application.Products.ListProducts;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Application.Tests.Products;

public sealed class ProductCsvTests
{
    private static ProductListItemDto Item(
        string code = "FD-90-GS-1000",
        string name = "Fire Door 90",
        string? owner = "Anna Becker",
        string category = "Fire Doors") =>
        new(
            Guid.NewGuid(), code, name, category, "Active", "Approval", 96,
            ["de-DE", "en-GB"], 4, owner,
            new DateTimeOffset(2026, 6, 22, 10, 30, 0, TimeSpan.Zero),
            ChannelReadiness.DeriveAll(96, "Approval"),
            // The CSV renders the text, not which locale produced it: the file
            // is a snapshot in one language, and a per-cell provenance column
            // would be noise in a spreadsheet. The header assertion above pins
            // that these two do not leak into the export.
            NameLocale: "en-GB", CategoryLocale: "en-GB");

    [Fact]
    public void Render_WritesHeaderRow()
    {
        var csv = ProductCsv.Render([]);

        csv.Split('\n')[0].Trim().ShouldBe(
            "Code,Name,Category,Lifecycle,Workflow,Completeness %,Languages,Variants,Owner,Updated (UTC)");
    }

    [Fact]
    public void Render_WritesOneRowPerItem()
    {
        var csv = ProductCsv.Render([Item(), Item(code: "FD-60")]);

        csv.Trim().Split('\n').Length.ShouldBe(3); // header + 2
    }

    [Fact]
    public void Render_FormatsFieldsPredictably()
    {
        var line = ProductCsv.Render([Item()]).Trim().Split('\n')[1];

        line.ShouldBe("FD-90-GS-1000,Fire Door 90,Fire Doors,Active,Approval,96,de-DE en-GB,4,Anna Becker,2026-06-22 10:30:00");
    }

    [Fact]
    public void Render_QuotesFieldsContainingCommas()
    {
        var csv = ProductCsv.Render([Item(name: "Door, Steel")]);

        csv.ShouldContain("\"Door, Steel\"");
    }

    [Fact]
    public void Render_DoublesEmbeddedQuotes()
    {
        var csv = ProductCsv.Render([Item(name: "Door \"Premium\"")]);

        csv.ShouldContain("\"Door \"\"Premium\"\"\"");
    }

    [Fact]
    public void Render_QuotesFieldsContainingNewlines()
    {
        var csv = ProductCsv.Render([Item(name: "Line1\nLine2")]);

        csv.ShouldContain("\"Line1\nLine2\"");
    }

    [Theory]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1)")]
    public void Render_NeutralizesSpreadsheetFormulas(string malicious)
    {
        // Owner and name are user-controlled: a leading =/+/-/@ must not be
        // interpreted as a formula when the CSV is opened in Excel.
        var csv = ProductCsv.Render([Item(owner: malicious)]);

        csv.ShouldContain($"'{malicious}");
    }

    [Fact]
    public void Render_WritesEmptyStringForMissingOwner()
    {
        var line = ProductCsv.Render([Item(owner: null)]).Trim().Split('\n')[1];

        line.ShouldEndWith(",,2026-06-22 10:30:00");
    }
}
