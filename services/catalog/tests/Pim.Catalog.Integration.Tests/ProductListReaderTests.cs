using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Products.ListProducts;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Infrastructure.ReadModels;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Integration.Tests;

/// <summary>
/// Exercises the real SQL against the real schema. Fixture data is chosen so
/// every assertion has exactly one correct answer:
///   FD-100 "Fire Door Alpha"  — category Fire Doors,  approval,  4/4 required approved = 100%, de-DE+en-GB, 2 SKUs
///   FD-050 "Fire Door Beta"   — category Fire Doors,  review,    2/4 approved (1 draft ignored) = 50%, de-DE, 0 SKUs
///   GP-000 "Glass Wall Gamma" — category Partitions,  draft,     no attribute set = 0%, en-GB, 1 SKU
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ProductListReaderTests(PostgresCatalogFixture fixture) : IAsyncLifetime
{
    private static bool _seeded;

    public async Task InitializeAsync()
    {
        if (_seeded)
        {
            return;
        }

        await fixture.ExecuteAsync(Seed);
        _seeded = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Defaults to de-DE so the assertions written before Sprint 16 keep
    /// asserting what they always did. The English and fallback behaviour is
    /// covered explicitly in <see cref="ProductListReaderLocaleTests"/> rather
    /// than by quietly changing what every existing test means.
    /// </summary>
    private static ProductListFilter Filter(
        int page = 1,
        int pageSize = 25,
        string sortBy = "updated",
        bool desc = true,
        WorkflowState? state = null,
        long? categoryId = null,
        string? search = null,
        string locale = "de-DE")
        => new(page, pageSize, sortBy, desc, state, categoryId, search, new DisplayLocale(locale));

    private ProductListReader CreateSut() => new(fixture.CreateDbContext());

    [Fact]
    public async Task List_ReturnsAllSeededProducts_WithTotalCount()
    {
        var result = await CreateSut().ListAsync(Filter(), CancellationToken.None);

        result.TotalCount.ShouldBe(3);
        result.Items.Count.ShouldBe(3);
    }

    [Fact]
    public async Task CompletenessPct_FollowsApprovedRequiredRule()
    {
        var result = await CreateSut().ListAsync(Filter(), CancellationToken.None);

        var byCode = result.Items.ToDictionary(i => i.Code);
        byCode["FD-100"].CompletenessPct.ShouldBe(100);
        byCode["FD-050"].CompletenessPct.ShouldBe(50);   // draft value must NOT count
        byCode["GP-000"].CompletenessPct.ShouldBe(0);    // no attribute set
    }

    [Fact]
    public async Task Languages_AndVariantCount_AreAggregated()
    {
        var result = await CreateSut().ListAsync(Filter(), CancellationToken.None);

        var alpha = result.Items.Single(i => i.Code == "FD-100");
        alpha.Languages.ShouldBe(["de-DE", "en-GB"]);
        alpha.VariantCount.ShouldBe(2);

        var gamma = result.Items.Single(i => i.Code == "GP-000");
        gamma.VariantCount.ShouldBe(1);
    }

    [Fact]
    public async Task Paging_SlicesAndReportsTotal()
    {
        var page1 = await CreateSut().ListAsync(Filter(page: 1, pageSize: 2, sortBy: "code", desc: false), CancellationToken.None);
        var page2 = await CreateSut().ListAsync(Filter(page: 2, pageSize: 2, sortBy: "code", desc: false), CancellationToken.None);

        page1.Items.Select(i => i.Code).ShouldBe(["FD-050", "FD-100"]);
        page2.Items.Select(i => i.Code).ShouldBe(["GP-000"]);
        page1.TotalCount.ShouldBe(3);
        page2.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task Paging_PastTheEnd_StillReportsTotal()
    {
        var result = await CreateSut().ListAsync(Filter(page: 99, pageSize: 25), CancellationToken.None);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task SortByQuality_Descending_OrdersByCompleteness()
    {
        var result = await CreateSut().ListAsync(Filter(sortBy: "quality", desc: true), CancellationToken.None);

        result.Items.Select(i => i.Code).ShouldBe(["FD-100", "FD-050", "GP-000"]);
    }

    [Fact]
    public async Task StatusFilter_MatchesWorkflowState()
    {
        var result = await CreateSut().ListAsync(Filter(state: WorkflowState.Review), CancellationToken.None);

        result.Items.ShouldHaveSingleItem().Code.ShouldBe("FD-050");
        result.Items[0].WorkflowState.ShouldBe("Review");
    }

    [Fact]
    public async Task CategoryFilter_IncludesSubtree()
    {
        // Category id 2 = 'Fire Doors' node; both FD products hang below it.
        var result = await CreateSut().ListAsync(Filter(categoryId: 2), CancellationToken.None);

        result.Items.Select(i => i.Code).OrderBy(c => c).ShouldBe(["FD-050", "FD-100"]);
    }

    [Fact]
    public async Task Search_MatchesCodeOrName_CaseInsensitively()
    {
        var byName = await CreateSut().ListAsync(Filter(search: "gamma"), CancellationToken.None);
        byName.Items.ShouldHaveSingleItem().Code.ShouldBe("GP-000");

        var byCode = await CreateSut().ListAsync(Filter(search: "fd-1"), CancellationToken.None);
        byCode.Items.ShouldHaveSingleItem().Code.ShouldBe("FD-100");
    }

    [Fact]
    public async Task Search_LikeWildcards_AreEscaped()
    {
        var result = await CreateSut().ListAsync(Filter(search: "%"), CancellationToken.None);

        result.Items.ShouldBeEmpty(); // '%' is a literal, not match-everything
    }

    [Fact]
    public async Task Owner_IsReturned_AndNullWhenUnset()
    {
        var result = await CreateSut().ListAsync(Filter(), CancellationToken.None);

        var byCode = result.Items.ToDictionary(i => i.Code);
        byCode["FD-100"].Owner.ShouldBe("Anna Becker");
        byCode["GP-000"].Owner.ShouldBeNull();
    }

    [Fact]
    public async Task SortByOwner_UsesOwnerColumn()
    {
        var result = await CreateSut().ListAsync(Filter(sortBy: "owner", desc: false), CancellationToken.None);

        // NULLs sort last ascending in PostgreSQL.
        result.Items.Select(i => i.Code).ShouldBe(["FD-100", "FD-050", "GP-000"]);
    }

    [Fact]
    public async Task LifecycleState_IsExposedInEnumForm()
    {
        var result = await CreateSut().ListAsync(Filter(), CancellationToken.None);

        result.Items.Single(i => i.Code == "FD-100").LifecycleState.ShouldBe("Active");
    }

    // ------------------------------------------------- display locale (S16)
    //
    // The fixture is already shaped for this, which is why these assertions can
    // be exact:
    //   FD-100  base de-DE, translated de-DE + en-GB   → no fallback either way
    //   FD-050  base de-DE, translated de-DE only      → falls back under en-GB
    //   GP-000  base en-GB, translated en-GB only      → falls back under de-DE
    // and the category side:
    //   node fd_90       no translation at all         → shows the node code
    //   node fire_doors  de-DE only
    //   node partitions  en-GB only

    [Fact]
    public async Task Locale_English_ReturnsTheEnglishName()
    {
        var result = await CreateSut().ListAsync(Filter(locale: "en-GB"), CancellationToken.None);

        var alpha = result.Items.Single(i => i.Code == "FD-100");

        // The whole point of Sprint 16: this text was in product_tr the entire
        // time and the grid could not reach it.
        alpha.Name.ShouldBe("Fire Door Alpha (EN)");
        alpha.NameLocale.ShouldBe("en-GB");
    }

    [Fact]
    public async Task Locale_FallsBackToTheBaseLocale_AndSaysSo()
    {
        var result = await CreateSut().ListAsync(Filter(locale: "en-GB"), CancellationToken.None);

        var beta = result.Items.Single(i => i.Code == "FD-050");

        beta.Name.ShouldBe("Fire Door Beta");

        // Reporting the locale is what lets the UI mark the row. Without it the
        // client cannot distinguish a translation from a substitute, and a PIM
        // that hides that is working against its own purpose.
        beta.NameLocale.ShouldBe("de-DE");
    }

    [Fact]
    public async Task Locale_FallbackRunsInBothDirections()
    {
        // GP-000's base locale is en-GB, so asking for German exercises the
        // fallback the other way round — the rule is "requested, else base",
        // not "requested, else German".
        var result = await CreateSut().ListAsync(Filter(locale: "de-DE"), CancellationToken.None);

        var gamma = result.Items.Single(i => i.Code == "GP-000");

        gamma.Name.ShouldBe("Glass Wall Gamma");
        gamma.NameLocale.ShouldBe("en-GB");
    }

    [Fact]
    public async Task Locale_CategoryFollowsTheSameRule()
    {
        var result = await CreateSut().ListAsync(Filter(locale: "en-GB"), CancellationToken.None);
        var byCode = result.Items.ToDictionary(i => i.Code);

        byCode["FD-050"].Category.ShouldBe("Fire Doors");
        byCode["FD-050"].CategoryLocale.ShouldBe("de-DE");   // fell back

        byCode["GP-000"].Category.ShouldBe("Partitions");
        byCode["GP-000"].CategoryLocale.ShouldBe("en-GB");   // direct hit
    }

    [Fact]
    public async Task Locale_UntranslatedInEveryLocale_ReportsNoLocaleAtAll()
    {
        var result = await CreateSut().ListAsync(Filter(locale: "en-GB"), CancellationToken.None);

        var alpha = result.Items.Single(i => i.Code == "FD-100");

        // Node fd_90 has no translation in any language, so the code stands in.
        // Null is not the same as a fallback and the UI marks it differently:
        // one is "no translation yet", the other is "no name at all".
        alpha.Category.ShouldBe("fd_90");
        alpha.CategoryLocale.ShouldBeNull();
    }

    [Fact]
    public async Task Locale_DoesNotChangeTheRowCount()
    {
        var german = await CreateSut().ListAsync(Filter(locale: "de-DE"), CancellationToken.None);
        var english = await CreateSut().ListAsync(Filter(locale: "en-GB"), CancellationToken.None);

        // The fallback lateral replaced a join in the COUNT query too. If the
        // two diverged, the grid would report a total for a different set from
        // the one it displays.
        english.TotalCount.ShouldBe(german.TotalCount);
        english.Items.Count.ShouldBe(german.Items.Count);
    }

    [Fact]
    public async Task Locale_SortingByName_UsesTheDisplayedText()
    {
        var result = await CreateSut().ListAsync(
            Filter(sortBy: "name", desc: false, locale: "en-GB"), CancellationToken.None);

        // Under en-GB the names are "Fire Door Alpha (EN)", "Fire Door Beta"
        // (fallback) and "Glass Wall Gamma". Sorting must follow what the user
        // sees, not the base-locale text behind it.
        result.Items.Select(i => i.Code).ShouldBe(["FD-100", "FD-050", "GP-000"]);
    }

    private const string Seed = """
        SET search_path TO catalog;

        INSERT INTO locale (code, name, is_base) VALUES
          ('de-DE', 'Deutsch', true),
          ('en-GB', 'English (UK)', false);

        -- ids are IDENTITY starting at 1: node 1 = root, 2 = Fire Doors,
        -- 3 = child series under Fire Doors, 4 = Partitions.
        INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth) VALUES
          ('root',        'FAMILY',      NULL, 'root',                    0),
          ('fire_doors',  'CATEGORY',    1,    'root.fire_doors',         1),
          ('fd_90',       'SERIES',      2,    'root.fire_doors.fd_90',   2),
          ('partitions',  'CATEGORY',    1,    'root.partitions',         1);

        INSERT INTO taxonomy_node_tr (node_id, locale_code, name, translation_status) VALUES
          (2, 'de-DE', 'Fire Doors', 'approved'),
          (4, 'en-GB', 'Partitions', 'approved');

        INSERT INTO attribute_group (code) VALUES ('general');

        INSERT INTO attribute (code, data_type, group_id) VALUES
          ('fire_rating',   'text',   1),
          ('width_mm',      'number', 1),
          ('height_mm',     'number', 1),
          ('material',      'text',   1),
          ('nice_to_have',  'text',   1);

        INSERT INTO attribute_set (code, name) VALUES ('fire_door_set', 'Fire door attributes');

        INSERT INTO attribute_set_attribute (set_id, attribute_id, is_required) VALUES
          (1, 1, true), (1, 2, true), (1, 3, true), (1, 4, true),
          (1, 5, false); -- optional: must not affect the denominator

        -- product 1: FD-100 — 4/4 approved
        INSERT INTO product (product_code, primary_node_id, attribute_set_id, base_locale,
                             lifecycle_state, workflow_state, owner_display_name, updated_at)
        VALUES ('FD-100', 3, 1, 'de-DE', 'ACTIVE', 'approval', 'Anna Becker', now() - interval '1 hour');

        -- product 2: FD-050 — 2/4 approved + 1 draft (ignored)
        INSERT INTO product (product_code, primary_node_id, attribute_set_id, base_locale,
                             lifecycle_state, workflow_state, owner_display_name, updated_at)
        VALUES ('FD-050', 2, 1, 'de-DE', 'ACTIVE', 'review', 'Markus Klein', now() - interval '2 hour');

        -- product 3: GP-000 — no attribute set
        INSERT INTO product (product_code, primary_node_id, attribute_set_id, base_locale,
                             lifecycle_state, workflow_state, updated_at)
        VALUES ('GP-000', 4, NULL, 'en-GB', 'PLANNED', 'draft', now() - interval '3 hour');

        INSERT INTO product_tr (product_id, locale_code, name, translation_status) VALUES
          (1, 'de-DE', 'Fire Door Alpha', 'approved'),
          (1, 'en-GB', 'Fire Door Alpha (EN)', 'approved'),
          (2, 'de-DE', 'Fire Door Beta', 'approved'),
          (3, 'en-GB', 'Glass Wall Gamma', 'approved');

        INSERT INTO attribute_value (product_id, attribute_id, value_text, data_state) VALUES
          (1, 1, 'EI90',  'approved'),
          (1, 4, 'steel', 'approved');
        INSERT INTO attribute_value (product_id, attribute_id, value_number, data_state) VALUES
          (1, 2, 1000, 'approved'),
          (1, 3, 2100, 'approved');

        INSERT INTO attribute_value (product_id, attribute_id, value_text, data_state) VALUES
          (2, 1, 'EI30', 'approved'),
          (2, 4, 'wood', 'draft');   -- draft: excluded from completeness
        INSERT INTO attribute_value (product_id, attribute_id, value_number, data_state) VALUES
          (2, 2, 900, 'approved');

        INSERT INTO sku (product_id, sku_code) VALUES
          (1, 'FD-100-L'), (1, 'FD-100-R'), (3, 'GP-000-STD');
        """;
}
