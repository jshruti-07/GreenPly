using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Lookups;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Application.Tests.Common;

/// <summary>
/// Resolution of the requested display locale.
///
/// Pure and cheap to test, and worth testing because the default chain encodes
/// a product decision that is not obvious from the code: the catalogue's base
/// locale is German and the default display locale is English, deliberately.
/// </summary>
public sealed class DisplayLocaleTests
{
    private static readonly LocaleDto German = new("de-DE", "Deutsch", IsBase: true);
    private static readonly LocaleDto English = new("en-GB", "English (UK)", IsBase: false);
    private static readonly LocaleDto French = new("fr-FR", "Français", IsBase: false);

    private static readonly IReadOnlyList<LocaleDto> Seeded = [German, English, French];

    [Fact]
    public void NoPreference_ResolvesToEnglish_NotToTheBaseLocale()
    {
        DisplayLocale.Resolve(null, Seeded)!.Code.ShouldBe("en-GB");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankIsTreatedAsNoPreference(string requested)
    {
        DisplayLocale.Resolve(requested, Seeded)!.Code.ShouldBe("en-GB");
    }

    [Theory]
    [InlineData("de-DE", "de-DE")]
    [InlineData("de-de", "de-DE")]
    [InlineData("DE-DE", "de-DE")]
    [InlineData("  fr-FR  ", "fr-FR")]
    public void RequestedLocale_ComesBackInTheStoredSpelling(string requested, string expected)
    {
        // The SQL compares against the stored value, so canonicalising here is
        // what stops a correctly-cased-but-differently-cased request matching
        // nothing and looking like missing data.
        DisplayLocale.Resolve(requested, Seeded)!.Code.ShouldBe(expected);
    }

    [Fact]
    public void UnconfiguredLocale_ResolvesToNull_SoTheCallerCanReject()
    {
        DisplayLocale.Resolve("es-ES", Seeded).ShouldBeNull();
    }

    [Fact]
    public void WithoutEnGb_AnyOtherEnglishIsPreferred()
    {
        // A catalogue set up with en-US should still open in English rather
        // than silently reverting to the base locale over a tag mismatch.
        var american = new LocaleDto("en-US", "English (US)", IsBase: false);

        DisplayLocale.Resolve(null, [German, american])!.Code.ShouldBe("en-US");
    }

    [Fact]
    public void WithNoEnglishAtAll_TheBaseLocaleWins()
    {
        DisplayLocale.Resolve(null, [German, French])!.Code.ShouldBe("de-DE");
    }

    [Fact]
    public void WithNoEnglishAndNoBase_TheFirstConfiguredLocaleWins()
    {
        var neither = new LocaleDto("it-IT", "Italiano", IsBase: false);

        DisplayLocale.Resolve(null, [neither, French])!.Code.ShouldBe("it-IT");
    }

    [Fact]
    public void NoLocalesConfiguredAtAll_Throws()
    {
        // Not a fallback case. A catalogue with no locales cannot render a name
        // in any language, and emitting SQL that matches nothing would present
        // a configuration error as missing data.
        Should.Throw<InvalidOperationException>(() => DisplayLocale.Resolve("en-GB", []));
    }
}
