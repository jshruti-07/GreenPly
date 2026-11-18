using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Domain.Workflow;
using Pim.Catalog.Infrastructure.ReadModels;
using Pim.Catalog.Infrastructure.Workflow;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Integration.Tests;

/// <summary>
/// The shipped pipeline lives in db/init/01_catalog_schema.sql as reference
/// data. That is the single source of truth, which makes it exactly the thing
/// that needs checking: a definition edited by hand can silently make a
/// transition unreachable, and the only symptom is a button that stopped
/// appearing.
///
/// These tests read it out of a REAL PostgreSQL container through the REAL
/// loader, then assert the process it describes — including that it still
/// reaches exactly the states the Product aggregate hardcoded up to Sprint 26.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class WorkflowPipelineTests(PostgresCatalogFixture fixture)
{
    private async Task<WorkflowDefinition> LoadAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        using var catalog = new WorkflowCatalog(dataSource);

        return await catalog.GetActiveAsync("PRODUCT", CancellationToken.None);
    }

    [Fact]
    public async Task TheSeededPipelineLoads_AndIsNamedProductPublishing()
    {
        var pipeline = await LoadAsync();

        pipeline.Code.ShouldBe("product_publishing");
        pipeline.EntityType.ShouldBe("PRODUCT");
        pipeline.Version.ShouldBe(1);
    }

    [Fact]
    public async Task ItHasTheFourEditorialSteps_InOrder()
    {
        var pipeline = await LoadAsync();

        pipeline.Steps.Select(s => s.Code).ShouldBe(["draft", "review", "approval", "published"]);
        pipeline.InitialStep.Code.ShouldBe("draft");
        pipeline.Steps.Single(s => s.IsTerminal).Code.ShouldBe("published");

        // Roles are recorded and advisory: nothing checks them until RBAC ships.
        // Asserted anyway, because a NULL that appears later would silently
        // empty the "Assigned to ... role" line on the Workflow tab.
        pipeline.StepFor(WorkflowState.Approval)!.OwnerRole.ShouldBe("Compliance Manager");
        pipeline.StepFor(WorkflowState.Published)!.OwnerRole.ShouldBeNull();
    }

    [Fact]
    public async Task ItDescribesExactlyTheSixShippedTransitions()
    {
        var pipeline = await LoadAsync();

        var triples = pipeline.Transitions
            .Select(t => (
                From: pipeline.StepById(t.FromStepId)!.Code,
                Action: WorkflowActions.ToToken(t.Action),
                To: pipeline.StepById(t.ToStepId)!.Code))
            .OrderBy(x => x.From, StringComparer.Ordinal)
            .ThenBy(x => x.Action, StringComparer.Ordinal)
            .ToList();

        triples.ShouldBe(
        [
            ("approval", "APPROVE", "published"),
            ("approval", "REJECT", "draft"),
            ("approval", "REQUEST_CHANGES", "draft"),
            ("draft", "SUBMIT", "review"),
            ("review", "APPROVE", "approval"),
            ("review", "REQUEST_CHANGES", "draft"),
        ]);
    }

    [Fact]
    public async Task ItReachesExactlyTheStatesTheAggregateUsedToHardcode()
    {
        // The migration's whole claim is "same behaviour, different home".
        // This is where that claim is checked against the deployed artefact
        // rather than against a comment.
        var pipeline = await LoadAsync();

        Reachable(pipeline, WorkflowState.Draft).ShouldBe([WorkflowState.Review]);
        Reachable(pipeline, WorkflowState.Review)
            .ShouldBe([WorkflowState.Draft, WorkflowState.Approval], ignoreOrder: true);
        Reachable(pipeline, WorkflowState.Approval)
            .ShouldBe([WorkflowState.Draft, WorkflowState.Published], ignoreOrder: true);
        Reachable(pipeline, WorkflowState.Published).ShouldBeEmpty();
    }

    [Fact]
    public async Task ATaskAndItsHistoryRoundTripThroughEntityFramework()
    {
        // The mapping is the part of this module most likely to be wrong in a
        // way unit tests cannot see: a mistyped column name, an enum converter
        // that writes 'Open' where the CHECK constraint demands 'open', a
        // history row whose action is stored as 'RequestChanges' instead of
        // REQUEST_CHANGES. All of those compile.
        await fixture.ExecuteAsync(Seed);

        var pipeline = await LoadAsync();
        var now = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

        long productId;
        await using (var arrange = fixture.CreateDbContext())
        {
            productId = await arrange.Products
                .Where(p => p.Code == ProductCode.Create(ProductCodeUnderTest).Value)
                .Select(p => p.Id)
                .SingleAsync();
        }

        await using (var write = fixture.CreateDbContext())
        {
            var task = ApprovalTask.OpenAt(
                productId, pipeline, pipeline.StepFor(WorkflowState.Draft)!, "Anna Becker", now);

            task.Perform(pipeline, WorkflowAction.Submit, "ready for review", "Anna Becker", now)
                .IsSuccess.ShouldBeTrue();

            write.ApprovalTasks.Add(task);
            await write.SaveChangesAsync();
        }

        await using (var read = fixture.CreateDbContext())
        {
            var reloaded = await read.ApprovalTasks
                .SingleAsync(t => t.ProductId == productId && t.Status == ApprovalTaskStatus.Open);

            reloaded.CurrentStepId.ShouldBe(pipeline.StepFor(WorkflowState.Review)!.Id);
            reloaded.Priority.ShouldBe(ApprovalPriority.Normal);
            reloaded.AssigneeDisplayName.ShouldBe("Anna Becker");
            reloaded.AssigneeUserId.ShouldBeNull();

            var entry = reloaded.History.Single();
            entry.Action.ShouldBe(WorkflowAction.Submit);
            entry.Comment.ShouldBe("ready for review");
            entry.FromStepId.ShouldBe(pipeline.StepFor(WorkflowState.Draft)!.Id);
            entry.ToStepId.ShouldBe(pipeline.StepFor(WorkflowState.Review)!.Id);
        }

        // And the same rows through the read path the Workflow tab actually uses.
        await using (var readModel = fixture.CreateDbContext())
        {
            var store = new WorkflowReadStore(readModel);

            var row = await store.GetProductWorkflowAsync(ProductCodeUnderTest, CancellationToken.None);

            row.ShouldNotBeNull();
            row.TaskStatus.ShouldBe("open");
            row.TaskStepCode.ShouldBe("review");
            row.AssigneeDisplayName.ShouldBe("Anna Becker");

            var history = await store.GetHistoryAsync(row.TaskId!.Value, CancellationToken.None);

            history.Single().Action.ShouldBe("SUBMIT");
            history.Single().FromStepCode.ShouldBe("draft");
            history.Single().ToStepCode.ShouldBe("review");
        }
    }

    private const string ProductCodeUnderTest = "WF-ROUNDTRIP-1";

    /// <summary>
    /// Idempotent on purpose: this class shares one container with the other
    /// integration suites and must not care whether theirs ran first.
    /// </summary>
    private const string Seed = """
        SET search_path TO catalog;

        INSERT INTO locale (code, name, is_base) VALUES ('de-DE', 'Deutsch', true)
          ON CONFLICT (code) DO NOTHING;

        INSERT INTO taxonomy_node (code, node_type, path, depth) VALUES ('wf_root', 'FAMILY', 'wf_root', 0)
          ON CONFLICT (code) DO NOTHING;

        INSERT INTO product (product_code, primary_node_id, base_locale, workflow_state, owner_display_name)
        SELECT 'WF-ROUNDTRIP-1', tn.id, 'de-DE', 'draft', 'Anna Becker'
        FROM taxonomy_node tn WHERE tn.code = 'wf_root'
          ON CONFLICT (product_code) DO NOTHING;
        """;

    private static List<WorkflowState> Reachable(WorkflowDefinition pipeline, WorkflowState from) =>
        Enum.GetValues<WorkflowState>().Where(to => pipeline.Allows(from, to)).ToList();
}
