using FluentValidation.TestHelper;
using Pim.Catalog.Application.Products.ListProducts;
using Shouldly;
using Xunit;

namespace Pim.Catalog.Application.Tests.Products;

public class ListProductsQueryValidatorTests
{
    private readonly ListProductsQueryValidator _validator = new();

    [Fact]
    public void Defaults_AreValid()
    {
        var result = _validator.TestValidate(new ListProductsQuery());
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Page_BelowOne_Fails(int page)
    {
        var result = _validator.TestValidate(new ListProductsQuery(Page: page));
        result.ShouldHaveValidationErrorFor(q => q.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void PageSize_OutOfBounds_Fails(int pageSize)
    {
        var result = _validator.TestValidate(new ListProductsQuery(PageSize: pageSize));
        result.ShouldHaveValidationErrorFor(q => q.PageSize);
    }

    [Fact]
    public void PageSize_AtMax_IsValid()
    {
        var result = _validator.TestValidate(new ListProductsQuery(PageSize: ListProductsQuery.MaxPageSize));
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("code")]
    [InlineData("name")]
    [InlineData("category")]
    [InlineData("status")]
    [InlineData("quality")]
    [InlineData("updated")]
    [InlineData("UPDATED")] // whitelist is case-insensitive
    public void SortBy_Whitelisted_IsValid(string sortBy)
    {
        var result = _validator.TestValidate(new ListProductsQuery(SortBy: sortBy));
        result.ShouldNotHaveValidationErrorFor(q => q.SortBy);
    }

    [Theory]
    [InlineData("updated_at")]
    [InlineData("p.updated_at; DROP TABLE product")]
    [InlineData("")]
    public void SortBy_NotWhitelisted_Fails(string sortBy)
    {
        var result = _validator.TestValidate(new ListProductsQuery(SortBy: sortBy));
        result.ShouldHaveValidationErrorFor(q => q.SortBy);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("DESC")]
    public void SortDir_AscOrDesc_IsValid(string sortDir)
    {
        var result = _validator.TestValidate(new ListProductsQuery(SortDir: sortDir));
        result.ShouldNotHaveValidationErrorFor(q => q.SortDir);
    }

    [Fact]
    public void SortDir_Unknown_Fails()
    {
        var result = _validator.TestValidate(new ListProductsQuery(SortDir: "sideways"));
        result.ShouldHaveValidationErrorFor(q => q.SortDir);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("Review")]
    [InlineData("APPROVAL")]
    [InlineData("Published")]
    [InlineData(null)]
    public void Status_KnownWorkflowStateOrNull_IsValid(string? status)
    {
        var result = _validator.TestValidate(new ListProductsQuery(Status: status));
        result.ShouldNotHaveValidationErrorFor(q => q.Status);
    }

    [Fact]
    public void Status_Unknown_Fails()
    {
        var result = _validator.TestValidate(new ListProductsQuery(Status: "Active"));
        result.ShouldHaveValidationErrorFor(q => q.Status);
    }

    [Fact]
    public void CategoryId_Zero_Fails()
    {
        var result = _validator.TestValidate(new ListProductsQuery(CategoryId: 0));
        result.ShouldHaveValidationErrorFor(q => q.CategoryId);
    }

    [Fact]
    public void Search_OverMaxLength_Fails()
    {
        var result = _validator.TestValidate(
            new ListProductsQuery(Search: new string('x', ListProductsQuery.MaxSearchLength + 1)));
        result.ShouldHaveValidationErrorFor(q => q.Search);
    }
}
