using Glimpse.Models;
using Microsoft.EntityFrameworkCore;

namespace Glimpse.Data;

public class GlimpseDbContext : DbContext
{
    public GlimpseDbContext(DbContextOptions<GlimpseDbContext> options) : base(options) { }

    public DbSet<Screenshot> Screenshots => Set<Screenshot>();
    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Screenshot>(entity =>
        {
            entity.HasIndex(e => e.Path).IsUnique();

            // Many-to-many relationship with Tags
            entity.HasMany(s => s.Tags)
                  .WithMany(t => t.Screenshots)
                  .UsingEntity(j => j.ToTable("ScreenshotTags"));
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
        });
    }
}
