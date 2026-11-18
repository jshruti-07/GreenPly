using System.Reflection;
using MediatR;
using Pim.Catalog.Application;
using Pim.Catalog.Application.Auditing;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Application.Tests.Auditing;

/// <summary>
/// The audit trail is opt-in: a command is recorded only if it implements
/// <see cref="IAuditableCommand"/>. That makes forgetting the marker a silent
/// failure — the change happens, nothing errors, and the history is simply
/// missing an entry that nobody notices until an auditor asks.
///
/// These tests close that hole. Any new command must either be auditable or be
/// listed in <see cref="NotAudited"/> with a reason, which forces the decision
/// to be made deliberately rather than by omission.
/// </summary>
public sealed class AuditCoverageTests
{
    /// <summary>
    /// Commands that deliberately do NOT write audit entries.
    /// Add here only with a reason; the list is reviewed at sprint close.
    /// </summary>
    private static readonly Dictionary<string, string> NotAudited = new(StringComparer.Ordinal)
    {
        // The audit trail is keyed by PRODUCT — IAuditableCommand requires a
        // ProductCode and the Activity tab reads by product. An uploaded asset
        // belongs to no product: it lands in the library unlinked, and is
        // attached to products afterwards by a command that IS audited.
        // Implementing the interface here would mean inventing a product code
        // to satisfy a signature, which is worse than the gap.
        //
        // Asset-level history needs an audit surface that is not product-keyed.
        // Recorded as R-25 rather than left as an omission.
        ["UploadAssetCommand"] =
            "Assets are uploaded into the library unlinked, so there is no product to key the "
            + "audit entry to. Needs a non-product-keyed audit surface — see R-25.",
    };

    private static IEnumerable<Type> CommandTypes() =>
        CatalogApplicationAssembly.Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.Name.EndsWith("Command", StringComparison.Ordinal))
            .Where(t => Array.Exists(
                t.GetInterfaces(),
                i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)));

    [Fact]
    public void EveryCommand_IsAuditedOrExplicitlyExempt()
    {
        var missing = CommandTypes()
            .Where(t => !typeof(IAuditableCommand).IsAssignableFrom(t))
            .Where(t => !NotAudited.ContainsKey(t.Name))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        missing.ShouldBeEmpty(
            $"These commands change data but leave no audit entry: {string.Join(", ", missing)}. " +
            "Implement IAuditableCommand, or add the type to NotAudited with a reason.");
    }

    [Fact]
    public void CommandsAreDiscovered_SoAPassingSuiteIsNotVacuous()
    {
        // Guards the guard: if the discovery predicate ever stops matching,
        // the test above would pass by finding nothing at all.
        CommandTypes().Count().ShouldBeGreaterThan(15);
    }

    [Fact]
    public void EveryAuditableCommand_ProducesANonEmptyActionAndDetail()
    {
        // A blank action or detail renders as an empty row in the timeline,
        // which is worse than no row at all.
        var auditable = CommandTypes()
            .Where(t => typeof(IAuditableCommand).IsAssignableFrom(t))
            .ToList();

        auditable.ShouldNotBeEmpty();

        foreach (var type in auditable)
        {
            var instance = (IAuditableCommand)CreateWithDefaults(type);

            instance.AuditAction.ShouldNotBeNullOrWhiteSpace($"{type.Name} has a blank audit action.");
            instance.AuditDetail.ShouldNotBeNullOrWhiteSpace($"{type.Name} has a blank audit detail.");
        }
    }

    [Fact]
    public void AuditActions_UseAConsistentVocabulary()
    {
        // The Activity tab renders each action as a chip and offers them as
        // filters. An ad-hoc verb per command would give stewards a filter list
        // that grows without meaning.
        string[] allowed =
        [
            "created", "updated", "deleted", "published", "workflow",
            "lifecycle", "classified", "linked", "unlinked", "translated",
        ];

        foreach (var type in CommandTypes().Where(t => typeof(IAuditableCommand).IsAssignableFrom(t)))
        {
            var action = ((IAuditableCommand)CreateWithDefaults(type)).AuditAction;

            allowed.ShouldContain(
                action,
                $"{type.Name} uses audit action '{action}', which is not in the agreed vocabulary.");
        }
    }

    /// <summary>
    /// Builds a command with harmless defaults so its computed audit properties
    /// can be read. Commands are records with primary constructors, so the
    /// single public constructor is the one to satisfy.
    /// </summary>
    private static object CreateWithDefaults(Type type)
    {
        var constructor = type.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var arguments = constructor.GetParameters()
            .Select(p => DefaultFor(p.ParameterType))
            .ToArray();

        return constructor.Invoke(arguments);
    }

    private static object? DefaultFor(Type type)
    {
        if (type == typeof(string))
        {
            return "x";
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        // IReadOnlyList<T> and friends: an empty list keeps Count-based
        // detail strings working without inventing content.
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IReadOnlyCollection<>) ||
                definition == typeof(IEnumerable<>) ||
                definition == typeof(List<>))
            {
                return Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));
            }
        }

        return null;
    }
}
