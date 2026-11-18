using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// Drains the outbox: reclaim, claim, publish, record outcome, repeat.
///
/// Delivery is AT-LEAST-ONCE and cannot be otherwise. The relay publishes and
/// then records success in two separate operations, so a crash between them
/// redelivers. Making that exactly-once would require a distributed transaction
/// across PostgreSQL and the broker, which is a far worse trade than asking
/// consumers to deduplicate on a message id we already carry.
///
/// The loop never throws. A BackgroundService whose ExecuteAsync throws is torn
/// down silently and the host keeps serving traffic — the queue would stop
/// draining with nothing obviously wrong. Every failure is caught, logged and
/// retried on the next pass instead.
/// </summary>
public sealed partial class OutboxRelay(
    OutboxStore store,
    IOutboxTransport transport,
    OutboxMetrics metrics,
    IOptions<OutboxOptions> options,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    /// <summary>
    /// Lease owner. Machine plus process plus a per-start guid: the guid is
    /// what makes a restarted process on the same host a different owner, so a
    /// previous incarnation's leases are correctly treated as abandoned rather
    /// than as its own.
    /// </summary>
    private readonly string _leaseOwner =
        $"{Environment.MachineName}/{Environment.ProcessId}/{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        LogStarted(logger, _leaseOwner, transport.Description, _options.BatchSize);

        if (!transport.IsDeliveringExternally)
        {
            LogNoExternalDelivery(logger, transport.Description);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var claimedCount = 0;

            try
            {
                await ReclaimAsync(stoppingToken);
                claimedCount = await DrainOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown. Anything still leased is reclaimed by whichever
                // relay runs next, once the lease lapses.
                break;
            }
#pragma warning disable CA1031 // The loop must survive every failure; see class remarks.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogPassFailed(logger, ex);
            }

            // A full batch means there is probably more waiting, so go straight
            // round again; a burst then drains at the transport's pace rather
            // than one batch per poll interval. Anything less means the queue
            // is drained and polling again immediately would just spin.
            if (claimedCount >= _options.BatchSize)
            {
                continue;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        LogStopped(logger, _leaseOwner);
    }

    private async Task ReclaimAsync(CancellationToken cancellationToken)
    {
        var reclaimed = await store.ReclaimExpiredLeasesAsync(_options.MaxAttempts, cancellationToken);

        if (reclaimed > 0)
        {
            metrics.RecordReclaimed(reclaimed);
            LogReclaimed(logger, reclaimed);
        }
    }

    /// <returns>How many messages were claimed this pass.</returns>
    private async Task<int> DrainOnceAsync(CancellationToken cancellationToken)
    {
        var batch = await store.ClaimAsync(
            _options.BatchSize, _options.LeaseSeconds, _leaseOwner, cancellationToken);

        if (batch.Count == 0)
        {
            return 0;
        }

        // Sequential on purpose. The claim query guarantees at most one
        // in-flight message per aggregate, so publishing this batch in parallel
        // would not actually break per-aggregate ordering — but it would make
        // failure handling concurrent for no measured benefit at this volume.
        // Revisit with a number, not a hunch.
        foreach (var message in batch)
        {
            await PublishOneAsync(message, cancellationToken);
        }

        return batch.Count;
    }

    private async Task PublishOneAsync(OutboxClaim message, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await transport.PublishAsync(message, cancellationToken);
            stopwatch.Stop();

            var stillOwned = await store.MarkPublishedAsync(message.Id, _leaseOwner, cancellationToken);

            if (stillOwned)
            {
                metrics.RecordPublished(message.EventType, stopwatch.Elapsed.TotalMilliseconds);
            }
            else
            {
                // The lease lapsed while the transport was working and another
                // relay has taken the row. The message was published twice;
                // that is within the at-least-once contract, but a persistent
                // pattern of it means LeaseSeconds is set below real publish
                // latency and should be raised.
                LogLeaseLostAfterPublish(logger, message.MessageId, _options.LeaseSeconds);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A transport may throw anything; the relay must classify, not crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            stopwatch.Stop();
            await RecordFailureAsync(message, ex, cancellationToken);
        }
    }

    private async Task RecordFailureAsync(OutboxClaim message, Exception ex, CancellationToken cancellationToken)
    {
        var attemptJustMade = message.AttemptCount + 1;
        var willDie = attemptJustMade >= _options.MaxAttempts;

        var backoffSeconds = OutboxRetryPolicy.NextDelaySeconds(
            attemptJustMade, _options.BackoffBaseSeconds, _options.BackoffMaxSeconds);

        try
        {
            await store.MarkFailedAsync(
                message.Id,
                _leaseOwner,
                ex.ToString(),
                backoffSeconds,
                _options.MaxAttempts,
                cancellationToken);
        }
#pragma warning disable CA1031 // Recording the failure failed; the lease lapse is the backstop.
        catch (Exception recordEx)
#pragma warning restore CA1031
        {
            // The database is unreachable too. Nothing more can be done here —
            // the row stays 'publishing' and the lease reclaim will return it.
            LogFailureNotRecorded(logger, recordEx, message.MessageId);
            return;
        }

        if (willDie)
        {
            metrics.RecordDeadLettered(message.EventType);
            LogDeadLettered(logger, ex, message.MessageId, message.EventType, message.AggregateId, attemptJustMade);
        }
        else
        {
            metrics.RecordFailed(message.EventType);
            LogPublishFailed(logger, ex, message.MessageId, message.EventType, attemptJustMade, backoffSeconds);
        }
    }

    [LoggerMessage(EventId = 7001, Level = LogLevel.Warning,
        Message = "Outbox relay is disabled by configuration; nothing will be published.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Information,
        Message = "Outbox relay started as {LeaseOwner} via {Transport}, batch size {BatchSize}.")]
    private static partial void LogStarted(ILogger logger, string leaseOwner, string transport, int batchSize);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Warning,
        Message = "Outbox transport {Transport} does not deliver to another process. " +
                  "Messages will be drained and discarded — this is a development configuration.")]
    private static partial void LogNoExternalDelivery(ILogger logger, string transport);

    [LoggerMessage(EventId = 7004, Level = LogLevel.Information,
        Message = "Outbox relay {LeaseOwner} stopped.")]
    private static partial void LogStopped(ILogger logger, string leaseOwner);

    [LoggerMessage(EventId = 7005, Level = LogLevel.Error,
        Message = "Outbox relay pass failed; retrying on the next interval.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7006, Level = LogLevel.Warning,
        Message = "Reclaimed {Count} outbox message(s) whose lease expired mid-publish.")]
    private static partial void LogReclaimed(ILogger logger, int count);

    [LoggerMessage(EventId = 7007, Level = LogLevel.Warning,
        Message = "Publish of {MessageId} succeeded but the lease had already lapsed; another relay owns it now. " +
                  "The message was delivered more than once. Consider raising LeaseSeconds above {LeaseSeconds}.")]
    private static partial void LogLeaseLostAfterPublish(ILogger logger, Guid messageId, int leaseSeconds);

    [LoggerMessage(EventId = 7008, Level = LogLevel.Warning,
        Message = "Outbox publish of {MessageId} ({EventType}) failed on attempt {Attempt}; " +
                  "retrying in {BackoffSeconds}s.")]
    private static partial void LogPublishFailed(
        ILogger logger, Exception exception, Guid messageId, string eventType, int attempt, int backoffSeconds);

    [LoggerMessage(EventId = 7009, Level = LogLevel.Error,
        Message = "Outbox message {MessageId} ({EventType}) for aggregate {AggregateId} was DEAD-LETTERED after " +
                  "{Attempt} attempts. It will not be retried and a consumer never saw it.")]
    private static partial void LogDeadLettered(
        ILogger logger, Exception exception, Guid messageId, string eventType, Guid aggregateId, int attempt);

    [LoggerMessage(EventId = 7010, Level = LogLevel.Error,
        Message = "Could not record the publish failure for {MessageId}; the lease will expire and the message " +
                  "will be reclaimed.")]
    private static partial void LogFailureNotRecorded(ILogger logger, Exception exception, Guid messageId);
}
