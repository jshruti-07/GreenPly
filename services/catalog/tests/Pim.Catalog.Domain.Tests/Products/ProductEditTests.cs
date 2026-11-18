using Pim.Catalog.Domain.Products;
using Pim.Catalog.Domain.Tests.Workflow;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Domain.Tests.Products;

public sealed class ProductEditTests
{
    private static Product Create() =>
        Product.Create(ProductCode.Create("FD-90-GS-1000").Value, 1, "en", "Fire Door 90").Value;

    [Fact]
    public void UpdateGeneral_AppliesEveryFieldAndBumpsRowVersion()
    {
        var product = Create();

        var result = product.UpdateGeneral(
            "Fire Door 90 Renamed", "Anna Becker", "12345678", "MAT-001", expectedRowVersion: 1);

        result.IsSuccess.ShouldBeTrue();
        product.BaseLocaleName.ShouldBe("Fire Door 90 Renamed");
        product.OwnerDisplayName.ShouldBe("Anna Becker");
        product.Gtin.ShouldBe("12345678");
        product.SapMaterialNo.ShouldBe("MAT-001");
        product.RowVersion.ShouldBe(2);
    }

    [Fact]
    public void UpdateGeneral_TrimsAndNullsBlankOptionalFields()
    {
        var product = Create();

        product.UpdateGeneral("  Trimmed  ", "   ", "  ", "", 1).IsSuccess.ShouldBeTrue();

        product.BaseLocaleName.ShouldBe("Trimmed");
        product.OwnerDisplayName.ShouldBeNull();
        product.Gtin.ShouldBeNull();
        product.SapMaterialNo.ShouldBeNull();
    }

    [Fact]
    public void UpdateGeneral_WithStaleRowVersion_ChangesNothing()
    {
        var product = Create();
        product.UpdateGeneral("First edit", null, null, null, 1).IsSuccess.ShouldBeTrue();

        // Second editor still holds version 1.
        var result = product.UpdateGeneral("Second edit", "Someone", null, null, 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.ConcurrentModification");
        product.BaseLocaleName.ShouldBe("First edit");
        product.OwnerDisplayName.ShouldBeNull();
        product.RowVersion.ShouldBe(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateGeneral_WithoutName_FailsAndLeavesProductUntouched(string name)
    {
        var product = Create();

        var result = product.UpdateGeneral(name, "Anna Becker", null, null, 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.Name.Required");
        product.BaseLocaleName.ShouldBe("Fire Door 90");
        product.OwnerDisplayName.ShouldBeNull(); // no partial application
        product.RowVersion.ShouldBe(1);
    }

    [Fact]
    public void UpdateGeneral_WithOverlongName_Fails()
    {
        var product = Create();

        var result = product.UpdateGeneral(
            new string('x', ProductTranslation.MaxNameLength + 1), null, null, null, 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.Name.TooLong");
        product.RowVersion.ShouldBe(1);
    }
}

public sealed class ProductWorkflowTests
{
    /// <summary>
    /// The shipped pipeline, stated for the domain (see WorkflowTestPipeline).
    /// Sprint 27 moved these rules out of the Product aggregate and into
    /// configuration, so a test of workflow behaviour has to say which
    /// configuration it is testing — that is the point, not an inconvenience.
    /// </summary>
    private static readonly Pim.Catalog.Domain.Workflow.WorkflowDefinition Pipeline =
        WorkflowTestPipeline.ProductPublishing();

    private static Product Create() =>
        Product.Create(ProductCode.Create("FD-90-GS-1000").Value, 1, "en", "Fire Door 90").Value;

    [Fact]
    public void NewProduct_StartsInDraft()
    {
        Create().WorkflowState.ShouldBe(WorkflowState.Draft);
    }

    [Fact]
    public void FollowsTheFullEditorialChain()
    {
        var product = Create();

        product.ChangeWorkflowState(WorkflowState.Review, Pipeline).IsSuccess.ShouldBeTrue();
        product.ChangeWorkflowState(WorkflowState.Approval, Pipeline).IsSuccess.ShouldBeTrue();
        product.ChangeWorkflowState(WorkflowState.Published, Pipeline).IsSuccess.ShouldBeTrue();

        product.WorkflowState.ShouldBe(WorkflowState.Published);
        product.RowVersion.ShouldBe(4);
    }

    [Theory]
    [InlineData(WorkflowState.Review)]
    [InlineData(WorkflowState.Approval)]
    public void CanBeRejectedBackToDraft(WorkflowState from)
    {
        var product = Create();
        product.ChangeWorkflowState(WorkflowState.Review, Pipeline);
        if (from == WorkflowState.Approval)
        {
            product.ChangeWorkflowState(WorkflowState.Approval, Pipeline);
        }

        product.ChangeWorkflowState(WorkflowState.Draft, Pipeline).IsSuccess.ShouldBeTrue();
        product.WorkflowState.ShouldBe(WorkflowState.Draft);
    }

    [Theory]
    [InlineData(WorkflowState.Approval)]
    [InlineData(WorkflowState.Published)]
    public void CannotSkipStates(WorkflowState target)
    {
        var product = Create();

        var result = product.ChangeWorkflowState(target, Pipeline);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.Workflow.InvalidTransition");
        product.WorkflowState.ShouldBe(WorkflowState.Draft);
    }

    [Fact]
    public void PublishedIsTerminal()
    {
        var product = Create();
        product.ChangeWorkflowState(WorkflowState.Review, Pipeline);
        product.ChangeWorkflowState(WorkflowState.Approval, Pipeline);
        product.ChangeWorkflowState(WorkflowState.Published, Pipeline);

        product.ChangeWorkflowState(WorkflowState.Draft, Pipeline).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void SameStateIsIdempotentAndDoesNotBumpRowVersion()
    {
        var product = Create();

        product.ChangeWorkflowState(WorkflowState.Draft, Pipeline).IsSuccess.ShouldBeTrue();
        product.RowVersion.ShouldBe(1);
    }
}

/// <summary>
/// The rule really is configuration now. If these passed against the old
/// hardcoded Dictionary as well, Sprint 27 would have changed nothing.
/// </summary>
public sealed class ProductWorkflowIsConfigurableTests
{
    private static Product Create() =>
        Product.Create(ProductCode.Create("FD-90-GS-1000").Value, 1, "en", "Fire Door 90").Value;

    [Fact]
    public void ADifferentPipeline_AllowsAMoveTheShippedOneForbids()
    {
        var product = Create();

        product.ChangeWorkflowState(
                WorkflowState.Published, WorkflowTestPipeline.ProductPublishing())
            .IsFailure.ShouldBeTrue();

        product.ChangeWorkflowState(
                WorkflowState.Published, WorkflowTestPipeline.StraightToPublished())
            .IsSuccess.ShouldBeTrue();

        product.WorkflowState.ShouldBe(WorkflowState.Published);
    }

    [Fact]
    public void APipelineThatDoesNotDefineTheCurrentState_RefusesEveryMove()
    {
        var product = Create();
        product.ChangeWorkflowState(WorkflowState.Review, WorkflowTestPipeline.ProductPublishing());

        // StraightToPublished has no Review step. Refusing beats treating an
        // unknown origin as "anything goes".
        product.ChangeWorkflowState(
                WorkflowState.Published, WorkflowTestPipeline.StraightToPublished())
            .IsFailure.ShouldBeTrue();
    }
}
