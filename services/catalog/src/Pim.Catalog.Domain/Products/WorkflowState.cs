namespace Pim.Catalog.Domain.Products;

/// <summary>Editorial workflow per the approved database design (product.workflow_state).</summary>
public enum WorkflowState
{
    Draft,
    Review,
    Approval,
    Published
}
