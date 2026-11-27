using DisputePortal.Modules.Disputes.Application;
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
            dispute.Property(d => d.TransactionType).HasConversion<string>().HasMaxLength(20);
            dispute.Property(d => d.SchemeReference).HasMaxLength(35);
            dispute.Property(d => d.Amount).HasPrecision(18, 2);
            dispute.Property(d => d.Currency).HasMaxLength(3).IsFixedLength();
            dispute.Property(d => d.CounterpartyName).HasMaxLength(140);
            dispute.Property(d => d.AccountMasked).HasMaxLength(34);
            dispute.Property(d => d.OriginalEndToEndId).HasMaxLength(35);
            dispute.Property(d => d.MandateType).HasMaxLength(20);
            dispute.Property(d => d.MandateReference).HasMaxLength(35);
            dispute.Property(d => d.CardScheme).HasMaxLength(20);
            dispute.Property(d => d.ReasonId).HasMaxLength(40);
            dispute.Property(d => d.ReasonCode).HasMaxLength(4);
            dispute.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            dispute.Property(d => d.OutcomeNote).HasMaxLength(500);
            dispute.Property(d => d.SchemePayload).HasColumnType("jsonb");

            // PostgreSQL's xmin system column changes on every update, which gives optimistic concurrency without an extra column.
            dispute.Property(d => d.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            dispute.Ignore(d => d.DomainEvents);
            dispute.Ignore(d => d.IsAwaitingReview);
            dispute.Ignore(d => d.IsOpen);
            dispute.Ignore(d => d.IsCompleted);

            dispute.HasMany(d => d.Events).WithOne().HasForeignKey(e => e.DisputeId).OnDelete(DeleteBehavior.Cascade);
            dispute.HasMany(d => d.Comments).WithOne().HasForeignKey(c => c.DisputeId).OnDelete(DeleteBehavior.Cascade);

            dispute.HasIndex(d => d.Reference).IsUnique();

            // One dispute per transaction, which is the final guard against a double submission. A withdrawn
            // dispute is left out of the index, so the customer can dispute the same transaction again.
            dispute.HasIndex(d => d.TransactionId).IsUnique().HasFilter("status <> 'Withdrawn'");
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
            disputeEvent.Property(e => e.ActorType).HasConversion<string>().HasMaxLength(20);
            disputeEvent.HasIndex(e => new { e.DisputeId, e.OccurredAt });
        });

        modelBuilder.Entity<DisputeComment>(comment =>
        {
            comment.ToTable("dispute_comments");
            comment.HasKey(c => c.Id);

            // Assigned in code, for the same reason as the event keys.
            comment.Property(c => c.Id).ValueGeneratedNever();
            comment.Property(c => c.AuthorType).HasConversion<string>().HasMaxLength(20);
            comment.Property(c => c.Body).HasMaxLength(DisputeComment.MaxLength);
            comment.HasIndex(c => new { c.DisputeId, c.CreatedAt });
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
            record.HasIndex(r => r.CreatedAt);
        });
    }

    /// <summary>Turns the status changes and agent comments raised by tracked disputes into outbox rows, inside the same save.</summary>
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
                OutboxMessages.Add(OutboxMessageFactory.Create(dispute, domainEvent));
            }

            dispute.ClearDomainEvents();
        }
    }
}
