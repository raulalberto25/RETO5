namespace FinBank.AccountsService.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Domain;

/// <summary>
/// Entity Framework Core DbContext for accounts schema.
/// Infrastructure layer: responsible for database mapping.
/// </summary>
public class AccountsDbContext : DbContext
{
    public DbSet<Account> Accounts { get; set; } = null!;

    public AccountsDbContext(DbContextOptions<AccountsDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("accounts");

        modelBuilder.Entity<Account>(builder =>
        {
            builder.HasKey(a => a.Id);
            builder.Property(a => a.Id).ValueGeneratedNever();

            builder.Property(a => a.UserId)
                .IsRequired()
                .HasIndex();

            builder.Property(a => a.AccountNumber)
                .IsRequired()
                .HasMaxLength(20)
                .HasIndex(isUnique: true);

            builder.Property(a => a.Balance)
                .HasColumnType("numeric(19,4)")
                .IsRequired()
                .HasConversion(
                    money => money.Amount,
                    amount => Money.Of(amount));

            builder.Property(a => a.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("now()");

            builder.ToTable("accounts", schema: "accounts");
        });
    }
}
