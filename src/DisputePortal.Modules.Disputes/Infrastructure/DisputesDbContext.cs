using System.Text.Json;
using DisputePortal.Modules.Disputes.Contracts;
using DisputePortal.Modules.Disputes.Domain;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Modules.Disputes.Infrastructure;

internal sealed class DisputesDbContext(DbContextOptions<DisputesDbContext> options) : DbContext(options)
{
    public const string Schema = "disputes";

    public DbSet<Dispute> Disputes => Set<Dispute>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        WriteDomainEventsToOutbox();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        WriteDomainEventsToOutbox();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Dispute>(dispute =>
        {
            dispute.ToTable("disputes");
            dispute.HasKey(d => d.Id);
            dispute.Property(d => d.Id).ValueGeneratedNever();
            dispute.Property(d => d.Reference).HasMaxLength(30);
            dispute.Property(d => d.OriginalTransactionId).HasMaxLength(35);
            dispute.Property(d => d.OriginalEndToEndId).HasMaxLength(35);
            dispute.Property(d => d.Amount).HasPrecision(18, 2);
            dispute.Property(d => d.Currency).HasMaxLength(3).IsFixedLength();
            dispute.Property(d => d.MandateType).HasMaxLength(20);
            dispute.Property(d => d.MandateReference).HasMaxLength(35);
            dispute.Property(d => d.CreditorName).HasMaxLength(140);
            dispute.Property(d => d.DebtorAccountMasked).HasMaxLength(34);
            dispute.Property(d => d.ReasonCode).HasMaxLength(4);
            dispute.Property(d => d.Comment).HasMaxLength(500);
            dispute.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            dispute.Property(d => d.OutcomeNote).HasMaxLength(500);
            dispute.Property(d => d.ReturnPayload).HasColumnType("jsonb");

            // PostgreSQL's xmin system column changes on every update, which gives optimistic concurrency without an extra column.
            dispute.Property(d => d.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            dispute.Ignore(d => d.DomainEvents);
            dispute.Ignore(d => d.IsCompleted);

            dispute.HasMany(d => d.Events).WithOne().HasForeignKey(e => e.DisputeId).OnDelete(DeleteBehavior.Cascade);

            dispute.HasIndex(d => d.Reference).IsUnique();

            // One dispute per debit order. This is the final guard against a double submission.
            dispute.HasIndex(d => d.DebitOrderId).IsUnique();
            dispute.HasIndex(d => new { d.CustomerId, d.SubmittedAt }).IsDescending(false, true);
            dispute.HasIndex(d => new { d.Status, d.SubmittedAt });
        });

        modelBuilder.Entity<DisputeEvent>(disputeEvent =>
        {
            disputeEvent.ToTable("dispute_events");
            disputeEvent.HasKey(e => e.Id);

            // Keys are assigned in code. Without this, EF would treat a new event on a loaded dispute as an existing row.
            disputeEvent.Property(e => e.Id).ValueGeneratedNever();
            disputeEvent.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            disputeEvent.Property(e => e.Description).HasMaxLength(500);
            disputeEvent.HasIndex(e => new { e.DisputeId, e.OccurredAt });
        });

        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.ToTable("outbox_messages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).ValueGeneratedNever();
            message.Property(m => m.Type).HasMaxLength(100);
            message.Property(m => m.Payload).HasColumnType("jsonb");
            message.Property(m => m.LastError).HasMaxLength(2000);
            message.Ignore(m => m.IsAbandoned);
            message.HasIndex(m => new { m.NextAttemptAt, m.OccurredAt }).HasFilter("processed_at IS NULL");
        });

        modelBuilder.Entity<IdempotencyRecord>(record =>
        {
            record.ToTable("idempotency_keys");
            record.HasKey(r => new { r.CustomerId, r.Key });
            record.Property(r => r.Key).HasMaxLength(100);
            record.Property(r => r.RequestHash).HasMaxLength(64).IsFixedLength();
        });
    }

    /// <summary>Turns the status changes raised by tracked disputes into outbox rows, inside the same save.</summary>
    private void WriteDomainEventsToOutbox()
    {
        var disputes = ChangeTracker.Entries<Dispute>()
            .Select(entry => entry.Entity)
            .Where(dispute => dispute.DomainEvents.Count > 0)
            .ToList();

        foreach (var dispute in disputes)
        {
            foreach (var domainEvent in dispute.DomainEvents)
            {
                var isOutcome = domainEvent.Status is DisputeStatus.Refunded or DisputeStatus.Declined;
                var message = new DisputeStatusChanged(
                    dispute.Id,
                    dispute.CustomerId,
                    dispute.Reference,
                    domainEvent.Status.ToString(),
                    dispute.Amount,
                    dispute.Currency,
                    dispute.CreditorName,
                    isOutcome ? dispute.OutcomeNote : null,
                    domainEvent.OccurredAt);

                OutboxMessages.Add(new OutboxMessage(
                    Guid.NewGuid(), nameof(DisputeStatusChanged), JsonSerializer.Serialize(message), domainEvent.OccurredAt));
            }

            dispute.ClearDomainEvents();
        }
    }
}
