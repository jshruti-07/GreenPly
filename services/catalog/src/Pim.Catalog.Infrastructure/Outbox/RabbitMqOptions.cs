using System.ComponentModel.DataAnnotations;

namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// Broker settings for <see cref="RabbitMqOutboxTransport"/>.
///
/// Absence is meaningful: when <see cref="Uri"/> is not set, no RabbitMQ
/// transport is registered and the outbox keeps draining to the log-only one.
/// That is deliberate. A service that cannot reach a broker should say so
/// through the health check rather than refuse to start — the catalog API's job
/// is serving the grid, and it can do that perfectly well while messaging is
/// down. The alternative, failing startup on a missing broker, turns a
/// messaging outage into a total one.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "Outbox:RabbitMq";

    /// <summary>
    /// AMQP connection string, e.g. <c>amqp://user:pass@localhost:5672/</c>.
    /// Null or blank means "no broker configured" — see the type remarks.
    /// </summary>
    public string? Uri { get; set; }

    /// <summary>
    /// The exchange catalog publishes to. A <b>topic</b> exchange, declared
    /// durable, so a consumer can bind as narrowly as
    /// <c>catalog.product.created</c> or as broadly as <c>catalog.#</c> without
    /// this service knowing who is listening.
    ///
    /// This service declares the exchange and nothing else. Queues and bindings
    /// belong to the consumers that read them: a producer that declares its
    /// consumers' queues has quietly taken ownership of their retention,
    /// dead-lettering and lifetime, and will break them the next time it is
    /// deployed with a different opinion.
    /// </summary>
    [Required]
    public string Exchange { get; set; } = "catalog.events";

    /// <summary>
    /// Seconds to wait for the broker to confirm a publish before treating the
    /// attempt as failed.
    ///
    /// Bounded on purpose. <see cref="IOutboxTransport.PublishAsync"/> must not
    /// return until the broker has taken responsibility, so this call blocks a
    /// relay slot for its whole duration; an unbounded wait against a wedged
    /// broker would stall the relay rather than fail the message and let
    /// backoff do its work. Must stay comfortably below
    /// <see cref="OutboxOptions.LeaseSeconds"/>, or the lease lapses while the
    /// publish is still in flight and a second relay picks the same message up.
    /// </summary>
    [Range(1, 120)]
    public int ConfirmTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Whether the client reconnects by itself after a broker restart or a
    /// network blip. On by default: without it the first outage would leave a
    /// dead connection behind and every subsequent publish would fail until the
    /// API was restarted, which is a worse failure than the one it is reacting
    /// to.
    /// </summary>
    public bool AutomaticRecovery { get; set; } = true;

    /// <summary>Seconds between the client's own reconnection attempts.</summary>
    [Range(1, 300)]
    public int RecoveryIntervalSeconds { get; set; } = 5;

    /// <summary>True when a broker has actually been configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Uri);
}
