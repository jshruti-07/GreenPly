using Pim.Catalog.Application.Products.Compliance;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Application.Tests.Products;

public sealed class ComplianceValidatorTests
{
    private static AddComplianceDocumentCommand Add(
        string docType = "CE",
        string? reference = "CE-2026-1",
        DateOnly? validFrom = null,
        DateOnly? validTo = null,
        int rowVersion = 1) =>
        new("FD-90-GS-1000", docType, reference, "EN 1634-1", "TÜV", validFrom, validTo, rowVersion);

    private static readonly AddComplianceDocumentCommandValidator AddValidator = new();
    private static readonly UpdateComplianceDocumentCommandValidator UpdateValidator = new();

    [Fact]
    public void ValidAddCommand_Passes() =>
        AddValidator.Validate(Add()).IsValid.ShouldBeTrue();

    [Fact]
    public void UnknownDocType_IsRejected()
    {
        var result = AddValidator.Validate(Add(docType: "ISO9001"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName.Contains("DocType", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidToBeforeValidFrom_IsRejected()
    {
        var result = AddValidator.Validate(Add(
            validFrom: new DateOnly(2026, 6, 1),
            validTo: new DateOnly(2026, 1, 1)));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void EqualDates_AreAllowed()
    {
        // A one-day certificate is unusual but not invalid.
        var day = new DateOnly(2026, 6, 1);

        AddValidator.Validate(Add(validFrom: day, validTo: day)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void OpenEndedCertificate_IsAllowed()
    {
        // A DoP has a start and no expiry.
        AddValidator.Validate(Add(validFrom: new DateOnly(2026, 1, 1), validTo: null))
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void MissingRowVersion_IsRejected()
    {
        // Guards against a client that omits the field and would otherwise
        // send 0, silently bypassing the concurrency check.
        AddValidator.Validate(Add(rowVersion: 0)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void OverlongReference_IsRejected()
    {
        var result = AddValidator.Validate(Add(reference: new string('x', 200)));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void ExpiredIsNotAnAcceptableStatus()
    {
        // Expiry is derived, so it must not be storable as a status.
        var command = new UpdateComplianceDocumentCommand(
            "FD-90-GS-1000", 1, "CE", "CE-1", "EN 1634-1", "TÜV", null, null, "expired", 1);

        UpdateValidator.Validate(command).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void WithdrawnIsAcceptable()
    {
        var command = new UpdateComplianceDocumentCommand(
            "FD-90-GS-1000", 1, "CE", "CE-1", "EN 1634-1", "TÜV", null, null, "withdrawn", 1);

        UpdateValidator.Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void UpdateWithoutDocumentId_IsRejected()
    {
        var command = new UpdateComplianceDocumentCommand(
            "FD-90-GS-1000", 0, "CE", "CE-1", "EN 1634-1", "TÜV", null, null, "valid", 1);

        UpdateValidator.Validate(command).IsValid.ShouldBeFalse();
    }
}
