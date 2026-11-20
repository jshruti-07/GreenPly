using Pim.Catalog.Domain.Assets;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Domain.Tests.Assets;

public sealed class AssetRulesTests
{
    [Theory]
    [InlineData("sheet_hero.jpg", AssetType.Image)]
    [InlineData("sheet_hero.JPEG", AssetType.Image)]
    [InlineData("photo.png", AssetType.Image)]
    [InlineData("sample.webp", AssetType.Image)]
    [InlineData("icon.svg", AssetType.Image)]
    [InlineData("drawing.dxf", AssetType.Cad)]
    [InlineData("model.ifc", AssetType.Bim)]
    [InlineData("clip.mp4", AssetType.Video)]
    [InlineData("spec.pdf", AssetType.Document)]
    [InlineData("manual.docx", AssetType.Document)]
    public void InferType_FromStandardExtension_ReturnsExpectedType(string fileName, string expectedType)
    {
        var result = AssetRules.InferType(fileName);
        result.ShouldBe(expectedType);
    }

    [Theory]
    [InlineData("https://example.com/photo.jpg?w=800&q=80", AssetType.Image)]
    [InlineData("https://cdn.example.test/image.png?version=2", AssetType.Image)]
    [InlineData("https://cdn.example.test/document.pdf?dl=1", AssetType.Document)]
    public void InferType_WithQueryParams_InfersFromExtension(string url, string expectedType)
    {
        var result = AssetRules.InferType(url);
        result.ShouldBe(expectedType);
    }

    [Theory]
    [InlineData("https://images.unsplash.com/photo-1544816155-12df9643f363", AssetRole.Gallery, AssetType.Image)]
    [InlineData("https://images.unsplash.com/photo-1544816155-12df9643f363", AssetRole.Main, AssetType.Image)]
    [InlineData("/api/v1/assets/9c336b11-9689-4911-897b-95eb0746937e/file", AssetRole.Gallery, AssetType.Image)]
    [InlineData("https://example.com/cdn/file-12345", AssetRole.Datasheet, AssetType.Document)]
    [InlineData("https://example.com/cdn/file-12345", AssetRole.Video, AssetType.Video)]
    public void InferType_WithoutExtension_UsesRoleContext(string url, string role, string expectedType)
    {
        var result = AssetRules.InferType(url, role);
        result.ShouldBe(expectedType);
    }

    [Fact]
    public void InferType_WithExplicitDocumentExtension_DoesNotGetOverriddenByRole()
    {
        // A real PDF file should be recognized as DOCUMENT even if mistakenly linked as GALLERY,
        // so that AssetRules.Validate correctly catches and blocks the invalid assignment.
        var result = AssetRules.InferType("manual.pdf", AssetRole.Gallery);
        result.ShouldBe(AssetType.Document);

        var validation = AssetRules.Validate(result, AssetRole.Gallery);
        validation.IsFailure.ShouldBeTrue();
        validation.Error.Code.ShouldBe("Asset.TypeRoleMismatch");
    }

    [Fact]
    public void Validate_ImageWithGalleryRole_Succeeds()
    {
        var validation = AssetRules.Validate(AssetType.Image, AssetRole.Gallery);
        validation.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ImageWithMainRole_Succeeds()
    {
        var validation = AssetRules.Validate(AssetType.Image, AssetRole.Main);
        validation.IsSuccess.ShouldBeTrue();
    }
}
