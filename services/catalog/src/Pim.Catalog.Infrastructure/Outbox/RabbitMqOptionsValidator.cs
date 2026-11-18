using Microsoft.Extensions.Options;

namespace Pim.Catalog.Infrastructure.Outbox;

/// <summary>
/// Validates <see cref="RabbitMqOptions"/> on first resolution.
///
/// Hand-written for the same reason <see cref="OutboxOptionsValidator"/> is:
/// the data-annotations validator lives outside the packages this project
/// restores, and a few explicit checks beat an options object nobody checked.
///
/// Only runs meaningfully when a broker is configured. An unset
/// <see cref="RabbitMqOptions.Uri"/> is the documented way to say "no broker",
/// so validating the rest of the section in that case would turn a deliberate
/// absence into a startup failure.
/// </summary>
public sealed class RabbitMqOptionsValidator : IValidateOptions<RabbitMqOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        // Caught here rather than at first publish. A malformed URI would
        // otherwise surface as a failed message with a backoff delay in front
        // of it, which reads like a broker problem and is not one.
        if (!Uri.TryCreate(options.Uri, UriKind.Absolute, out var uri))
        {
            failures.Add($"{nameof(options.Uri)} '{options.Uri}' is not an absolute URI.");
        }
        else if (!uri.Scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase)
              && !uri.Scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"{nameof(options.Uri)} scheme '{uri.Scheme}' is not amqp or amqps. " +
                "A management-API URL is a common thing to paste here by mistake, " +
                "and it fails at connect time with an error that does not say so.");
        }

        if (string.IsNullOrWhiteSpace(options.Exchange))
        {
            failures.Add(
                $"{nameof(options.Exchange)} must be set. Publishing to the default exchange " +
                "would route by queue name and silently bypass the topic routing consumers bind to.");
        }

        if (options.ConfirmTimeoutSeconds is < 1 or > 120)
        {
            failures.Add($"{nameof(options.ConfirmTimeoutSeconds)} must be between 1 and 120.");
        }

        if (options.RecoveryIntervalSeconds is < 1 or > 300)
        {
            failures.Add($"{nameof(options.RecoveryIntervalSeconds)} must be between 1 and 300.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
