namespace FinBank.TransfersService.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Domain;

/// <summary>
/// Entity Framework Core DbContext for transfers schema
/// Includes both Transfer and OutboxEntry tables
/// </summary>
public class TransfersDbContext : DbContext
{
    public DbSet<Transfer> Transfers { get; set; } = null!;
    public DbSet<OutboxEntry> OutboxEntries { get; set; } = null!;

    public TransfersDbContext(DbContextOptions<TransfersDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("transfers");

        // Transfers table
        modelBuilder.Entity<Transfer>(builder =>
        {
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Id).ValueGeneratedNever();

            builder.Property(t => t.SourceAccountId).IsRequired().HasIndex();
            builder.Property(t => t.TargetAccountId).IsRequired().HasIndex();
            builder.Property(t => t.UserId).IsRequired().HasIndex();
            builder.Property(t => t.Amount).HasColumnType("numeric(19,4)").IsRequired();
            builder.Property(t => t.Reference).HasMaxLength(255);
            builder.Property(t => t.CreatedAt).IsRequired().HasDefaultValueSql("now()");

            builder.ToTable("transfers", schema: "transfers");
        });

        // Outbox table (for guaranteed event delivery)
        modelBuilder.Entity<OutboxEntry>(builder =>
        {
            builder.HasKey(o => o.Id);
            builder.Property(o => o.Id).ValueGeneratedNever();

            builder.Property(o => o.AggregateId).IsRequired();
            builder.Property(o => o.EventType).IsRequired().HasMaxLength(255);
            builder.Property(o => o.Payload).HasColumnType("jsonb").IsRequired();
            builder.Property(o => o.RoutingKey).HasMaxLength(255);
            builder.Property(o => o.PublishedAt);
            builder.Property(o => o.CreatedAt).IsRequired().HasDefaultValueSql("now()");

            builder.HasIndex(o => o.PublishedAt).HasFilter("\"published_at\" IS NULL");

            builder.ToTable("outbox_entries", schema: "transfers");
        });
    }
}

/// <summary>
/// Outbox entry: guarantees event delivery even if broker is down
/// Published = null: pending delivery
/// Published != null: delivered successfully
/// </summary>
public class OutboxEntry
{
    public Guid Id { get; set; }
    public Guid AggregateId { get; set; }  // Transfer ID
    public string EventType { get; set; } = null!;
    public string Payload { get; set; } = null!;  // JSON
    public string? RoutingKey { get; set; }  // RabbitMQ routing key
    public DateTime? PublishedAt { get; set; }  // NULL = not published yet
    public DateTime CreatedAt { get; set; }
}
