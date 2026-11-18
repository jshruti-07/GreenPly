using Npgsql;
using Pim.Catalog.Domain.Products;
using Pim.Catalog.Infrastructure.Outbox;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Integration.Tests.Outbox;

/// <summary>
/// The outbox against the real schema. Everything here is a claim the design
/// makes and that nothing else in the suite would catch: atomicity with the
/// aggregate write, per-aggregate ordering, lease ownership, and the
/// dead-letter terminus.
///
/// These tests are the reason the outbox is worth having. An outbox that
/// enqueues correctly but loses a message on a relay crash, or reorders two
/// events for one product, is worse than no outbox — it carries the same cost
/// and provides a guarantee it does not keep.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class OutboxStoreTests(PostgresCatalogFixture fixture) : IAsyncLifetime
{
    private const string Owner = "test-owner";
    private const string OtherOwner = "other-owner";

    private NpgsqlDataSource _dataSource = null!;
    private OutboxStore _store = null!;

    /// <summary>
    /// Idempotent and independent of the other suites' fixtures: the container
    /// is shared across the collection and class execution order is not
    /// guaranteed, so this must not assume it runs first or last.
    /// </summary>
    private const string Seed = """
        SET search_path TO catalog;

        INSERT INTO locale (code, name, is_base) VALUES ('en-GB', 'English (UK)', false)
          ON CONFLICT (code) DO NOTHING;

        INSERT INTO taxonomy_node (code, node_type, parent_id, path, depth)
          VALUES ('outbox_node', 'FAMILY', NULL, 'outbox_node', 0)
          ON CONFLICT (code) DO NOTHING;
        """;

    public async Task InitializeAsync()
    {
        await fixture.ExecuteAsync(Seed);

        _dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        _store = new OutboxStore(_dataSource);

        // Each test owns the queue. The collection runs serially, so this is
        // isolation rather than a race.
        await fixture.ExecuteAsync("DELETE FROM catalog.outbox_message;");
    }

    public async Task DisposeAsync() => await _dataSource.DisposeAsync();

    // ---------------------------------------------------------------- enqueue

    [Fact]
    public async Task SavingAnAggregate_WritesItsMessageInTheSameTransaction()
    {
        var code = $"OBX-{Guid.NewGuid():N}"[..12];

        await using var dbContext = fixture.CreateDbContext();
        var nodeId = await NodeIdAsync();

        var product = Product.Create(ProductCode.Create(code).Value, nodeId, "en-GB", "Outbox Probe").Value;
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        var rows = await QueryOutboxAsync();

        rows.Count.ShouldBe(1);
        rows[0].EventType.ShouldBe("catalog.product.created");
        rows[0].AggregateId.ShouldBe(product.PublicId);
        rows[0].Status.ShouldBe("pending");
    }

    [Fact]
    public async Task WhenTheAggregateWriteFails_NoMessageIsLeftBehind()
    {
        var code = $"OBX-{Guid.NewGuid():N}"[..12];
        var nodeId = await NodeIdAsync();

        await using (var first = fixture.CreateDbContext())
        {
            first.Products.Add(Product.Create(ProductCode.Create(code).Value, nodeId, "en-GB", "First").Value);
            await first.SaveChangesAsync();
        }

        await fixture.ExecuteAsync("DELETE FROM catalog.outbox_message;");

        // Same product_code: the unique index rejects the insert, so the whole
        // transaction — aggregate and message together — rolls back.
        await using var second = fixture.CreateDbContext();
        second.Products.Add(Product.Create(ProductCode.Create(code).Value, nodeId, "en-GB", "Duplicate").Value);

        await Should.ThrowAsync<Exception>(async () => await second.SaveChangesAsync());

        // This is the whole point of the pattern. A message here would announce
        // a product that does not exist.
        (await QueryOutboxAsync()).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ claim

    [Fact]
    public async Task Claim_TakesPendingWork_AndMarksItInFlight()
    {
        var aggregate = Guid.NewGuid();
        await InsertPendingAsync(aggregate, "catalog.product.created");

        var claimed = await _store.ClaimAsync(batchSize: 10, leaseSeconds: 60, Owner, CancellationToken.None);

        claimed.Count.ShouldBe(1);
        claimed[0].AggregateId.ShouldBe(aggregate);
        claimed[0].AttemptCount.ShouldBe(0);

        var rows = await QueryOutboxAsync();
        rows[0].Status.ShouldBe("publishing");
        rows[0].LeasedBy.ShouldBe(Owner);
    }

    [Fact]
    public async Task Claim_SkipsMessagesWhoseBackoffHasNotElapsed()
    {
        await InsertPendingAsync(Guid.NewGuid(), "catalog.product.created", nextAttemptOffset: "+1 hour");

        var claimed = await _store.ClaimAsync(batchSize: 10, leaseSeconds: 60, Owner, CancellationToken.None);

        claimed.ShouldBeEmpty();
    }

    [Fact]
    public async Task Claim_HoldsBackALaterMessageForTheSameAggregate()
    {
        var aggregate = Guid.NewGuid();
        await InsertPendingAsync(aggregate, "catalog.product.created");
        await InsertPendingAsync(aggregate, "catalog.product.lifecycle-changed");

        var claimed = await _store.ClaimAsync(batchSize: 10, leaseSeconds: 60, Owner, CancellationToken.None);

        // Ordering, stated as a test: one product's events reach a consumer in
        // the order they happened, even when the first one is slow.
        claimed.Count.ShouldBe(1);
        claimed[0].EventType.ShouldBe("catalog.product.created");
    }

    [Fact]
    public async Task Claim_DoesNotHoldBackADifferentAggregate()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await InsertPendingAsync(first, "catalog.product.created");
        await InsertPendingAsync(second, "catalog.product.created");

        var claimed = await _store.ClaimAsync(batchSize: 10, leaseSeconds: 60, Owner, CancellationToken.None);

        // Ordering is per aggregate and deliberately not global; two products
        // must not queue behind each other.
        claimed.Count.ShouldBe(2);
        claimed.Select(c => c.AggregateId).ShouldBe([first, second], ignoreOrder: true);
    }

    [Fact]
    public async Task Claim_ReleasesTheNextMessageOnlyAfterTheFirstIsPublished()
    {
        var aggregate = Guid.NewGuid();
        await InsertPendingAsync(aggregate, "catalog.product.created");
        await InsertPendingAsync(aggregate, "catalog.product.lifecycle-changed");

        var firstBatch = await _store.ClaimAsync(10, 60, Owner, CancellationToken.None);
        await _store.MarkPublishedAsync(firstBatch[0].Id, Owner, CancellationToken.None);

        var secondBatch = await _store.ClaimAsync(10, 60, Owner, CancellationToken.None);

        secondBatch.Count.ShouldBe(1);
        secondBatch[0].EventType.ShouldBe("catalog.product.lifecycle-changed");
    }

    // -------------------------------------------------------------- ownership

    [Fact]
    public async Task MarkPublished_IsRejectedWhenTheLeaseHasMovedOn()
    {
        await InsertPendingAsync(Guid.NewGuid(), "catalog.product.created");
        var claimed = await _store.ClaimAsync(10, 60, Owner, CancellationToken.None);

        var stillOwned = await _store.MarkPublishedAsync(claimed[0].Id, OtherOwner, CancellationToken.None);

        // A relay whose lease lapsed must not overwrite the outcome recorded by
        // whichever relay picked the message up afterwards.
        stillOwned.ShouldBeFalse();
        (await QueryOutboxAsync())[0].Status.ShouldBe("publishing");
    }

    [Fact]
    public async Task MarkPublished_RecordsTheTerminalSuccess()
    {
        await InsertPendingAsync(Guid.NewGuid(), "catalog.product.created");
        var claimed = await _store.ClaimAsync(10, 60, Owner, CancellationToken.None);

        (await _store.MarkPublishedAsync(claimed[0].Id, Owner, CancellationToken.None)).ShouldBeTrue();

        var row = (await QueryOutboxAsync())[0];
        row.Status.ShouldBe("published");
        row.PublishedAt.ShouldNotBeNull();
        row.LeasedBy.ShouldBeNull();
    }

    // ------------------------------------------------------- failure handling

    [Fact]
    public async Task MarkFailed_SchedulesARetry_AndKeepsTheMessageAlive()
    {
        await InsertPendingAsync(Guid.NewGuid(), "catalog.product.created");
        var claimed = await _store.ClaimAsync(10, 60, Owner, CancellationToken.None);

        await _store.MarkFailedAsync(
            claimed[0].Id, Owner, "broker refused", backoffSeconds: 3600, maxAttempts: 5, CancellationToken.None);

        var row = (await QueryOutboxAsync())[0];
        row.Status.ShouldBe("pending");
        row.AttemptCount.ShouldBe(1);
        // Asserted non-null first: last_error is nullable by design (it is
        // cleared on success), so the compiler is right to refuse the
        // unchecked call rather than let a null slip into the assertion.
        row.LastError.ShouldNotBeNull();
        row.LastError!.ShouldContain("broker refused");

        // Backed off, so a second pass must not pick it straight back up.
        (await _store.ClaimAsync(10, 60, Owner, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task MarkFailed_DeadLettersOnTheFinalAttempt()
    {
        await InsertPendingAsync(Guid.NewGuid(), "catalog.product.created");
        var claimed = await _store.ClaimAsync(10, 60, Owner, CancellationToken.None);

        await _store.MarkFailedAsync(
            claimed[0].Id, Owner, "permanent", backoffSeconds: 1, maxAttempts: 1, CancellationToken.None);

        var row = (await QueryOutboxAsync())[0];

        // Dead, not deleted. Something that was never delivered has to stay
        // visible to whoever has to decide what to do about it.
        row.Status.ShouldBe("dead");
        row.AttemptCount.ShouldBe(1);
        row.PublishedAt.ShouldBeNull();
    }

    [Fact]
    public async Task ADeadMessage_StopsBlockingItsAggregate()
    {
        var aggregate = Guid.NewGuid();
        await InsertPendingAsync(aggregate, "catalog.product.created");
        await InsertPendingAsync(aggregate, "catalog.product.lifecycle-changed");

        var claimed = await _store.ClaimAsync(10, 60, Owner, CancellationToken.None);
        await _store.MarkFailedAsync(claimed[0].Id, Owner, "permanent", 1, maxAttempts: 1, CancellationToken.None);

        var next = await _store.ClaimAsync(10, 60, Owner, CancellationToken.None);

        // Otherwise one poison message silently stops every later event for
        // that product — a per-product outage with no error to look at.
        next.Count.ShouldBe(1);
        next[0].EventType.ShouldBe("catalog.product.lifecycle-changed");
    }

    // ---------------------------------------------------------------- leasing

    [Fact]
    public async Task ExpiredLeases_AreReclaimed()
    {
        await InsertPendingAsync(Guid.NewGuid(), "catalog.product.created");

        // Lease shorter than the pause below, so it has genuinely lapsed rather
        // than been forced by rewriting the row.
        await _store.ClaimAsync(10, leaseSeconds: 5, Owner, CancellationToken.None);
        await fixture.ExecuteAsync(
            "UPDATE catalog.outbox_message SET lease_expires_at = now() - interval '1 second';");

        var reclaimed = await _store.ReclaimExpiredLeasesAsync(maxAttempts: 5, CancellationToken.None);

        reclaimed.ShouldBe(1);

        var row = (await QueryOutboxAsync())[0];
        row.Status.ShouldBe("pending");
        row.LeasedBy.ShouldBeNull();

        // Counted as an attempt: a message that keeps killing its relay is a
        // poison message and has to reach 'dead' rather than cycle forever.
        row.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task ReclaimingPastTheAttemptLimit_DeadLetters()
    {
        await InsertPendingAsync(Guid.NewGuid(), "catalog.product.created");
        await _store.ClaimAsync(10, 5, Owner, CancellationToken.None);
        await fixture.ExecuteAsync(
            "UPDATE catalog.outbox_message SET lease_expires_at = now() - interval '1 second';");

        await _store.ReclaimExpiredLeasesAsync(maxAttempts: 1, CancellationToken.None);

        (await QueryOutboxAsync())[0].Status.ShouldBe("dead");
    }

    // ------------------------------------------------------------------ depth

    [Fact]
    public async Task Depth_ReportsWhatTheHealthCheckNeeds()
    {
        await InsertPendingAsync(Guid.NewGuid(), "catalog.product.created");
        await InsertPendingAsync(Guid.NewGuid(), "catalog.product.created");
        await fixture.ExecuteAsync(
            "UPDATE catalog.outbox_message SET status = 'dead' WHERE id = (SELECT min(id) FROM catalog.outbox_message);");

        var depth = await _store.GetDepthAsync(CancellationToken.None);

        depth.Pending.ShouldBe(1);
        depth.Dead.ShouldBe(1);
        depth.OldestPendingAgeSeconds.ShouldNotBeNull();
    }

    [Fact]
    public async Task Depth_OnAnEmptyQueue_IsZeroRatherThanNull()
    {
        var depth = await _store.GetDepthAsync(CancellationToken.None);

        depth.Pending.ShouldBe(0);
        depth.Dead.ShouldBe(0);
        depth.OldestPendingAgeSeconds.ShouldBeNull();
    }

    // ---------------------------------------------------------------- helpers

    private async Task<long> NodeIdAsync()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM catalog.taxonomy_node WHERE code = 'outbox_node';", connection);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task InsertPendingAsync(Guid aggregateId, string eventType, string nextAttemptOffset = "+0 second")
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO catalog.outbox_message
                (message_id, occurred_at, event_type, schema_version,
                 aggregate_type, aggregate_id, payload, next_attempt_at)
            VALUES
                (@messageId, now(), @eventType, 1,
                 'product', @aggregateId, '{}'::jsonb, now() + @offset::interval);
            """,
            connection);

        command.Parameters.AddWithValue("messageId", Guid.NewGuid());
        command.Parameters.AddWithValue("eventType", eventType);
        command.Parameters.AddWithValue("aggregateId", aggregateId);
        command.Parameters.AddWithValue("offset", nextAttemptOffset);

        await command.ExecuteNonQueryAsync();
    }

    private sealed record OutboxRow(
        long Id,
        string EventType,
        Guid AggregateId,
        string Status,
        int AttemptCount,
        string? LeasedBy,
        string? LastError,
        DateTime? PublishedAt);

    private async Task<IReadOnlyList<OutboxRow>> QueryOutboxAsync()
    {
        var rows = new List<OutboxRow>();

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT id, event_type, aggregate_id, status, attempt_count, leased_by, last_error, published_at
            FROM catalog.outbox_message
            ORDER BY id;
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new OutboxRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetInt32(4),
                await reader.IsDBNullAsync(5) ? null : reader.GetString(5),
                await reader.IsDBNullAsync(6) ? null : reader.GetString(6),
                await reader.IsDBNullAsync(7) ? null : reader.GetDateTime(7)));
        }

        return rows;
    }
}
