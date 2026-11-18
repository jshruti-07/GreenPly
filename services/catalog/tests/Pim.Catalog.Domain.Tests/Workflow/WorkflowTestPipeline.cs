using Pim.Catalog.Domain.Products;
using Pim.Catalog.Domain.Workflow;

namespace Pim.Catalog.Domain.Tests.Workflow;

/// <summary>
/// Pipelines for domain tests.
///
/// <see cref="ProductPublishing"/> states the shipped process a SECOND time,
/// independently of db/init/01_catalog_schema.sql. That is deliberate and is
/// not a duplicated source of truth: nothing in production reads it, and
/// WorkflowDefinitionSeedTests loads the real definition out of a real database
/// and asserts the two agree. Two independent statements plus a test that they
/// match is how you find out that someone edited one of them.
/// </summary>
internal static class WorkflowTestPipeline
{
    public static WorkflowStepDefinition Step(
        long id, WorkflowState state, int order, bool initial = false, bool terminal = false) =>
        new(id, state.ToString().ToLowerInvariant(), state.ToString(), order,
            OwnerRole: null, initial, terminal, state);

    /// <summary>The pipeline as shipped: draft, review, approval, published.</summary>
    public static WorkflowDefinition ProductPublishing()
    {
        var draft = Step(1, WorkflowState.Draft, 1, initial: true);
        var review = Step(2, WorkflowState.Review, 2);
        var approval = Step(3, WorkflowState.Approval, 3);
        var published = Step(4, WorkflowState.Published, 4, terminal: true);

        return WorkflowDefinition.Create(
            id: 1,
            code: "product_publishing",
            name: "Product Publishing",
            entityType: "PRODUCT",
            version: 1,
            steps: [draft, review, approval, published],
            transitions:
            [
                new(1, draft.Id, review.Id, WorkflowAction.Submit, "Submit for review", 1),
                new(2, review.Id, approval.Id, WorkflowAction.Approve, "Pass review", 1),
                new(3, review.Id, draft.Id, WorkflowAction.RequestChanges, "Request changes", 2),
                new(4, approval.Id, published.Id, WorkflowAction.Approve, "Approve and publish", 1),
                new(5, approval.Id, draft.Id, WorkflowAction.Reject, "Reject", 2),
                new(6, approval.Id, draft.Id, WorkflowAction.RequestChanges, "Request changes", 3)
            ]);
    }

    /// <summary>
    /// A two-step pipeline that allows Draft straight to Published — the move
    /// the old hardcoded table made impossible. Used to prove the rule really
    /// is configuration now and not a constant with extra steps.
    /// </summary>
    public static WorkflowDefinition StraightToPublished()
    {
        var draft = Step(10, WorkflowState.Draft, 1, initial: true);
        var published = Step(11, WorkflowState.Published, 2, terminal: true);

        return WorkflowDefinition.Create(
            id: 2,
            code: "fast_track",
            name: "Fast Track",
            entityType: "PRODUCT",
            version: 1,
            steps: [draft, published],
            transitions:
            [
                new(20, draft.Id, published.Id, WorkflowAction.Publish, "Publish", 1)
            ]);
    }
}
