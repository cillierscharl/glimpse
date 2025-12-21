using Glimpse.Models;
using Microsoft.EntityFrameworkCore;

namespace Glimpse.Data;

public class GlimpseDbContext : DbContext
{
    public GlimpseDbContext(DbContextOptions<GlimpseDbContext> options) : base(options) { }

    public DbSet<Screenshot> Screenshots => Set<Screenshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Screenshot>(entity =>
        {
            entity.HasIndex(e => e.Path).IsUnique();
        });
    }
}
