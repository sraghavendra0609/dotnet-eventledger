using EventGateway.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EventGateway.Infrastructure.Persistence;

public sealed class EventGatewayDbContext(DbContextOptions<EventGatewayDbContext> options) : DbContext(options)
{
    public DbSet<EventRecord> Events => Set<EventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.Property(x => x.AccountId).HasMaxLength(64);
        });
    }
}
