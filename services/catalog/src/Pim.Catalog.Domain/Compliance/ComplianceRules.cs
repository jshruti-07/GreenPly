using Pim.SharedKernel.Results;

namespace Pim.Catalog.Domain.Compliance;

/// <summary>Document types recognised by the compliance module.</summary>
public static class ComplianceDocType
{
    public static readonly string[] All = ["DOP", "CE", "EPD", "DPP", "TEST", "UKCA", "ISI", "FSC", "CARB", "EPA", "IGBC"];

    public static bool IsKnown(string docType) =>
        Array.Exists(All, t => string.Equals(t, docType, StringComparison.Ordinal));
}

/// <summary>Administrative state of a certificate, set by a steward.</summary>
public static class ComplianceStatus
{
    public const string Valid = "valid";
    public const string Withdrawn = "withdrawn";
    public const string Superseded = "superseded";

    public static readonly string[] All = [Valid, Withdrawn, Superseded];

    public static bool IsKnown(string status) =>
        Array.Exists(All, s => string.Equals(s, status, StringComparison.Ordinal));
}

/// <summary>
/// Expiry state, DERIVED from valid_to and the current date — never stored.
///
/// A stored expiry flag is wrong the day after it is written unless something
/// rewrites it nightly, and a silently-wrong compliance flag in this industry
/// means shipping a product whose certification has lapsed.
/// </summary>
public enum ExpiryState
{
    /// <summary>No end date recorded — a DoP, for instance, does not expire.</summary>
    NoExpiry,

    Valid,

    /// <summary>Expires within the warning window; renewal should be scheduled.</summary>
    Expiring,

    Expired,
}

public static class ComplianceExpiry
{
    /// <summary>
    /// Certification renewal in construction runs to months, not weeks: twelve
    /// months' notice is what makes the warning actionable rather than alarming.
    /// </summary>
    public const int WarningWindowMonths = 12;

    /// <summary>
    /// Classifies a certificate against a reference date.
    ///
    /// <paramref name="today"/> is passed in rather than read from the clock so
    /// the boundaries are testable and so one request classifies every document
    /// against the same instant.
    /// </summary>
    public static ExpiryState Classify(DateOnly? validTo, DateOnly today)
    {
        if (validTo is not { } expiry)
        {
            return ExpiryState.NoExpiry;
        }

        // Inclusive: a certificate valid to 31 Dec is still valid ON 31 Dec.
        if (expiry < today)
        {
            return ExpiryState.Expired;
        }

        return expiry <= today.AddMonths(WarningWindowMonths)
            ? ExpiryState.Expiring
            : ExpiryState.Valid;
    }
}

public static class ComplianceErrors
{
    public static Error NotFound(long id) =>
        Error.NotFound("Compliance.NotFound", $"Compliance document {id} was not found on this product.");

    public static Error UnknownDocType(string docType) =>
        Error.Validation("Compliance.UnknownDocType",
            $"'{docType}' is not a recognised document type. Expected one of: {string.Join(", ", ComplianceDocType.All)}.");

    public static Error UnknownStatus(string status) =>
        Error.Validation("Compliance.UnknownStatus",
            $"'{status}' is not a recognised status. Expected one of: {string.Join(", ", ComplianceStatus.All)}.");

    public static readonly Error ValidToBeforeValidFrom =
        Error.Validation("Compliance.InvalidDateRange", "Valid-to cannot be earlier than valid-from.");

    public static Error DuplicateReference(string reference) =>
        Error.Conflict("Compliance.DuplicateReference",
            $"Certificate '{reference}' is already recorded against this product.");
}
