using NSubstitute;
using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Lookups;
using Pim.Catalog.Application.Products.Localization;
using Pim.Catalog.Application.Products.Logistics;
using Pim.Catalog.Application.Products.Planning;
using Pim.SharedKernel.Results;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Application.Tests.Products;

/// <summary>
/// R-15 Phase 2: the detail tabs render in the reader's language, not the
/// product's base locale.
///
/// Each tab is pinned on two things — the default it resolves to when the
/// caller asks for nothing, and its refusal of a locale this catalogue is not
/// configured for. The second matters more than it looks: falling back on an
/// unrecognised locale produces the bug report ADR-009 exists to prevent ("I
/// selected French and it is still German"), with nothing in the response to
/// explain it.
///
/// The stores are substituted and return null, so these tests are about the
/// handler's contract with the store — which locale it hands over, and whether
/// it hands over anything at all — not about SQL. The SQL is covered by the
/// integration suite and the smoke run.
/// </summary>
public sealed class DetailTabLocaleTests
{
    private static readonly IReadOnlyList<LocaleDto> Configured =
    [
        new("de-DE", "German", true),
        new("en-GB", "English", false),
    ];

    private const string Unconfigured = "fr-FR";

    private readonly ILocaleCatalog _locales = Substitute.For<ILocaleCatalog>();
    private readonly IProductPlanningStore _planning = Substitute.For<IProductPlanningStore>();
    private readonly IProductRelationshipStore _relationships = Substitute.For<IProductRelationshipStore>();
    private readonly IProductApplicationStore _applications = Substitute.For<IProductApplicationStore>();
    private readonly IProductLocalizationStore _localization = Substitute.For<IProductLocalizationStore>();

    private readonly DisplayLocaleResolver _resolver;

    public DetailTabLocaleTests()
    {
        _locales.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Configured));
        _resolver = new DisplayLocaleResolver(_locales);
    }

    // ------------------------------------------------------------- Planning

    [Fact]
    public async Task Planning_DefaultsToEnglish_NotTheGermanBaseLocale()
    {
        DisplayLocale? captured = null;
        _planning
            .GetAsync(Arg.Any<string>(), Arg.Do<DisplayLocale>(l => captured = l), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProductPlanningDto?>(null));

        await new GetProductPlanningQueryHandler(_planning, _resolver)
            .Handle(new GetProductPlanningQuery("A-1"), CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Code.ShouldBe(DisplayLocale.PreferredDefault);
    }

    [Fact]
    public async Task Planning_RejectsAnUnconfiguredLocale_WithoutTouchingTheStore()
    {
        var result = await new GetProductPlanningQueryHandler(_planning, _resolver)
            .Handle(new GetProductPlanningQuery("A-1", Unconfigured), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Locale.Unknown");

        await _planning.DidNotReceive()
            .GetAsync(Arg.Any<string>(), Arg.Any<DisplayLocale>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------- Relationships

    [Fact]
    public async Task Relationships_PassTheRequestedLocaleThrough_CanonicallySpelled()
    {
        DisplayLocale? captured = null;
        _relationships
            .GetAsync(Arg.Any<string>(), Arg.Do<DisplayLocale>(l => captured = l), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProductRelationshipsDto?>(null));

        // Lower-cased deliberately: BCP 47 tags are case-insensitive, but the
        // SQL compares against the spelling stored in catalog.locale.
        await new GetProductRelationshipsQueryHandler(_relationships, _resolver)
            .Handle(new GetProductRelationshipsQuery("A-1", "de-de"), CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Code.ShouldBe("de-DE");
    }

    [Fact]
    public async Task Relationships_RejectAnUnconfiguredLocale()
    {
        var result = await new GetProductRelationshipsQueryHandler(_relationships, _resolver)
            .Handle(new GetProductRelationshipsQuery("A-1", Unconfigured), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Locale.Unknown");
    }

    [Fact]
    public async Task AddingARelationship_ReturnsThePayloadInTheScreensLanguage()
    {
        // A save that answers in a different language from the form it was sent
        // from would flip the tab under the user. The write path carries the
        // locale for that reason alone — the write itself has no language.
        DisplayLocale? captured = null;
        _relationships
            .AddAsync(
                Arg.Any<AddRelationshipCommand>(),
                Arg.Do<DisplayLocale>(l => captured = l),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<ProductRelationshipsDto>(
                Pim.Catalog.Domain.Products.ProductErrors.NotFound("A-1"))));

        await new AddRelationshipCommandHandler(_relationships, _resolver)
            .Handle(
                new AddRelationshipCommand("A-1", "ACCESSORY", "B-2", 1, RowVersion: 1, Locale: "de-DE"),
                CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Code.ShouldBe("de-DE");
    }

    // --------------------------------------------------------- Applications

    [Fact]
    public async Task Applications_DefaultToEnglish()
    {
        DisplayLocale? captured = null;
        _applications
            .GetAsync(Arg.Any<string>(), Arg.Do<DisplayLocale>(l => captured = l), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProductApplicationsDto?>(null));

        await new GetProductApplicationsQueryHandler(_applications, _resolver)
            .Handle(new GetProductApplicationsQuery("A-1"), CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Code.ShouldBe(DisplayLocale.PreferredDefault);
    }

    [Fact]
    public async Task Applications_RejectAnUnconfiguredLocale()
    {
        var result = await new GetProductApplicationsQueryHandler(_applications, _resolver)
            .Handle(new GetProductApplicationsQuery("A-1", Unconfigured), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Locale.Unknown");
    }

    // --------------------------------------------------------- Localization

    [Fact]
    public async Task Localization_DefaultsToEnglish_ForTheLabelsItRenders()
    {
        DisplayLocale? captured = null;
        _localization
            .GetAsync(Arg.Any<string>(), Arg.Do<DisplayLocale>(l => captured = l), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProductLocalizationDto?>(null));

        await new GetProductLocalizationQueryHandler(_localization, _resolver)
            .Handle(new GetProductLocalizationQuery("A-1"), CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Code.ShouldBe(DisplayLocale.PreferredDefault);
    }

    [Fact]
    public async Task Localization_SeparatesTheLanguageBeingEditedFromTheOneBeingRead()
    {
        // The distinction this whole tab turns on: LocaleCode is what is being
        // translated INTO, DisplayLocale is what the surrounding screen is in.
        // A steward working in English can translate into French, and neither
        // value should be inferred from the other.
        DisplayLocale? captured = null;
        _localization
            .SaveAsync(
                Arg.Any<UpdateProductLocalizationCommand>(),
                Arg.Do<DisplayLocale>(l => captured = l),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<ProductLocalizationDto>(
                Pim.Catalog.Domain.Products.ProductErrors.NotFound("A-1"))));

        await new UpdateProductLocalizationCommandHandler(_localization, _resolver)
            .Handle(
                new UpdateProductLocalizationCommand(
                    "A-1",
                    LocaleCode: "de-DE",
                    Values: [],
                    TranslationStatus: null,
                    RowVersion: 1,
                    DisplayLocale: "en-GB"),
                CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Code.ShouldBe("en-GB");
    }
}
