using Microsoft.EntityFrameworkCore;

namespace Sales.ApiService.Data;

public class SalesDbContext : DbContext
{
    public SalesDbContext(DbContextOptions<SalesDbContext> options) : base(options)
    {
    }

    public DbSet<Deal> Deals => Set<Deal>();
}

public class Deal
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
