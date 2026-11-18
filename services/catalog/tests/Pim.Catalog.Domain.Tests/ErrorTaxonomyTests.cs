using System.Reflection;
using System.Text.RegularExpressions;
using Pim.Catalog.Domain.Products;
using Pim.SharedKernel.Results;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Domain.Tests;

/// <summary>
/// The error code is the machine-readable half of every RFC 7807 response, and
/// it is the part clients branch on. Once published it is a contract.
///
/// These tests pin the shape of that contract rather than each individual code:
/// a new error added in a later sprint is caught if it breaks the convention,
/// duplicates an existing code with different wording, or maps to a status
/// class that would mislead a client into retrying something unretryable.
/// </summary>
public sealed partial class ErrorTaxonomyTests
{
    /// <summary>Area.Thing or Area.Thing.Problem — PascalCase segments only.</summary>
    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)+$")]
    private static partial Regex CodePattern();

    /// <summary>
    /// Every <c>Error</c> exposed by the domain, found by reflecting over the
    /// static members of the *Errors classes and any public static Error fields.
    /// Factory methods are invoked with placeholder arguments — the codes are
    /// constant, so the arguments only affect the message.
    /// </summary>
    private static IEnumerable<(string Source, Error Error)> AllErrors()
    {
        var assembly = typeof(ProductErrors).Assembly;

        foreach (var type in assembly.GetTypes().Where(t => t.IsClass && t.IsAbstract && t.IsSealed))
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType == typeof(Error) && field.GetValue(null) is Error error)
                {
                    yield return ($"{type.Name}.{field.Name}", error);
                }
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (property.PropertyType == typeof(Error) && property.GetValue(null) is Error error)
                {
                    yield return ($"{type.Name}.{property.Name}", error);
                }
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.ReturnType != typeof(Error) || method.IsSpecialName)
                {
                    continue;
                }

                var arguments = method.GetParameters()
                    .Select(p => PlaceholderFor(p.ParameterType))
                    .ToArray();

                if (method.Invoke(null, arguments) is Error error)
                {
                    yield return ($"{type.Name}.{method.Name}", error);
                }
            }
        }
    }

    private static object? PlaceholderFor(Type type) =>
        type == typeof(string) ? "x" : type.IsValueType ? Activator.CreateInstance(type) : null;

    [Fact]
    public void ErrorsAreDiscovered_SoAPassingSuiteIsNotVacuous()
    {
        AllErrors().Count().ShouldBeGreaterThan(40);
    }

    [Fact]
    public void EveryCode_FollowsTheDottedPascalConvention()
    {
        var offenders = AllErrors()
            .Where(e => !CodePattern().IsMatch(e.Error.Code))
            .Select(e => $"{e.Source} -> '{e.Error.Code}'")
            .ToList();

        offenders.ShouldBeEmpty(
            $"These codes do not match Area.Thing[.Problem]: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void EveryCode_IsUsedForExactlyOneMeaning()
    {
        // The same code with two different messages means a client branching on
        // the code cannot know which situation it is in.
        var conflicting = AllErrors()
            .GroupBy(e => e.Error.Code, StringComparer.Ordinal)
            .Where(g => g.Select(e => e.Error.Message).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.Key)
            .ToList();

        conflicting.ShouldBeEmpty(
            $"These codes carry more than one message: {string.Join(", ", conflicting)}");
    }

    [Fact]
    public void EveryMessage_IsAFullSentenceForAHuman()
    {
        // The message is rendered verbatim in the UI. A bare code or fragment
        // reaching a steward is a defect, not a detail.
        var offenders = AllErrors()
            .Where(e => e.Error.Message.Length < 12 || !e.Error.Message.EndsWith('.'))
            .Select(e => $"{e.Source} -> '{e.Error.Message}'")
            .ToList();

        offenders.ShouldBeEmpty(
            $"These messages are not complete sentences: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void NoMessage_LeaksInternalVocabulary()
    {
        // Column and table names, stack frames and SQL belong in logs. A steward
        // reading 'row_version' learns nothing and it hints at the schema.
        string[] leaks = ["row_version", "product_id", "SELECT ", "npgsql", "Exception", "::"];

        var offenders = AllErrors()
            .Where(e => leaks.Any(l => e.Error.Message.Contains(l, StringComparison.OrdinalIgnoreCase)))
            .Select(e => $"{e.Source} -> '{e.Error.Message}'")
            .ToList();

        offenders.ShouldBeEmpty(
            $"These messages leak internal vocabulary: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void NotFoundErrors_AreTypedNotFound()
    {
        // A 'not found' returned as a 400 tells a client to fix its request when
        // the request was fine, and vice versa.
        var offenders = AllErrors()
            .Where(e => e.Error.Code.Contains("NotFound", StringComparison.Ordinal))
            .Where(e => e.Error.Type != ErrorType.NotFound)
            .Select(e => $"{e.Source} ({e.Error.Type})")
            .ToList();

        offenders.ShouldBeEmpty(
            $"These are named NotFound but map to another status: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void DuplicateAndConcurrencyErrors_AreTypedConflict()
    {
        // 409 is the signal that the request was well-formed but the world
        // disagreed — the client should reload, not edit its payload.
        var offenders = AllErrors()
            .Where(e => e.Error.Code.Contains("Duplicate", StringComparison.Ordinal)
                     || e.Error.Code.Contains("InUse", StringComparison.Ordinal)
                     || e.Error.Code.Contains("ConcurrentModification", StringComparison.Ordinal))
            .Where(e => e.Error.Type != ErrorType.Conflict)
            .Select(e => $"{e.Source} ({e.Error.Type})")
            .ToList();

        offenders.ShouldBeEmpty(
            $"These describe a conflict but map to another status: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void NoError_UsesTheUnmappedFailureType()
    {
        // ErrorType.Failure maps to 500. A domain rule that returns one is
        // telling the client the server broke when it did not.
        var offenders = AllErrors()
            .Where(e => e.Error.Type == ErrorType.Failure)
            .Select(e => e.Source)
            .ToList();

        offenders.ShouldBeEmpty(
            $"These would surface as a 500 to the client: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void EveryCode_IsNamespacedByArea()
    {
        // A bare 'NotFound' from one module collides with the next module's.
        string[] knownAreas =
        [
            "Product", "Sku", "Attribute", "Classification", "Compliance",
            "Packaging", "Relationship", "Application", "Asset", "Localization",
            "Workflow",
        ];

        var offenders = AllErrors()
            .Where(e => !knownAreas.Contains(e.Error.Code.Split('.')[0], StringComparer.Ordinal))
            .Select(e => $"{e.Source} -> '{e.Error.Code}'")
            .ToList();

        offenders.ShouldBeEmpty(
            $"These sit outside the known areas ({string.Join(", ", knownAreas)}): {string.Join(", ", offenders)}");
    }
}
