using DisputePortal.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Modules.Transactions.Infrastructure;

internal sealed class TransactionsDbContext(DbContextOptions<TransactionsDbContext> options) : DbContext(options)
{
    public const string Schema = "transactions";

    public DbSet<Mandate> Mandates => Set<Mandate>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<DebitOrder> DebitOrders => Set<DebitOrder>();

    public DbSet<CardTransaction> CardTransactions => Set<CardTransaction>();

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

        // Table per type: the shared columns live in one table and each type keeps its details in its own,
        // joined on the transaction ID.
        modelBuilder.Entity<Transaction>(transaction =>
        {
            transaction.UseTptMappingStrategy();
            transaction.ToTable("transactions");

            // The snake_case naming convention does not rename primary keys in a table-per-type hierarchy,
            // so these tables keep EF Core's default key names (PK_transactions, PK_debit_orders, PK_card_transactions).
            transaction.HasKey(t => t.Id);
            transaction.Property(t => t.Id).ValueGeneratedNever();
            transaction.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
            transaction.Property(t => t.AccountMasked).HasMaxLength(34);
            transaction.Property(t => t.CounterpartyName).HasMaxLength(140);
            transaction.Property(t => t.Amount).HasPrecision(18, 2);
            transaction.Property(t => t.Currency).HasMaxLength(3).IsFixedLength();
            transaction.Property(t => t.DisputabilityReason).HasConversion<string>().HasMaxLength(40);
            transaction.HasIndex(t => new { t.CustomerId, t.Type, t.EffectiveDate }).IsDescending(false, false, true);
        });

        modelBuilder.Entity<DebitOrder>(debitOrder =>
        {
            debitOrder.ToTable("debit_orders");
            debitOrder.Property(d => d.MandateType).HasConversion<string>().HasMaxLength(20);
            debitOrder.Property(d => d.MandateReference).HasMaxLength(35);
            debitOrder.Property(d => d.TransactionId).HasMaxLength(35);
            debitOrder.Property(d => d.EndToEndId).HasMaxLength(35);
            debitOrder.Property(d => d.SequenceType).HasConversion<string>().HasMaxLength(20);
            debitOrder.Property(d => d.CreditorShortName).HasMaxLength(20);
            debitOrder.Property(d => d.StatementText).HasMaxLength(140);
            debitOrder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            debitOrder.Property(d => d.StatusReasonCode).HasMaxLength(4);
            debitOrder.HasOne<Mandate>().WithMany().HasForeignKey(d => d.MandateId).OnDelete(DeleteBehavior.Restrict);
            debitOrder.HasIndex(d => d.TransactionId).IsUnique();
        });

        modelBuilder.Entity<CardTransaction>(card =>
        {
            card.ToTable("card_transactions");
            card.Property(c => c.CardNumberMasked).HasMaxLength(19);
            card.Property(c => c.Scheme).HasConversion<string>().HasMaxLength(20);
            card.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
            card.Property(c => c.MerchantCity).HasMaxLength(40);
            card.Property(c => c.MerchantCountry).HasMaxLength(2).IsFixedLength();
            card.Property(c => c.MerchantCategoryCode).HasMaxLength(4).IsFixedLength();
            card.Property(c => c.AuthorisationCode).HasMaxLength(6);
            card.Property(c => c.AcquirerReferenceNumber).HasMaxLength(23);
            card.Property(c => c.EntryMode).HasConversion<string>().HasMaxLength(20);
            card.Property(c => c.Verification).HasConversion<string>().HasMaxLength(20);
            card.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            card.Property(c => c.RefundedAmount).HasPrecision(18, 2);
            card.Ignore(c => c.OutstandingAmount);
            card.HasIndex(c => c.AcquirerReferenceNumber).IsUnique();
        });
    }
}
