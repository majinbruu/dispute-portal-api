using DisputePortal.Modules.Notifications.Application;
using DisputePortal.Modules.Notifications.Domain;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DisputePortal.Modules.Notifications.Infrastructure;

internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string Schema = "notifications";

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Notification>(notification =>
        {
            notification.ToTable("notifications");
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Id).ValueGeneratedNever();
            notification.Property(n => n.Category).HasConversion<string>().HasMaxLength(40);
            notification.Property(n => n.Title).HasMaxLength(140);
            notification.Property(n => n.Body).HasMaxLength(1000);
            notification.Ignore(n => n.IsRead);
            notification.HasIndex(n => n.SourceMessageId).IsUnique();
            notification.HasIndex(n => new { n.CustomerId, n.CreatedAt }).IsDescending(false, true);
        });
    }
}

internal sealed class EfNotificationStore(NotificationsDbContext db) : INotificationStore
{
    public Task<bool> ExistsForSourceAsync(Guid sourceMessageId, CancellationToken cancellationToken) =>
        db.Notifications.AnyAsync(n => n.SourceMessageId == sourceMessageId, cancellationToken);

    public async Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken)
    {
        db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<PagedResult<Notification>> ListAsync(Guid customerId, bool unreadOnly, PageRequest page, CancellationToken cancellationToken)
    {
        var query = db.Notifications.AsNoTracking().Where(n => n.CustomerId == customerId);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Notification>(items, page.Page, page.PageSize, total);
    }

    public Task<int> CountUnreadAsync(Guid customerId, CancellationToken cancellationToken) =>
        db.Notifications.CountAsync(n => n.CustomerId == customerId && n.ReadAt == null, cancellationToken);

    public Task<Notification?> FindForUpdateAsync(Guid notificationId, Guid customerId, CancellationToken cancellationToken) =>
        db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.CustomerId == customerId, cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
