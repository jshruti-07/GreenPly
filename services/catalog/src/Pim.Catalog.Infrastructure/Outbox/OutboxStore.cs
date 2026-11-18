using Npgsql;
using Pim.Catalog.Infrastructure.ReadModels;

namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>One claimed message, as handed to the transport.</summary>
public sealed record OutboxClaim(
    long Id,
    Guid MessageId,
    string EventType,
    int SchemaVersion,
    string AggregateType,
    Guid AggregateId,
    string Payload,
    Guid? CorrelationId,
    DateTimeOffset OccurredAt,
    int AttemptCount);

/// <summary>Queue depth, for the health check and the metrics gauge.</summary>
public sealed record OutboxDepth(long Pending, long Publishing, long Dead, double? OldestPendingAgeSeconds);

/// <summary>
/// Every SQL statement the relay runs.
///
/// It holds its own <see cref="NpgsqlDataSource"/> rather than borrowing the
/// request DbContext's connection, because the relay is a singleton background
/// worker with no request scope to borrow from. Reads elsewhere in this service
/// deliberately share EF's connection so they can see uncommitted writes; the
/// relay wants the opposite — it must only ever see committed messages.
///
/// Queries go through <see cref="DapperGateway"/>'s connection-taking overloads
/// rather than calling Dapper directly. That is not indirection for its own
/// sake: Dapper's snake_case mapping is applied in that type's static
/// constructor, and R-12 is the record of what happens when code reaches Dapper
/// by another route — every projection fails to materialise. Touching the type
/// is what guarantees the configuration ran.
/// </summary>
public sealed class OutboxStore(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// Claims a batch and marks it in flight.
    ///
    /// Two things make this more than a SELECT:
    ///
    /// <c>FOR UPDATE SKIP LOCKED</c> lets several relays share the queue
    /// without blocking each other — a locked row is skipped, not waited on.
    ///
    /// The <c>NOT EXISTS</c> guard is the per-aggregate ordering guarantee: a
    /// message is only eligible when no earlier message for the same aggregate
    /// is still pending or in flight. So a product's "created" cannot overtake
    /// its own "lifecycle-changed" because the first one is being retried.
    /// Ordering is guaranteed per aggregate, never globally, and consumers must
    /// not assume otherwise.
    ///
    /// The advisory lock closes the one gap the guard leaves. Under READ
    /// COMMITTED two relays can evaluate NOT EXISTS against snapshots taken
    /// before either commits its claim, and both could then consider adjacent
    /// messages for the same aggregate eligible. A transaction-scoped advisory
    /// lock on the aggregate id serialises that window. It is released when
    /// this claim transaction ends, not held across the publish — holding a
    /// database lock for the duration of a network call is exactly what the
    /// lease exists to avoid.
    /// </summary>
    private const string ClaimSql = """
        WITH claimable AS (
            SELECT o.id
            FROM catalog.outbox_message o
            WHERE o.status = 'pending'
              AND o.next_attempt_at <= now()
              AND NOT EXISTS (
                  SELECT 1
                  FROM catalog.outbox_message earlier
                  WHERE earlier.aggregate_id = o.aggregate_id
                    AND earlier.id < o.id
                    AND earlier.status IN ('pending', 'publishing')
              )
              AND pg_try_advisory_xact_lock(hashtextextended(o.aggregate_id::text, 0))
            ORDER BY o.id
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED
        )
        UPDATE catalog.outbox_message m
        SET status           = 'publishing',
            lease_expires_at = now() + make_interval(secs => @LeaseSeconds),
            leased_by        = @LeasedBy
        FROM claimable c
        WHERE m.id = c.id
        RETURNING m.id,
                  m.message_id,
                  m.event_type,
                  m.schema_version,
                  m.aggregate_type,
                  m.aggregate_id,
                  m.payload,
                  m.correlation_id,
                  m.occurred_at,
                  m.attempt_count;
        """;

    /// <summary>
    /// Terminal success. Guarded on <c>status = 'publishing'</c> so a relay
    /// whose lease already lapsed — and whose row another relay has since taken
    /// — cannot overwrite the newer owner's outcome. The affected-row count
    /// tells the caller whether it still owned the message.
    /// </summary>
    private const string MarkPublishedSql = """
        UPDATE catalog.outbox_message
        SET status           = 'published',
            published_at     = now(),
            lease_expires_at = NULL,
            leased_by        = NULL,
            last_error       = NULL
        WHERE id = @Id
          AND status = 'publishing'
          AND leased_by = @LeasedBy;
        """;

    /// <summary>
    /// Failure. Increments the attempt count and either schedules a retry or
    /// dead-letters, in one statement so the decision cannot interleave with
    /// another relay's.
    ///
    /// <c>last_error</c> is truncated: a driver exception can carry a very
    /// large message, and an unbounded error column turns a broker outage into
    /// table bloat.
    /// </summary>
    private const string MarkFailedSql = """
        UPDATE catalog.outbox_message
        SET attempt_count    = attempt_count + 1,
            status           = CASE WHEN attempt_count + 1 >= @MaxAttempts THEN 'dead' ELSE 'pending' END,
            next_attempt_at  = CASE WHEN attempt_count + 1 >= @MaxAttempts
                                    THEN next_attempt_at
                                    ELSE now() + make_interval(secs => @BackoffSeconds) END,
            lease_expires_at = NULL,
            leased_by        = NULL,
            last_error       = left(@Error, 4000)
        WHERE id = @Id
          AND status = 'publishing'
          AND leased_by = @LeasedBy;
        """;

    /// <summary>
    /// Returns rows whose relay died mid-publish. Without this they would sit
    /// in 'publishing' forever and, because 'publishing' blocks their
    /// aggregate's ordering guard, would stall every later message for that
    /// aggregate too — a single crash quietly becoming a per-product outage.
    ///
    /// The attempt count is deliberately incremented: a message that
    /// repeatedly kills its relay is a poison message and must reach 'dead'
    /// rather than cycle forever.
    /// </summary>
    private const string ReclaimSql = """
        UPDATE catalog.outbox_message
        SET status           = CASE WHEN attempt_count + 1 >= @MaxAttempts THEN 'dead' ELSE 'pending' END,
            attempt_count    = attempt_count + 1,
            lease_expires_at = NULL,
            leased_by        = NULL,
            last_error       = 'Lease expired before the publish completed; reclaimed.'
        WHERE status = 'publishing'
          AND lease_expires_at IS NOT NULL
          AND lease_expires_at < now();
        """;

    private const string DepthSql = """
        SELECT
            count(*) FILTER (WHERE status = 'pending')                          AS pending,
            count(*) FILTER (WHERE status = 'publishing')                       AS publishing,
            count(*) FILTER (WHERE status = 'dead')                             AS dead,
            -- extract() returns numeric, which Npgsql surfaces as decimal; the
            -- cast keeps the mapping to double explicit rather than leaving it
            -- to a conversion that would only fail at runtime.
            extract(epoch FROM (now() - min(enqueued_at) FILTER (WHERE status = 'pending')))::double precision
                                                                                AS oldest_pending_age_seconds
        FROM catalog.outbox_message;
        """;

    public async Task<IReadOnlyList<OutboxClaim>> ClaimAsync(
        int batchSize,
        int leaseSeconds,
        string leasedBy,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await DapperGateway.QueryOnAsync<OutboxClaim>(
            connection,
            ClaimSql,
            new { BatchSize = batchSize, LeaseSeconds = (double)leaseSeconds, LeasedBy = leasedBy },
            cancellationToken);
    }

    /// <returns><c>true</c> if this relay still owned the message.</returns>
    public async Task<bool> MarkPublishedAsync(long id, string leasedBy, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var affected = await DapperGateway.ExecuteOnAsync(
            connection,
            MarkPublishedSql,
            new { Id = id, LeasedBy = leasedBy },
            cancellationToken);

        return affected == 1;
    }

    /// <returns><c>true</c> if this relay still owned the message.</returns>
    public async Task<bool> MarkFailedAsync(
        long id,
        string leasedBy,
        string error,
        int backoffSeconds,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var affected = await DapperGateway.ExecuteOnAsync(
            connection,
            MarkFailedSql,
            new
            {
                Id = id,
                LeasedBy = leasedBy,
                Error = error,
                BackoffSeconds = (double)backoffSeconds,
                MaxAttempts = maxAttempts
            },
            cancellationToken);

        return affected == 1;
    }

    /// <returns>Number of stranded messages returned to the queue.</returns>
    public async Task<int> ReclaimExpiredLeasesAsync(int maxAttempts, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await DapperGateway.ExecuteOnAsync(
            connection,
            ReclaimSql,
            new { MaxAttempts = maxAttempts },
            cancellationToken);
    }

    public async Task<OutboxDepth> GetDepthAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var depth = await DapperGateway.QuerySingleOrDefaultOnAsync<OutboxDepth>(
            connection,
            DepthSql,
            parameters: null,
            cancellationToken);

        // An aggregate query over an empty table still returns one row, so null
        // means the query did not run rather than that the queue is empty.
        return depth ?? new OutboxDepth(0, 0, 0, null);
    }
}
