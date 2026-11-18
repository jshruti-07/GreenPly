using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pim.Catalog.Infrastructure.Outbox;

namespace Pim.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="OutboxMessage"/> to <c>catalog.outbox_message</c>.
///
/// EF Core owns the INSERT only. Every state transition after that is done by
/// the relay in SQL (see <c>OutboxStore</c>), because claiming work needs
/// <c>FOR UPDATE SKIP LOCKED</c> and EF has no expression for it. Splitting the
/// write path this way is deliberate: the enqueue must join the aggregate's
/// transaction, and the relay must not.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_message", "catalog");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(m => m.MessageId).HasColumnName("message_id").IsRequired();
        builder.HasIndex(m => m.MessageId).IsUnique();

        builder.Property(m => m.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(m => m.EnqueuedAt).HasColumnName("enqueued_at").IsRequired();

        builder.Property(m => m.EventType).HasColumnName("event_type").IsRequired();
        builder.Property(m => m.SchemaVersion).HasColumnName("schema_version").IsRequired();

        builder.Property(m => m.AggregateType).HasColumnName("aggregate_type").IsRequired();
        builder.Property(m => m.AggregateId).HasColumnName("aggregate_id").IsRequired();

        // jsonb, not text: operators query these payloads when a consumer
        // disagrees with the catalogue, and jsonb is what makes that possible
        // without exporting the column first.
        builder.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();

        builder.Property(m => m.CorrelationId).HasColumnName("correlation_id");

        builder.Property(m => m.Status).HasColumnName("status").IsRequired();
        builder.Property(m => m.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(m => m.NextAttemptAt).HasColumnName("next_attempt_at").IsRequired();

        builder.Property(m => m.LeaseExpiresAt).HasColumnName("lease_expires_at");
        builder.Property(m => m.LeasedBy).HasColumnName("leased_by");

        builder.Property(m => m.LastError).HasColumnName("last_error");
        builder.Property(m => m.PublishedAt).HasColumnName("published_at");
    }
}
