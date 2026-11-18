using Pim.Catalog.Application.Common;
using Pim.Catalog.Application.Workflow;
using Pim.Catalog.Infrastructure.ReadModels;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Integration.Tests;

/// <summary>
/// The board and queue SQL against the real schema.
///
/// The assertion that earns its place is the sort sweep. `GetWorkflowQueueQuery`
/// publishes a list of sortable columns and `WorkflowBoardStore` holds the SQL
/// expression for each; the two are separate lists in separate assemblies, and
/// nothing but this test makes them agree. A column accepted by the validator
/// and missing from the dictionary is a 500 on a query string the API's own
/// error message told the caller to use.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class WorkflowBoardStoreTests(PostgresCatalogFixture fixture) : IAsyncLifetime
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

    private WorkflowBoardStore CreateSut() => new(fixture.CreateDbContext());

    private static WorkflowQueueFilter Filter(
        string sortBy = "updated",
        bool desc = true,
        string? step = null,
        string? assignee = null,
        string? priority = null,
        string? search = null,
        int page = 1,
        int pageSize = 25) =>
        new(page, pageSize, step, assignee, priority, search, sortBy, desc, new DisplayLocale("de-DE"));

    [Fact]
    public async Task EverySortableColumnActuallySorts()
    {
        // Not "does not throw" as a formality: an unwhitelisted key throws
        // ArgumentException and a bad expression throws PostgresException, and
        // both would reach a caller as a 500 from a value the validator
        // accepted.
        foreach (var column in GetWorkflowQueueQuery.SortableColumns)
        {
            foreach (var descending in new[] { true, false })
            {
                var page = await CreateSut().GetQueueAsync(
                    Filter(sortBy: column, desc: descending), CancellationToken.None);

                page.Items.Count.ShouldBeGreaterThan(0, $"sorting by '{column}' returned nothing");
            }
        }
    }

    [Fact]
    public async Task SortingByCodeIsActuallyOrdered()
    {
        // Guards the guard above: every column "works" if the ORDER BY is
        // ignored, so at least one has to be checked for order.
        var page = await CreateSut().GetQueueAsync(Filter(sortBy: "code", desc: false), CancellationToken.None);

        page.Items.Select(i => i.Code).ShouldBe(page.Items.Select(i => i.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task StateTotalsCoverEveryProduct()
    {
        var sut = CreateSut();

        var totals = await sut.GetStateTotalsAsync(CancellationToken.None);
        var all = await sut.GetQueueAsync(Filter(pageSize: 1), CancellationToken.None);

        totals.Sum(t => t.Total).ShouldBe(all.TotalCount);
        totals.ShouldAllBe(t => t.Total > 0);
    }

    [Fact]
    public async Task TheBoardNeverReturnsMoreThanTheRequestedCardsPerColumn()
    {
        var cards = await CreateSut().GetCardsAsync(1, new DisplayLocale("de-DE"), CancellationToken.None);

        cards.GroupBy(c => c.WorkflowState, StringComparer.Ordinal)
            .ShouldAllBe(g => g.Count() == 1);

        // And it covers every state that has products, rather than only the
        // first one the window happened to reach.
        var totals = await CreateSut().GetStateTotalsAsync(CancellationToken.None);
        cards.Select(c => c.WorkflowState).Order(StringComparer.Ordinal)
            .ShouldBe(totals.Select(t => t.WorkflowState).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task FilteringByStepReturnsOnlyThatStep()
    {
        var page = await CreateSut().GetQueueAsync(Filter(step: "draft"), CancellationToken.None);

        page.TotalCount.ShouldBeGreaterThan(0);
        page.Items.ShouldAllBe(i => i.WorkflowState == "draft");
    }

    [Fact]
    public async Task SearchMatchesCodeAndNameInAnyLanguage()
    {
        // Matching is locale-agnostic, rendering is not (Sprint 17). The German
        // name must be findable while the display locale is English.
        var sut = CreateSut();

        var byCode = await sut.GetQueueAsync(Filter(search: "WFB-"), CancellationToken.None);
        byCode.TotalCount.ShouldBe(3);

        var byGermanName = await sut.GetQueueAsync(
            new WorkflowQueueFilter(1, 25, null, null, null, "Brandschutztuer",
                "updated", true, new DisplayLocale("en-GB")),
            CancellationToken.None);

        byGermanName.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task AnUnknownAssigneeReturnsNothingRatherThanEverything()
    {
        var page = await CreateSut().GetQueueAsync(
            Filter(assignee: "Nobody At All"), CancellationToken.None);

        page.TotalCount.ShouldBe(0);
        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnwhitelistedSortKeyIsRefusedRatherThanInterpolated()
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            CreateSut().GetQueueAsync(Filter(sortBy: "p.id; DROP TABLE product"), CancellationToken.None));
    }

    /// <summary>
    /// Idempotent: this class shares one container with the other suites and
    /// must not care whether theirs ran first. Three products in three states,
    /// one of them German-named so the search assertion has something
    /// locale-specific to find.
    /// </summary>
    private const string Seed = """
        SET search_path TO catalog;

        INSERT INTO locale (code, name, is_base) VALUES ('de-DE', 'Deutsch', true)
          ON CONFLICT (code) DO NOTHING;
        INSERT INTO locale (code, name, is_base) VALUES ('en-GB', 'English (UK)', false)
          ON CONFLICT (code) DO NOTHING;

        INSERT INTO taxonomy_node (code, node_type, path, depth) VALUES ('wfb_root', 'FAMILY', 'wfb_root', 0)
          ON CONFLICT (code) DO NOTHING;

        INSERT INTO product (product_code, primary_node_id, base_locale, workflow_state, owner_display_name)
        SELECT v.code, tn.id, 'de-DE', v.state, v.owner
        FROM taxonomy_node tn
        CROSS JOIN (VALUES
          ('WFB-1', 'draft',     'Anna Becker'),
          ('WFB-2', 'review',    'Markus Klein'),
          ('WFB-3', 'published', 'Lena Vogt')
        ) AS v(code, state, owner)
        WHERE tn.code = 'wfb_root'
          ON CONFLICT (product_code) DO NOTHING;

        INSERT INTO product_tr (product_id, locale_code, name)
        SELECT p.id, 'de-DE', 'Brandschutztuer 90'
        FROM product p WHERE p.product_code = 'WFB-1'
          ON CONFLICT (product_id, locale_code) DO NOTHING;

        INSERT INTO approval_task (product_id, definition_id, current_step_id, assignee_display_name)
        SELECT p.id, d.id, ws.id, p.owner_display_name
        FROM product p
        JOIN workflow_definition d ON d.entity_type = 'PRODUCT' AND d.is_active
        JOIN workflow_step ws ON ws.definition_id = d.id AND ws.code = p.workflow_state
        WHERE p.product_code LIKE 'WFB-%' AND NOT ws.is_terminal
          AND NOT EXISTS (SELECT 1 FROM approval_task t WHERE t.product_id = p.id AND t.status = 'open');
        """;
}
