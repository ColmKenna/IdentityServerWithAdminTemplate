using System;
using IdentityServerProject.Services;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

public class ListResultTests
{
    [Fact]
    public void Empty_ReturnsZeroCountAndEmptyItems()
    {
        var result = ListResult<string>.Empty(1, 10);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(1, result.TotalPages);
        Assert.False(result.HasPreviousPage);
        Assert.False(result.HasNextPage);
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(5, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 10, 3)]
    public void TotalPages_CalculatesCorrectPageCount(int totalCount, int pageSize, int expectedTotalPages)
    {
        var result = new ListResult<int>
        {
            Items = Array.Empty<int>(),
            TotalCount = totalCount,
            PageNumber = 1,
            PageSize = pageSize,
        };

        Assert.Equal(expectedTotalPages, result.TotalPages);
    }

    [Theory]
    [InlineData(1, 3, false, true)]
    [InlineData(2, 3, true, true)]
    [InlineData(3, 3, true, false)]
    public void HasPreviousAndNextPage_EvaluatesCorrectly(int pageNumber, int totalPages, bool expectedHasPrev, bool expectedHasNext)
    {
        var pageSize = 10;
        var totalCount = totalPages * pageSize;

        var result = new ListResult<int>
        {
            Items = Array.Empty<int>(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };

        Assert.Equal(expectedHasPrev, result.HasPreviousPage);
        Assert.Equal(expectedHasNext, result.HasNextPage);
    }
}

