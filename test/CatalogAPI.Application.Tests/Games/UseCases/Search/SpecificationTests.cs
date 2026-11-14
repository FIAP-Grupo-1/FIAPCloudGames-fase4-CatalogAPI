using CatalogAPI.Application.Contexts.Games.UseCases.Search;

namespace CatalogAPI.Application.Tests.Games.UseCases.Search;

public class SpecificationTests
{
    [Fact]
    public void Ensure_WithValidRequest_ShouldReturnValidContract()
        => Assert.True(Specification.Ensure(new Request("witcher", 1, 20)).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ensure_WithoutSearchTerm_ShouldReturnInvalidContract(string? q)
        => Assert.False(Specification.Ensure(new Request(q, 1, 20)).IsValid);

    [Fact]
    public void Ensure_WithSearchTermTooLong_ShouldReturnInvalidContract()
        => Assert.False(Specification.Ensure(
            new Request(new string('a', Specification.MaxQueryLength + 1), 1, 20)).IsValid);

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public void Ensure_WithInvalidPagination_ShouldReturnInvalidContract(int page, int pageSize)
        => Assert.False(Specification.Ensure(new Request("witcher", page, pageSize)).IsValid);
}
