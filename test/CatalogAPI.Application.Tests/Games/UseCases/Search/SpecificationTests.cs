using CatalogAPI.Application.Contexts.Games.UseCases.Search;
using CatalogAPI.Domain.Contexts.Games.Enums;

namespace CatalogAPI.Application.Tests.Games.UseCases.Search;

public class SpecificationTests
{
    private static Request Valid(
        string? term = "witcher", GameGenre? genre = null, decimal? min = null, decimal? max = null,
        int page = 1, int size = 10) => new(term, genre, min, max, page, size);

    [Fact]
    public void Ensure_WithTermOnly_ShouldBeValid()
        => Assert.True(Specification.Ensure(Valid()).IsValid);

    [Fact]
    public void Ensure_WithFilterOnly_ShouldBeValid()
        => Assert.True(Specification.Ensure(Valid(term: null, genre: GameGenre.RPG)).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Ensure_WithoutTermAndWithoutFilters_ShouldBeInvalid(string? term)
        => Assert.False(Specification.Ensure(Valid(term: term)).IsValid);

    [Fact]
    public void Ensure_WithTermTooLong_ShouldBeInvalid()
        => Assert.False(Specification.Ensure(Valid(term: new string('a', 101))).IsValid);

    [Fact]
    public void Ensure_WithUndefinedGenre_ShouldBeInvalid()
        => Assert.False(Specification.Ensure(Valid(genre: (GameGenre)99)).IsValid);

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, -1)]
    [InlineData(50, 10)]
    public void Ensure_WithInvalidPriceRange_ShouldBeInvalid(int? min, int? max)
        => Assert.False(Specification.Ensure(Valid(min: min, max: max)).IsValid);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public void Ensure_WithInvalidPagination_ShouldBeInvalid(int page, int size)
        => Assert.False(Specification.Ensure(Valid(page: page, size: size)).IsValid);
}
