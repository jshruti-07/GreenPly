namespace Pim.Catalog.Domain.Products;

/// <summary>
/// The editorial pipeline, as far as the Product aggregate needs to know about
/// it: which state moves are permitted.
///
/// Up to Sprint 26 the answer was a private Dictionary inside Product. That put
/// an ORGANISATION'S process inside a PRODUCT'S invariants — there is nothing
/// about a product that makes Draft to Published impossible, only something
/// about how this client chooses to work. Sprint 27 moves the content of the
/// rule into workflow_definition rows and leaves the invariant here: a product
/// still cannot be moved to an arbitrary state, it can only be moved somewhere
/// the configured pipeline allows.
///
/// The aggregate depends on this interface and not on WorkflowDefinition so
/// that a domain test can state the pipeline it is testing against in one line
/// instead of building a definition graph.
/// </summary>
public interface IWorkflowPolicy
{
    bool Allows(WorkflowState current, WorkflowState target);
}
