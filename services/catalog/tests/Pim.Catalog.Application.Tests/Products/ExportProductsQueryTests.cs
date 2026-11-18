using NSubstitute;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Lookups;
using Pim.Catalog.Application.Products.ExportProducts;
using Pim.Catalog.Application.Products.ListProducts;
using Pim.Catalog.Domain.Products;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Application.Tests.Products;

public sealed class ExportProductsQueryTests
{
    private readonly IProductListReader _reader = Substitute.For<IProductListReader>();
    private readonly ILocaleCatalog _locales = Substitute.For<ILocaleCatalog>();
    private readonly ExportProductsQueryValidator _validator = new();

    /// <summary>
    /// The catalogue the client actually runs: a German base with an English
    /// translation. fr-FR is deliberately absent so that the rejection path has
    /// a locale to be rejected with.
    /// </summary>
    private static readonly IReadOnlyList<LocaleDto> Configured =
    [
        new("de-DE", "German", true),
        new("en-GB", "English", false),
    ];

    public ExportProductsQueryTests() =>
        _locales.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Configured));

    private ExportProductsQueryHandler CreateSut() => new(_reader, _locales);

    private static PagedResult<ProductListItemDto> Page(params ProductListItemDto[] items) =>
        new(items, 1, ExportProductsQuery.MaxRows, items.Length);

    private static ProductListItemDto Item(string code) => new(
        Guid.NewGuid(), code, $"Name {code}", "Fire Doors", "Active", "Draft", 50,
        ["de-DE"], 1, "Anna Becker", DateTimeOffset.UtcNow,
        ChannelReadiness.DeriveAll(50, "Draft"),
        NameLocale: "en-GB", CategoryLocale: "en-GB");

    [Fact]
    public async Task Handle_ExportsEveryMatchingRow_NotJustTheFirstPage()
    {
        ProductListFilter? captured = null;
        _reader.ListAsync(Arg.Do<ProductListFilter>(f => captured = f), Arg.Any<CancellationToken>())
            .Returns(Page(Item("A-1"), Item("B-2")));

        var result = await CreateSut().Handle(new ExportProductsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        captured!.Page.ShouldBe(1);
        captured.PageSize.ShouldBe(ExportProductsQuery.MaxRows);
        result.Value.ShouldContain("A-1");
        result.Value.ShouldContain("B-2");
    }

    [Fact]
    public async Task Handle_PassesSelectedCodesThrough()
    {
        ProductListFilter? captured = null;
        _reader.ListAsync(Arg.Do<ProductListFilter>(f => captured = f), Arg.Any<CancellationToken>())
            .Returns(Page(Item("A-1")));

        await CreateSut().Handle(
            new ExportProductsQuery(Codes: ["A-1", " B-2 ", ""]),
            CancellationToken.None);

        captured!.Codes.ShouldBe(["A-1", "B-2"]); // trimmed, blanks dropped
    }

    [Fact]
    public async Task Handle_AppliesFiltersAndSort()
    {
        ProductListFilter? captured = null;
        _reader.ListAsync(Arg.Do<ProductListFilter>(f => captured = f), Arg.Any<CancellationToken>())
            .Returns(Page());

        await CreateSut().Handle(
            new ExportProductsQuery(SortBy: "QUALITY", SortDir: "asc", Status: "review", CategoryId: 3, Search: " door "),
            CancellationToken.None);

        captured!.SortBy.ShouldBe("quality");
        captured.SortDescending.ShouldBeFalse();
        captured.WorkflowState.ShouldBe(WorkflowState.Review);
        captured.CategoryId.ShouldBe(3);
        captured.Search.ShouldBe("door");
    }

    // ---------------------------------------------------------------------
    // Display locale (ADR-009). The export reuses the grid's read model so the
    // file cannot drift from the screen — which now includes the language it
    // was rendered in. These three cases pin that: the default, the pass
    // through, and the rejection. Sprint 16 changed this handler's signature
    // without covering any of them.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Handle_DefaultsToEnglish_NotTheGermanBaseLocale()
    {
        ProductListFilter? captured = null;
        _reader.ListAsync(Arg.Do<ProductListFilter>(f => captured = f), Arg.Any<CancellationToken>())
            .Returns(Page(Item("A-1")));

        var result = await CreateSut().Handle(new ExportProductsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        captured!.DisplayLocale.Code.ShouldBe(DisplayLocale.PreferredDefault);
    }

    [Fact]
    public async Task Handle_PassesTheRequestedLocaleThrough_CanonicallySpelled()
    {
        ProductListFilter? captured = null;
        _reader.ListAsync(Arg.Do<ProductListFilter>(f => captured = f), Arg.Any<CancellationToken>())
            .Returns(Page(Item("A-1")));

        // Lower-cased on purpose: BCP 47 tags are case-insensitive, but the SQL
        // compares against the spelling stored in catalog.locale.
        var result = await CreateSut().Handle(
            new ExportProductsQuery(Locale: "de-de"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        captured!.DisplayLocale.Code.ShouldBe("de-DE");
    }

    [Fact]
    public async Task Handle_RejectsALocaleTheCatalogueIsNotConfiguredFor()
    {
        var result = await CreateSut().Handle(
            new ExportProductsQuery(Locale: "fr-FR"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Locale.Unknown");

        // The read must not happen at all — a rejected locale should cost
        // nothing, and an export that queried first would be a 5,000-row scan
        // thrown away.
        await _reader.DidNotReceive().ListAsync(
            Arg.Any<ProductListFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_RejectsUnwhitelistedSort()
    {
        _validator.Validate(new ExportProductsQuery(SortBy: "updated_at; DROP TABLE product")).IsValid
            .ShouldBeFalse();
    }

    [Fact]
    public void Validator_RejectsUnknownStatus()
    {
        _validator.Validate(new ExportProductsQuery(Status: "Nonsense")).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validator_RejectsOversizedSelection()
    {
        var codes = Enumerable.Range(0, ExportProductsQuery.MaxSelectedCodes + 1)
            .Select(i => $"CODE-{i}")
            .ToList();

        _validator.Validate(new ExportProductsQuery(Codes: codes)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validator_AcceptsSelectionAtTheLimit()
    {
        var codes = Enumerable.Range(0, ExportProductsQuery.MaxSelectedCodes)
            .Select(i => $"CODE-{i}")
            .ToList();

        _validator.Validate(new ExportProductsQuery(Codes: codes)).IsValid.ShouldBeTrue();
    }
}
