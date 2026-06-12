using System.Text.Json;
using EventGateway.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EventGateway.Infrastructure.Persistence;

public sealed class EventGatewayDbContext(DbContextOptions<EventGatewayDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public DbSet<EventRecord> Events => Set<EventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.Property(x => x.AccountId).HasMaxLength(64);
            entity.Property(x => x.Currency).HasMaxLength(10);
            entity.Property(x => x.Metadata).HasConversion(
                new ValueConverter<Dictionary<string, string>?, string?>(
                    v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                    v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOptions)));
        });
    }
}
