using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pim.Catalog.Domain.Workflow;

namespace Pim.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ApprovalTask"/> and its history to approval_task /
/// approval_history.
///
/// These are EF-mapped and not Dapper-written for one reason, and it is the
/// design of the module rather than a preference: the task move, the history
/// row and product.workflow_state must commit together or not at all. A Dapper
/// store would open its own connection and its own transaction, and the failure
/// mode — a product showing Approved with no approval in its trail — is exactly
/// the thing a governance module exists to prevent.
///
/// Reads go the other way: the Workflow tab's history list and the board come
/// from Dapper read stores, because they join across steps and products and
/// have no business loading an aggregate.
/// </summary>
public sealed class ApprovalTaskConfiguration : IEntityTypeConfiguration<ApprovalTask>
{
    public void Configure(EntityTypeBuilder<ApprovalTask> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("approval_task", "catalog");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(t => t.PublicId).HasColumnName("public_id").IsRequired();
        builder.HasIndex(t => t.PublicId).IsUnique();

        builder.Property(t => t.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(t => t.DefinitionId).HasColumnName("definition_id").IsRequired();
        builder.Property(t => t.CurrentStepId).HasColumnName("current_step_id").IsRequired();

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasConversion(
                status => status.ToString().ToLowerInvariant(),
                value => Enum.Parse<ApprovalTaskStatus>(value, true));

        builder.Property(t => t.Priority)
            .HasColumnName("priority")
            .IsRequired()
            .HasConversion(
                priority => priority.ToString().ToLowerInvariant(),
                value => Enum.Parse<ApprovalPriority>(value, true));

        builder.Property(t => t.DueDate).HasColumnName("due_date");
        builder.Property(t => t.AssigneeUserId).HasColumnName("assignee_user_id");
        builder.Property(t => t.AssigneeDisplayName).HasColumnName("assignee_display_name");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // Owned, like product_tr: history is never loaded without its task and
        // never edited after it is written.
        builder.OwnsMany(t => t.History, history =>
        {
            history.ToTable("approval_history", "catalog");
            history.WithOwner().HasForeignKey(h => h.TaskId);

            history.HasKey(h => h.Id);
            history.Property(h => h.Id).HasColumnName("id").UseIdentityAlwaysColumn();

            history.Property(h => h.TaskId).HasColumnName("task_id").IsRequired();
            history.Property(h => h.FromStepId).HasColumnName("from_step_id");
            history.Property(h => h.ToStepId).HasColumnName("to_step_id").IsRequired();

            // The database token, not the enum name: REQUEST_CHANGES and
            // RequestChanges are the same action spelled for two audiences, and
            // WorkflowActions owns the translation between them.
            history.Property(h => h.Action)
                .HasColumnName("action")
                .IsRequired()
                .HasConversion(
                    action => WorkflowActions.ToToken(action),
                    value => Parse(value));

            history.Property(h => h.ActorUserId).HasColumnName("actor_user_id");
            history.Property(h => h.ActorDisplay).HasColumnName("actor_display");
            history.Property(h => h.Comment).HasColumnName("comment");
            history.Property(h => h.OccurredAt).HasColumnName("occurred_at").IsRequired();
        });
    }

    /// <summary>
    /// Throws rather than defaulting on an unrecognised token. A row that says
    /// APPROVED and materialises as Submit would be a falsified approval trail,
    /// which is worse than a failed read.
    /// </summary>
    private static WorkflowAction Parse(string value) =>
        WorkflowActions.TryParse(value, out var action)
            ? action
            : throw new InvalidWorkflowDefinitionException(
                $"The approval trail contains an action '{value}' this build does not recognise.");
}
