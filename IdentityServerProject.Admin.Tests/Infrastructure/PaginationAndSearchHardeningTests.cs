namespace IdentityServerProject.Admin.Tests.Infrastructure;

public class PaginationAndSearchHardeningTests
{
    [Theory]
    [InlineData(0, 0, 1, 1, 0)]
    [InlineData(-10, -20, 1, 1, 0)]
    [InlineData(2, 500, 2, Pagination.MaxPageSize, Pagination.MaxPageSize)]
    [InlineData(3, 25, 3, 25, 50)]
    public void Normalize_ClampsInputsAndCalculatesSkip(
        int pageNumber,
        int pageSize,
        int expectedPageNumber,
        int expectedPageSize,
        int expectedSkip)
    {
        var result = Pagination.Normalize(pageNumber, pageSize);

        Assert.Equal(expectedPageNumber, result.PageNumber);
        Assert.Equal(expectedPageSize, result.PageSize);
        Assert.Equal(expectedSkip, result.Skip);
    }

    [Fact]
    public void Normalize_ExtremePageNumber_DoesNotOverflow()
    {
        var result = Pagination.Normalize(int.MaxValue, Pagination.MaxPageSize);

        Assert.InRange(result.Skip, 0, int.MaxValue);
        Assert.True(result.PageNumber > 0);
    }

    [Theory]
    [InlineData("100%", "100[%]")]
    [InlineData("user_name", "user[_]name")]
    [InlineData("[type]", "[[]type[]]")]
    public void EscapeLikePattern_ProducesLiteralSqlServerPattern(string input, string expected)
    {
        Assert.Equal(expected, LikeExtensions.EscapeLikePattern(input));
    }
}
