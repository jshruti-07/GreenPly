using NSubstitute;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Lookups;
using Pim.Catalog.Application.Products.ListProducts;
using Pim.Catalog.Domain.Products;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Application.Tests.Products;

public class ListProductsQueryHandlerTests
{
    private readonly IProductListReader _reader = Substitute.For<IProductListReader>();
    private readonly ILocaleCatalog _locales = Substitute.For<ILocaleCatalog>();

    /// <summary>
    /// Mirrors the seeded catalogue: German is the source language, English is
    /// a translation. That ordering matters — the default display locale must
    /// come out English despite German being the base.
    /// </summary>
    public ListProductsQueryHandlerTests()
    {
        _locales.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new LocaleDto("de-DE", "Deutsch", IsBase: true),
            new LocaleDto("en-GB", "English (UK)", IsBase: false),
        ]);
    }

    private ListProductsQueryHandler CreateSut() => new(_reader, _locales);

    private static PagedResult<ProductListItemDto> EmptyPage(int page = 1, int pageSize = 25) =>
        new([], page, pageSize, 0);

    [Fact]
    public async Task Handle_NormalizesDefaultsIntoFilter()
    {
        ProductListFilter? captured = null;
        _reader.ListAsync(Arg.Do<ProductListFilter>(f => captured = f), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());

        var result = await CreateSut().Handle(new ListProductsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured.Page.ShouldBe(1);
        captured.PageSize.ShouldBe(25);
        captured.SortBy.ShouldBe("updated");
        captured.SortDescending.ShouldBeTrue();
        captured.WorkflowState.ShouldBeNull();
        captured.CategoryId.ShouldBeNull();
        captured.Search.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ParsesStatusCaseInsensitively_AndLowercasesSortBy()
    {
        ProductListFilter? captured = null;
        _reader.ListAsync(Arg.Do<ProductListFilter>(f => captured = f), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());

        var query = new ListProductsQuery(SortBy: "QUALITY", SortDir: "ASC", Status: "review");
        await CreateSut().Handle(query, CancellationToken.None);

        captured.ShouldNotBeNull();
        captured.SortBy.ShouldBe("quality");
        captured.SortDescending.ShouldBeFalse();
        captured.WorkflowState.ShouldBe(WorkflowState.Review);
    }

    [Fact]
    public async Task Handle_TrimsSearch_AndTreatsWhitespaceAsNull()
    {
        ProductListFilter? captured = null;
        _reader.ListAsync(Arg.Do<ProductListFilter>(f => captured = f), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());

        await CreateSut().Handle(new ListProductsQuery(Search: "  fire door  "), CancellationToken.None);
        captured!.Search.ShouldBe("fire door");

        await CreateSut().Handle(new ListProductsQuery(Search: "   "), CancellationToken.None);
        captured!.Search.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ReturnsReaderResultUnchanged()
    {
        var dto = new ProductListItemDto(
            Guid.NewGuid(), "FD-90-GS-1000", "Fire Door 90", "Fire Doors",
            "Active", "Approval", 96, ["de-DE", "en-GB"], 4, "Anna Becker", DateTimeOffset.UtcNow,
            ChannelReadiness.DeriveAll(96, "Approval"), "en-GB", "en-GB");
        var page = new PagedResult<ProductListItemDto>([dto], 1, 25, 1);
        _reader.ListAsync(Arg.Any<ProductListFilter>(), Arg.Any<CancellationToken>()).Returns(page);

        var result = await CreateSut().Handle(new ListProductsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(page);
        result.Value.Items[0].CompletenessPct.ShouldBe(96);
    }

    [Fact]
    public async Task Handle_DefaultsToEnglish_NotToTheBaseLocale()
    {
        ProductListFilter? captured = null;
        _reader.ListAsync(Arg.Do<ProductListFilter>(f => captured = f), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());

        await CreateSut().Handle(new ListProductsQuery(), CancellationToken.None);

        // The regression this pins: base locale is a property of the data,
        // default display locale is a property of the audience. Conflating them
        // is why the grid was German for fifteen sprints.
        captured!.DisplayLocale.Code.ShouldBe("en-GB");
    }

    [Fact]
    public async Task Handle_HonoursAnExplicitLocale_CaseInsensitively()
    {
        ProductListFilter? captured = null;
        _reader.ListAsync(Arg.Do<ProductListFilter>(f => captured = f), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());

        await CreateSut().Handle(new ListProductsQuery(Locale: "DE-de"), CancellationToken.None);

        // BCP 47 tags are case-insensitive, but the SQL compares against the
        // stored spelling — so the canonical form must come back out.
        captured!.DisplayLocale.Code.ShouldBe("de-DE");
    }

    [Fact]
    public async Task Handle_RejectsAnUnconfiguredLocale()
    {
        var result = await CreateSut().Handle(new ListProductsQuery(Locale: "fr-FR"), CancellationToken.None);

        // Rejected, not quietly ignored: silently falling back produces
        // "I selected French and it is still German" with nothing to explain it.
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Locale.Unknown");
        await _reader.DidNotReceive().ListAsync(Arg.Any<ProductListFilter>(), Arg.Any<CancellationToken>());
    }
}
