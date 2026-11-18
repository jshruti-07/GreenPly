using Pim.Catalog.Infrastructure.Outbox;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Integration.Tests.Outbox;

/// <summary>
/// Guards the configuration that decides whether this service publishes at all.
///
/// The failure these prevent is the quiet one. A broker misconfiguration does
/// not crash anything: the relay claims messages, the transport fails to
/// connect, backoff spaces the retries further and further apart, and after
/// eight attempts the messages are dead-lettered. By then the cause is a stack
/// trace several minutes behind the symptom. Rejecting the configuration at
/// startup turns that into one clear message before a single row is claimed.
/// </summary>
public sealed class RabbitMqOptionsValidatorTests
{
    private readonly RabbitMqOptionsValidator _validator = new();

    private static RabbitMqOptions Valid() => new()
    {
        Uri = "amqp://pim:pim_local_dev@localhost:5672/",
        Exchange = "catalog.events",
        ConfirmTimeoutSeconds = 10,
        RecoveryIntervalSeconds = 5,
    };

    [Fact]
    public void NoUri_IsValid_BecauseAbsenceMeansNoBroker()
    {
        // The documented switch. Validating the rest of the section when no
        // broker is configured would turn a deliberate absence — the default
        // for every environment that has not been given one — into a startup
        // failure.
        var result = _validator.Validate(null, new RabbitMqOptions { Uri = null });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void AConfiguredBroker_IsValidated()
    {
        _validator.Validate(null, Valid()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("localhost:5672")]
    public void AMalformedUri_IsRejected(string uri)
    {
        var options = Valid();
        options.Uri = uri;

        _validator.Validate(null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void TheManagementUrl_IsRejectedRatherThanAttempted()
    {
        // Pasting http://localhost:15672 here is an easy mistake — it is the URL
        // an operator actually visits — and connecting to it fails with a
        // protocol error that says nothing about the cause.
        var options = Valid();
        options.Uri = "http://localhost:15672/";

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("amqp");
    }

    [Fact]
    public void AnEmptyExchange_IsRejected()
    {
        // Publishing to the default exchange routes by queue name, which would
        // silently bypass the topic routing every consumer binds to. It would
        // not error — it would just deliver to nobody.
        var options = Valid();
        options.Exchange = "  ";

        _validator.Validate(null, options).Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void AnOutOfRangeConfirmTimeout_IsRejected(int seconds)
    {
        var options = Valid();
        options.ConfirmTimeoutSeconds = seconds;

        _validator.Validate(null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void IsConfigured_TracksTheUri()
    {
        new RabbitMqOptions { Uri = null }.IsConfigured.ShouldBeFalse();
        new RabbitMqOptions { Uri = "   " }.IsConfigured.ShouldBeFalse();
        new RabbitMqOptions { Uri = "amqp://localhost/" }.IsConfigured.ShouldBeTrue();
    }
}
