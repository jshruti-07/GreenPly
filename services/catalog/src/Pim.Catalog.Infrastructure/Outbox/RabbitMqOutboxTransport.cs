using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// Publishes outbox messages to RabbitMQ.
///
/// This is the whole of what ADR-007 deferred. The outbox itself — the
/// transactional enqueue, per-aggregate ordering, leasing, backoff and
/// dead-lettering — was built and verified against a log-only transport; this
/// type does the one thing that was missing, and does nothing else. It holds no
/// retry logic, no state machine and no idea that a database exists, because
/// all of that is the relay's and duplicating it here is how two half-correct
/// retry policies end up fighting each other.
///
/// PUBLISHER CONFIRMS ARE NOT OPTIONAL HERE. <see cref="IOutboxTransport"/>
/// says returning normally means "the broker has accepted responsibility", and
/// a fire-and-forget publish would satisfy that contract's signature while
/// breaking its meaning: the relay would mark the row published and the message
/// would be gone. The channel is opened with confirmation tracking on, so the
/// await below does not complete until the broker has acked.
///
/// WHAT AN ACK DOES AND DOES NOT MEAN. It means the exchange took the message.
/// With a topic exchange and no bound queue it does NOT mean anyone will ever
/// read it — that is normal for pub/sub and is the consumer's problem to solve
/// by binding a durable queue before the events they care about are produced.
/// Publishing with <c>mandatory</c> would turn "nobody is listening yet" into a
/// delivery failure and dead-letter perfectly good messages during any window
/// when a consumer is being deployed.
/// </summary>
public sealed partial class RabbitMqOutboxTransport : IOutboxTransport, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqOutboxTransport> _logger;

    // One connection and one channel for the process. The relay publishes from
    // a single loop, so a channel pool would add contention handling for
    // contention that does not exist. IChannel is not thread-safe, which the
    // gate below enforces rather than assumes.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;
    private bool _exchangeDeclared;
    private bool _disposed;

    public RabbitMqOutboxTransport(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqOutboxTransport> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
    }

    public string Description =>
        string.Format(
            CultureInfo.InvariantCulture,
            "RabbitMQ exchange '{0}'",
            _options.Exchange);

    /// <summary>
    /// True, and that is the point of this class existing: it is what stops the
    /// health check reporting Degraded and lets a drained queue mean what it
    /// looks like it means.
    /// </summary>
    public bool IsDeliveringExternally => true;

    public async Task PublishAsync(OutboxClaim message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The relay treats a throw as "attempt failed, retry with backoff", so
        // every failure path below simply propagates. Catching here would strip
        // the outbox of the only signal it has.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.ConfirmTimeoutSeconds));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = await EnsureChannelAsync(timeout.Token).ConfigureAwait(false);

            var properties = new BasicProperties
            {
                // The consumer's idempotency key. Carried in the AMQP property
                // rather than only in the body so a consumer can deduplicate
                // without parsing JSON it may not yet be able to read.
                MessageId = message.MessageId.ToString("D", CultureInfo.InvariantCulture),
                Type = message.EventType,
                ContentType = "application/json",
                ContentEncoding = "utf-8",

                // Survives a broker restart. Pointless without a durable
                // exchange and a durable queue on the other side, but the half
                // this service controls is set correctly.
                Persistent = true,

                Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["schema_version"] = message.SchemaVersion,
                    ["aggregate_type"] = message.AggregateType,
                    ["aggregate_id"] = message.AggregateId.ToString("D", CultureInfo.InvariantCulture),

                    // Present so an operator reading a message off a queue can
                    // tell a first delivery from a fifth without correlating
                    // against the outbox table.
                    ["attempt"] = message.AttemptCount + 1,
                },
            };

            if (message.CorrelationId is { } correlationId)
            {
                properties.CorrelationId = correlationId.ToString("D", CultureInfo.InvariantCulture);
            }

            var body = Encoding.UTF8.GetBytes(message.Payload);

            // Routing key IS the event type. A topic exchange then lets a
            // consumer bind 'catalog.product.created', 'catalog.product.*' or
            // 'catalog.#' and choose its own granularity, without this service
            // maintaining a routing table of who wants what.
            await channel.BasicPublishAsync(
                exchange: _options.Exchange,
                routingKey: message.EventType,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: timeout.Token).ConfigureAwait(false);

            LogPublished(_logger, message.EventType, message.MessageId, _options.Exchange);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The linked token fired, not the caller's: the broker did not
            // confirm in time. Rethrown as a timeout so the relay records
            // something an operator can act on rather than a bare cancellation,
            // which reads like a clean shutdown.
            LogConfirmTimedOut(_logger, message.MessageId, _options.ConfirmTimeoutSeconds);

            throw new TimeoutException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "RabbitMQ did not confirm message {0} within {1}s.",
                    message.MessageId,
                    _options.ConfirmTimeoutSeconds));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Opens the connection and channel on first use, and reopens them if the
    /// client could not recover on its own.
    ///
    /// Lazy rather than eager, so the API starts whether or not the broker is
    /// up. Connecting in the constructor would make a broker outage during
    /// deployment into a service that will not boot.
    /// </summary>
    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } open)
        {
            return open;
        }

        await DisposeChannelAsync().ConfigureAwait(false);

        var factory = new ConnectionFactory
        {
            Uri = new Uri(_options.Uri!),
            AutomaticRecoveryEnabled = _options.AutomaticRecovery,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(_options.RecoveryIntervalSeconds),
            ClientProvidedName = "pim-catalog-outbox",
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        // publisherConfirmationsEnabled turns confirms on; the tracking flag is
        // what makes BasicPublishAsync await the ack rather than returning as
        // soon as the frame is written. Without the second one this class would
        // satisfy IOutboxTransport's signature and violate its contract.
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken).ConfigureAwait(false);

        if (!_exchangeDeclared)
        {
            // Idempotent, and re-run after any reconnect because a broker that
            // was restarted without persistent definitions has forgotten it.
            await _channel.ExchangeDeclareAsync(
                exchange: _options.Exchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _exchangeDeclared = true;
        }

        LogConnected(_logger, _options.Exchange);

        return _channel;
    }

    private async ValueTask DisposeChannelAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
            _channel = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        // Cleared with the connection: a fresh one may be pointing at a broker
        // that has never heard of this exchange.
        _exchangeDeclared = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await DisposeChannelAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    [LoggerMessage(
        EventId = 7110,
        Level = LogLevel.Information,
        Message = "Outbox transport connected to RabbitMQ, publishing to exchange {Exchange}.")]
    private static partial void LogConnected(ILogger logger, string exchange);

    [LoggerMessage(
        EventId = 7111,
        Level = LogLevel.Debug,
        Message = "Outbox published {EventType} message {MessageId} to {Exchange}.")]
    private static partial void LogPublished(
        ILogger logger, string eventType, Guid messageId, string exchange);

    [LoggerMessage(
        EventId = 7112,
        Level = LogLevel.Warning,
        Message = "RabbitMQ did not confirm outbox message {MessageId} within {TimeoutSeconds}s; " +
                  "the relay will retry it with backoff.")]
    private static partial void LogConfirmTimedOut(
        ILogger logger, Guid messageId, int timeoutSeconds);
}
