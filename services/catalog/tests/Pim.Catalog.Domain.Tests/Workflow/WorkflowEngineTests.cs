using Pim.Catalog.Domain.Products;
using Pim.Catalog.Domain.Workflow;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Domain.Tests.Workflow;

public sealed class WorkflowActionTests
{
    [Fact]
    public void EveryAction_HasADatabaseToken()
    {
        // The table in WorkflowActions is written by hand. A member added to the
        // enum without a token would throw the first time anybody used it, and
        // "the Reject button does nothing" is a bad way to find that out.
        foreach (var action in Enum.GetValues<WorkflowAction>())
        {
            WorkflowActions.All.ShouldContain(action, $"{action} is missing from the token table.");
            WorkflowActions.ToToken(action).ShouldNotBeNullOrWhiteSpace();
        }

        WorkflowActions.All.Count.ShouldBe(Enum.GetValues<WorkflowAction>().Length);
    }

    [Fact]
    public void EveryToken_RoundTrips()
    {
        foreach (var action in WorkflowActions.All)
        {
            WorkflowActions.TryParse(WorkflowActions.ToToken(action), out var parsed).ShouldBeTrue();
            parsed.ShouldBe(action);
        }
    }

    [Theory]
    [InlineData("REQUEST_CHANGES", WorkflowAction.RequestChanges)]
    [InlineData("request_changes", WorkflowAction.RequestChanges)]
    [InlineData("RequestChanges", WorkflowAction.RequestChanges)]
    [InlineData("  approve  ", WorkflowAction.Approve)]
    // Case-insensitive against the enum name, so this spelling lands too. Kept
    // as an assertion rather than left implicit: it is the reason the reject
    // list below does not contain it.
    [InlineData("REQUESTCHANGES", WorkflowAction.RequestChanges)]
    public void AcceptsBothSpellings(string input, WorkflowAction expected)
    {
        // REQUEST_CHANGES arrives from SQL, RequestChanges from a JSON body.
        WorkflowActions.TryParse(input, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ESCALATE")]
    [InlineData("REQUEST CHANGES")]
    [InlineData("REQUEST-CHANGES")]
    public void RejectsAnythingElse(string? input)
    {
        WorkflowActions.TryParse(input, out _).ShouldBeFalse();
    }
}

public sealed class WorkflowDefinitionTests
{
    [Fact]
    public void ShippedPipeline_ReachesExactlyTheStatesTheAggregateUsedToHardcode()
    {
        // Sprint 27 changed WHERE the rule lives. This is the assertion that it
        // did not also change WHAT it says.
        var pipeline = WorkflowTestPipeline.ProductPublishing();

        Reachable(pipeline, WorkflowState.Draft).ShouldBe([WorkflowState.Review]);
        Reachable(pipeline, WorkflowState.Review).ShouldBe([WorkflowState.Approval, WorkflowState.Draft], ignoreOrder: true);
        Reachable(pipeline, WorkflowState.Approval).ShouldBe([WorkflowState.Published, WorkflowState.Draft], ignoreOrder: true);
        Reachable(pipeline, WorkflowState.Published).ShouldBeEmpty();
    }

    [Fact]
    public void ApprovalOffersRejectAndRequestChanges_BothLandingOnDraft()
    {
        // Prototype v4.2 draws three buttons on the Approval step. They are two
        // distinct verbs onto one step, which is what the trail is for.
        var pipeline = WorkflowTestPipeline.ProductPublishing();
        var approval = pipeline.StepFor(WorkflowState.Approval)!;

        var actions = pipeline.TransitionsFrom(approval.Id).Select(t => t.Action).ToList();

        actions.ShouldBe(
            [WorkflowAction.Approve, WorkflowAction.Reject, WorkflowAction.RequestChanges],
            ignoreOrder: true);

        pipeline.StepById(pipeline.Resolve(approval.Id, WorkflowAction.Reject)!.ToStepId)!.State
            .ShouldBe(WorkflowState.Draft);
        pipeline.StepById(pipeline.Resolve(approval.Id, WorkflowAction.RequestChanges)!.ToStepId)!.State
            .ShouldBe(WorkflowState.Draft);
    }

    [Fact]
    public void TransitionsAreReturnedInTheOrderTheyAreDeclared()
    {
        // The state-oriented endpoint picks the first transition that reaches a
        // target state, so "first" has to be the pipeline's declared preference
        // rather than whatever the database returned.
        var pipeline = WorkflowTestPipeline.ProductPublishing();
        var approval = pipeline.StepFor(WorkflowState.Approval)!;

        pipeline.TransitionsFrom(approval.Id).Select(t => t.SortOrder).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void ARequestedActionThatDoesNotLeaveThisStep_ResolvesToNothing()
    {
        var pipeline = WorkflowTestPipeline.ProductPublishing();
        var draft = pipeline.StepFor(WorkflowState.Draft)!;

        pipeline.Resolve(draft.Id, WorkflowAction.Approve).ShouldBeNull();
    }

    [Fact]
    public void APipelineWithNoStartingStep_IsRejectedAtLoad()
    {
        var draft = WorkflowTestPipeline.Step(1, WorkflowState.Draft, 1);

        var thrown = Should.Throw<InvalidWorkflowDefinitionException>(() =>
            WorkflowDefinition.Create(1, "broken", "Broken", "PRODUCT", 1, [draft], []));

        thrown.Message.ShouldContain("starting steps");
    }

    [Fact]
    public void APipelineWithTwoStartingSteps_IsRejectedAtLoad()
    {
        var draft = WorkflowTestPipeline.Step(1, WorkflowState.Draft, 1, initial: true);
        var review = WorkflowTestPipeline.Step(2, WorkflowState.Review, 2, initial: true);

        Should.Throw<InvalidWorkflowDefinitionException>(() =>
            WorkflowDefinition.Create(1, "broken", "Broken", "PRODUCT", 1, [draft, review], []));
    }

    [Fact]
    public void ATransitionPointingOutsideThePipeline_IsRejectedAtLoad()
    {
        var draft = WorkflowTestPipeline.Step(1, WorkflowState.Draft, 1, initial: true);

        var thrown = Should.Throw<InvalidWorkflowDefinitionException>(() =>
            WorkflowDefinition.Create(1, "broken", "Broken", "PRODUCT", 1, [draft],
                [new WorkflowTransitionDefinition(9, draft.Id, 999, WorkflowAction.Submit, "Go", 1)]));

        thrown.Message.ShouldContain("outside this pipeline");
    }

    [Fact]
    public void OneActionLeavingOneStepInTwoDirections_IsRejectedAtLoad()
    {
        // The database enforces this with a unique index. Enforced here too, so
        // a definition built any other way cannot slip past it — an ambiguous
        // action would resolve by row order, silently.
        var draft = WorkflowTestPipeline.Step(1, WorkflowState.Draft, 1, initial: true);
        var review = WorkflowTestPipeline.Step(2, WorkflowState.Review, 2);
        var approval = WorkflowTestPipeline.Step(3, WorkflowState.Approval, 3);

        var thrown = Should.Throw<InvalidWorkflowDefinitionException>(() =>
            WorkflowDefinition.Create(1, "broken", "Broken", "PRODUCT", 1, [draft, review, approval],
            [
                new WorkflowTransitionDefinition(1, draft.Id, review.Id, WorkflowAction.Submit, "A", 1),
                new WorkflowTransitionDefinition(2, draft.Id, approval.Id, WorkflowAction.Submit, "B", 2)
            ]));

        thrown.Message.ShouldContain("SUBMIT");
    }

    private static List<WorkflowState> Reachable(WorkflowDefinition pipeline, WorkflowState from) =>
        Enum.GetValues<WorkflowState>().Where(to => pipeline.Allows(from, to)).ToList();
}

public sealed class ApprovalTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static ApprovalTask OpenAtDraft(WorkflowDefinition pipeline) =>
        ApprovalTask.OpenAt(42, pipeline, pipeline.StepFor(WorkflowState.Draft)!, "Anna Becker", Now);

    [Fact]
    public void OpensWhereTheProductAlreadyIs_NotAtTheStartOfThePipeline()
    {
        // The 500 products that predate this module carry a workflow state and
        // no task. Opening at the pipeline's start would rewrite the
        // catalogue's governance state as a side effect of a deployment.
        var pipeline = WorkflowTestPipeline.ProductPublishing();
        var approval = pipeline.StepFor(WorkflowState.Approval)!;

        var task = ApprovalTask.OpenAt(42, pipeline, approval, "Sarah Muller", Now);

        task.CurrentStepId.ShouldBe(approval.Id);
        task.Status.ShouldBe(ApprovalTaskStatus.Open);
        task.History.ShouldBeEmpty();
    }

    [Fact]
    public void RecordsEveryTransition_InOrder()
    {
        var pipeline = WorkflowTestPipeline.ProductPublishing();
        var task = OpenAtDraft(pipeline);

        task.Perform(pipeline, WorkflowAction.Submit, "ready", "Anna", Now).IsSuccess.ShouldBeTrue();
        task.Perform(pipeline, WorkflowAction.Approve, null, "Markus", Now.AddHours(1)).IsSuccess.ShouldBeTrue();

        task.History.Count.ShouldBe(2);
        task.History[0].Action.ShouldBe(WorkflowAction.Submit);
        task.History[0].Comment.ShouldBe("ready");
        task.History[0].ActorDisplay.ShouldBe("Anna");
        task.History[1].Action.ShouldBe(WorkflowAction.Approve);
        task.History[1].Comment.ShouldBeNull();
    }

    [Fact]
    public void CompletesOnReachingATerminalStep()
    {
        var pipeline = WorkflowTestPipeline.ProductPublishing();
        var task = OpenAtDraft(pipeline);

        task.Perform(pipeline, WorkflowAction.Submit, null, null, Now);
        task.Perform(pipeline, WorkflowAction.Approve, null, null, Now);
        task.Perform(pipeline, WorkflowAction.Approve, null, null, Now).IsSuccess.ShouldBeTrue();

        task.Status.ShouldBe(ApprovalTaskStatus.Completed);
        task.CurrentStepId.ShouldBe(pipeline.StepFor(WorkflowState.Published)!.Id);
    }

    [Fact]
    public void ACompletedTask_RefusesFurtherActions()
    {
        var pipeline = WorkflowTestPipeline.ProductPublishing();
        var task = OpenAtDraft(pipeline);

        task.Perform(pipeline, WorkflowAction.Submit, null, null, Now);
        task.Perform(pipeline, WorkflowAction.Approve, null, null, Now);
        task.Perform(pipeline, WorkflowAction.Approve, null, null, Now);

        var result = task.Perform(pipeline, WorkflowAction.Submit, null, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Workflow.TaskAlreadyClosed");
        task.History.Count.ShouldBe(3);
    }

    [Fact]
    public void AnActionUnavailableFromHere_ChangesNothingAndSaysWhichStep()
    {
        var pipeline = WorkflowTestPipeline.ProductPublishing();
        var task = OpenAtDraft(pipeline);

        var result = task.Perform(pipeline, WorkflowAction.Approve, null, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Workflow.ActionNotAvailable");
        result.Error.Message.ShouldContain("Draft");
        task.CurrentStepId.ShouldBe(pipeline.StepFor(WorkflowState.Draft)!.Id);
        task.History.ShouldBeEmpty();
    }

    [Fact]
    public void RejectAndRequestChanges_LandTogetherAndReadApart()
    {
        // Both go to Draft. The whole value of separating them is that the trail
        // can be read back — so assert the trail, not just the destination.
        var pipeline = WorkflowTestPipeline.ProductPublishing();
        var approval = pipeline.StepFor(WorkflowState.Approval)!;
        var task = ApprovalTask.OpenAt(42, pipeline, approval, null, Now);

        task.Perform(pipeline, WorkflowAction.Reject, "certificate expired", "Sarah", Now)
            .Value.State.ShouldBe(WorkflowState.Draft);

        task.History.Single().Action.ShouldBe(WorkflowAction.Reject);
        task.History.Single().Comment.ShouldBe("certificate expired");
    }

    [Fact]
    public void BlankCommentsAndActors_AreStoredAsNothingRatherThanAsBlanks()
    {
        var pipeline = WorkflowTestPipeline.ProductPublishing();
        var task = OpenAtDraft(pipeline);

        task.Perform(pipeline, WorkflowAction.Submit, "   ", "  ", Now);

        task.History.Single().Comment.ShouldBeNull();
        task.History.Single().ActorDisplay.ShouldBeNull();
    }

    [Fact]
    public void AnActorIsNeverGivenAUserId_BecauseThereIsNoIdentityService()
    {
        // R-26 in executable form. If this ever fails it means somebody wired
        // attribution in, and the risk register needs updating with it.
        var pipeline = WorkflowTestPipeline.ProductPublishing();
        var task = OpenAtDraft(pipeline);

        task.Perform(pipeline, WorkflowAction.Submit, null, "Anna Becker", Now);

        task.History.Single().ActorUserId.ShouldBeNull();
        task.AssigneeUserId.ShouldBeNull();
    }
}
