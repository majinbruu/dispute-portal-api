using DisputePortal.Modules.Disputes.Contracts;
using DisputePortal.Modules.Notifications.Application;
using DisputePortal.Modules.Notifications.Domain;
using DisputePortal.SharedKernel;
using Microsoft.Extensions.Time.Testing;

namespace DisputePortal.UnitTests.Notifications;

public class NotificationFactoryTests
{
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Submitted", nameof(NotificationCategory.DisputeReceived))]
    [InlineData("SentToScheme", nameof(NotificationCategory.DisputeInProgress))]
    [InlineData("Refunded", nameof(NotificationCategory.DisputeRefunded))]
    [InlineData("Declined", nameof(NotificationCategory.DisputeDeclined))]
    public void Each_dispute_status_produces_its_own_notification(string status, string expectedCategory)
    {
        var messageId = Guid.NewGuid();

        var notification = NotificationFactory.Create(messageId, Event(status))!;

        notification.Category.ToString().ShouldBe(expectedCategory);
        notification.CustomerId.ShouldBe(Customer);
        notification.SourceMessageId.ShouldBe(messageId);
        notification.Body.ShouldContain("DSP-1");
        notification.CreatedAt.ShouldBe(Now);
        notification.IsRead.ShouldBeFalse();
    }

    [Fact]
    public void A_debit_order_dispute_is_described_as_collected_by_the_creditor()
    {
        NotificationFactory.Create(Guid.NewGuid(), Event("Submitted"))!.Body
            .ShouldBe("Your dispute DSP-1 for R1,250.00 collected by Old Mutual Insure has been received.");
    }

    [Fact]
    public void A_card_dispute_is_described_as_a_purchase_at_the_merchant()
    {
        var message = Event("Submitted") with { TransactionType = DisputedTransactionTypes.Card, CounterpartyName = "Takealot" };

        NotificationFactory.Create(Guid.NewGuid(), message)!.Body
            .ShouldBe("Your dispute DSP-1 for R1,250.00 at Takealot has been received.");
    }

    [Fact]
    public void A_refund_notification_states_the_amount_in_rand()
    {
        NotificationFactory.Create(Guid.NewGuid(), Event("Refunded"))!.Body.ShouldContain("R1,250.00");
    }

    [Fact]
    public void An_amount_in_another_currency_is_shown_with_its_currency_code()
    {
        var message = Event("Refunded") with { Currency = "USD" };

        NotificationFactory.Create(Guid.NewGuid(), message)!.Body.ShouldContain("USD 1,250.00");
    }

    [Fact]
    public void A_declined_notification_includes_the_reason()
    {
        NotificationFactory.Create(Guid.NewGuid(), Event("Declined", "Already reversed."))!.Body.ShouldEndWith("Already reversed.");
    }

    [Fact]
    public void An_unknown_status_produces_no_notification()
    {
        NotificationFactory.Create(Guid.NewGuid(), Event("Archived")).ShouldBeNull();
    }

    internal static DisputeStatusChanged Event(string status, string? note = null) =>
        new(Guid.NewGuid(), Customer, "DSP-1", status, DisputedTransactionTypes.DebitOrder, 1250m, "ZAR", "Old Mutual Insure", note, Now);
}

public class DisputeStatusChangedHandlerTests
{
    private readonly InMemoryNotificationStore _store = new();

    [Fact]
    public async Task Handling_the_same_message_twice_creates_one_notification()
    {
        var handler = new DisputeStatusChangedHandler(_store);
        var messageId = Guid.NewGuid();
        var message = NotificationFactoryTests.Event("Refunded");

        await handler.HandleAsync(messageId, message, CancellationToken.None);
        await handler.HandleAsync(messageId, message, CancellationToken.None);

        _store.Notifications.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_status_without_a_customer_message_is_ignored()
    {
        var handler = new DisputeStatusChangedHandler(_store);

        await handler.HandleAsync(Guid.NewGuid(), NotificationFactoryTests.Event("Archived"), CancellationToken.None);

        _store.Notifications.ShouldBeEmpty();
    }
}

public class NotificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemoryNotificationStore _store = new();
    private readonly FakeTimeProvider _clock = new(Now);

    [Fact]
    public async Task Marking_as_read_is_safe_to_repeat_and_keeps_the_first_timestamp()
    {
        var notification = NotificationFactory.Create(Guid.NewGuid(), NotificationFactoryTests.Event("Refunded"))!;
        _store.Notifications.Add(notification);
        var service = new NotificationService(_store, _clock);

        await service.MarkReadAsync(notification.CustomerId, notification.Id, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = await service.MarkReadAsync(notification.CustomerId, notification.Id, CancellationToken.None);

        second.Value.IsRead.ShouldBeTrue();
        notification.ReadAt.ShouldBe(Now);
        (await service.CountUnreadAsync(notification.CustomerId, CancellationToken.None)).Unread.ShouldBe(0);
    }

    [Fact]
    public async Task Another_customers_notification_cannot_be_read_or_marked()
    {
        var notification = NotificationFactory.Create(Guid.NewGuid(), NotificationFactoryTests.Event("Refunded"))!;
        _store.Notifications.Add(notification);
        var service = new NotificationService(_store, _clock);
        var stranger = Guid.NewGuid();

        var result = await service.MarkReadAsync(stranger, notification.Id, CancellationToken.None);
        var list = await service.ListAsync(stranger, new NotificationListQuery(), CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        list.Items.ShouldBeEmpty();
        notification.IsRead.ShouldBeFalse();
    }

    [Fact]
    public async Task The_inbox_can_be_filtered_to_unread_notifications()
    {
        var read = NotificationFactory.Create(Guid.NewGuid(), NotificationFactoryTests.Event("Submitted"))!;
        var unread = NotificationFactory.Create(Guid.NewGuid(), NotificationFactoryTests.Event("Refunded"))!;
        read.MarkRead(Now);
        _store.Notifications.AddRange([read, unread]);
        var service = new NotificationService(_store, _clock);

        var page = await service.ListAsync(read.CustomerId, new NotificationListQuery { UnreadOnly = true }, CancellationToken.None);

        page.Items.Select(n => n.Id).ShouldBe([unread.Id]);
    }
}

internal sealed class InMemoryNotificationStore : INotificationStore
{
    public List<Notification> Notifications { get; } = [];

    public Task<bool> ExistsForSourceAsync(Guid sourceMessageId, CancellationToken cancellationToken) =>
        Task.FromResult(Notifications.Any(n => n.SourceMessageId == sourceMessageId));

    public Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (Notifications.Any(n => n.SourceMessageId == notification.SourceMessageId))
        {
            return Task.FromResult(false);
        }

        Notifications.Add(notification);
        return Task.FromResult(true);
    }

    public Task<PagedResult<Notification>> ListAsync(Guid customerId, bool unreadOnly, PageRequest page, CancellationToken cancellationToken)
    {
        var items = Notifications.Where(n => n.CustomerId == customerId && (!unreadOnly || !n.IsRead)).ToList();
        return Task.FromResult(new PagedResult<Notification>(items, page.Page, page.PageSize, items.Count));
    }

    public Task<int> CountUnreadAsync(Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(Notifications.Count(n => n.CustomerId == customerId && !n.IsRead));

    public Task<Notification?> FindForUpdateAsync(Guid notificationId, Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(Notifications.FirstOrDefault(n => n.Id == notificationId && n.CustomerId == customerId));

    public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
