using DisputePortal.Modules.DebitOrders.Domain;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Modules.DebitOrders.Infrastructure;

internal sealed class DebitOrdersDbContext(DbContextOptions<DebitOrdersDbContext> options) : DbContext(options)
{
    public const string Schema = "debit_orders";

    public DbSet<Mandate> Mandates => Set<Mandate>();

    public DbSet<DebitOrder> DebitOrders => Set<DebitOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Mandate>(mandate =>
        {
            mandate.ToTable("mandates");
            mandate.HasKey(m => m.Id);
            mandate.Property(m => m.Id).ValueGeneratedNever();
            mandate.Property(m => m.MandateReference).HasMaxLength(35);
            mandate.Property(m => m.ContractReference).HasMaxLength(35);
            mandate.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
            mandate.Property(m => m.CreditorName).HasMaxLength(140);
            mandate.Property(m => m.CreditorShortName).HasMaxLength(20);
            mandate.Property(m => m.InstalmentAmount).HasPrecision(18, 2);
            mandate.Property(m => m.MaximumCollectionAmount).HasPrecision(18, 2);
            mandate.Property(m => m.Currency).HasMaxLength(3).IsFixedLength();
            mandate.Property(m => m.Frequency).HasConversion<string>().HasMaxLength(20);
            mandate.Property(m => m.AuthenticationType).HasMaxLength(40);
            mandate.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
            mandate.HasIndex(m => m.MandateReference).IsUnique();
            mandate.HasIndex(m => new { m.CustomerId, m.Type });
        });

        modelBuilder.Entity<DebitOrder>(debitOrder =>
        {
            debitOrder.ToTable("debit_orders");
            debitOrder.HasKey(d => d.Id);
            debitOrder.Property(d => d.Id).ValueGeneratedNever();
            debitOrder.Property(d => d.MandateType).HasConversion<string>().HasMaxLength(20);
            debitOrder.Property(d => d.MandateReference).HasMaxLength(35);
            debitOrder.Property(d => d.TransactionId).HasMaxLength(35);
            debitOrder.Property(d => d.EndToEndId).HasMaxLength(35);
            debitOrder.Property(d => d.Amount).HasPrecision(18, 2);
            debitOrder.Property(d => d.Currency).HasMaxLength(3).IsFixedLength();
            debitOrder.Property(d => d.SequenceType).HasConversion<string>().HasMaxLength(20);
            debitOrder.Property(d => d.CreditorName).HasMaxLength(140);
            debitOrder.Property(d => d.CreditorShortName).HasMaxLength(20);
            debitOrder.Property(d => d.StatementText).HasMaxLength(140);
            debitOrder.Property(d => d.DebtorAccountMasked).HasMaxLength(34);
            debitOrder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            debitOrder.Property(d => d.StatusReasonCode).HasMaxLength(4);
            debitOrder.Property(d => d.DisputabilityReason).HasConversion<string>().HasMaxLength(40);
            debitOrder.HasOne<Mandate>().WithMany().HasForeignKey(d => d.MandateId).OnDelete(DeleteBehavior.Restrict);
            debitOrder.HasIndex(d => d.TransactionId).IsUnique();
            debitOrder.HasIndex(d => new { d.CustomerId, d.ActionDate }).IsDescending(false, true);
        });
    }
}
