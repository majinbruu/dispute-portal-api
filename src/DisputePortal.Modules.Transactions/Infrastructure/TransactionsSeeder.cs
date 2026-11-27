using System.Globalization;
using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Modules.Transactions.Infrastructure;

/// <summary>
/// Stands in for the payment systems: creates demo mandates, feeds simulated pacs.003 / pacs.002 pairs through the
/// same <see cref="DebitOrder.Record"/> path a real collection would take, and records demo card transactions.
/// Dates are relative to today, so the 60-day and 120-day scenarios hold whenever the demo is run.
/// Customers, account numbers and card numbers are synthetic.
/// </summary>
internal sealed class TransactionsSeeder(TransactionsDbContext db, TimeProvider timeProvider)
{
    private const string ThandiAccount = "1234567890";
    private const string SiphoAccount = "9876504321";
    private const string ThandiCard = "4000123456784821";
    private const string SiphoCard = "5100123456787733";

    private int _sequence;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var today = SouthAfricanTime.Today(timeProvider);

        // Each kind of demo data is checked on its own, so a database seeded before a kind existed still gets it.
        if (!await db.Mandates.AnyAsync(cancellationToken))
        {
            SeedThandi(now, today);
            SeedSipho(now, today);
        }

        if (!await db.DebitOrders.AnyAsync(d => d.MandateType == DebitOrderType.Eft, cancellationToken))
        {
            SeedThandiEft(now, today);
        }

        if (!await db.CardTransactions.AnyAsync(cancellationToken))
        {
            SeedThandiCard(now, today);
            SeedSiphoCard(now, today);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private void SeedThandi(DateTimeOffset now, DateOnly today)
    {
        var customer = DemoData.Thandi;

        // Registered mandates: collections are disputable inside the 60-day window.
        var vodacom = AddMandate(Registered(customer, "Vodacom", "VODACOM", "VOD-7741203", 599.00m, collectionDay: 1), now);
        Collect(vodacom, today.AddDays(-5), 599.00m, ThandiAccount, now);
        Collect(vodacom, today.AddDays(-35), 599.00m, ThandiAccount, now, id: DemoData.WithdrawnDebitOrderId);
        Collect(vodacom, today.AddDays(-60), 599.00m, ThandiAccount, now);  // last disputable day
        Collect(vodacom, today.AddDays(-61), 599.00m, ThandiAccount, now);  // window closed
        CollectMonthlyHistory(vodacom, today.AddDays(-95), months: 9, 599.00m, ThandiAccount, now);

        var virginActive = AddMandate(Registered(customer, "Virgin Active South Africa", "VIRGIN ACT", "VA-55610987", 875.00m, collectionDay: 25), now);
        Collect(virginActive, today.AddDays(-12), 875.00m, ThandiAccount, now, id: DemoData.RefundedDebitOrderId);
        Collect(virginActive, today.AddDays(-43), 875.00m, ThandiAccount, now);
        CollectMonthlyHistory(virginActive, today.AddDays(-74), months: 8, 875.00m, ThandiAccount, now);

        var dstv = AddMandate(Registered(customer, "MultiChoice DStv", "DSTV", "DSTV-30911274", 929.00m, collectionDay: 3), now);
        Collect(dstv, today.AddDays(-3), 929.00m, ThandiAccount, now, response: CollectionResponse.Rejected("AM04"));
        Collect(dstv, today.AddDays(-33), 929.00m, ThandiAccount, now, id: DemoData.PendingDebitOrderId);
        CollectMonthlyHistory(dstv, today.AddDays(-64), months: 8, 929.00m, ThandiAccount, now);

        var planetFitness = AddMandate(
            Registered(customer, "Planet Fitness", "PLANET FIT", "PF-20094518", 299.00m, collectionDay: 1) with { Status = MandateStatus.Cancelled }, now);
        Collect(planetFitness, today.AddDays(-8), 299.00m, ThandiAccount, now,
            id: DemoData.DeclinedDebitOrderId, transactionId: DemoData.DeclinedTransactionId);

        // DebiCheck mandates: the customer authenticated these terms with the bank.
        var anchor = DebiCheckAnchor(today.AddDays(-10));

        var oldMutual = AddMandate(DebiCheck(customer, "Old Mutual Insure", "OLD MUTUAL", "OM-POL-4410072", 1250.00m, 1875.00m, anchor.Day, dateAdjustmentAllowed: false, now), now);
        Collect(oldMutual, anchor, 1250.00m, ThandiAccount, now);                 // within terms: not disputable
        Collect(oldMutual, anchor.AddMonths(-1), 1250.00m, ThandiAccount, now);
        Collect(oldMutual, anchor.AddMonths(-2), 1250.00m, ThandiAccount, now);

        var mtn = AddMandate(DebiCheck(customer, "MTN South Africa", "MTN SA", "MTN-C-88120345", 449.00m, 673.50m, anchor.Day, dateAdjustmentAllowed: false, now), now);
        Collect(mtn, anchor, 899.00m, ThandiAccount, now);                         // above the mandate maximum: disputable
        Collect(mtn, anchor.AddMonths(-1).AddDays(3), 449.00m, ThandiAccount, now); // wrong day, no date adjustment: disputable
        Collect(mtn, anchor.AddMonths(-2), 449.00m, ThandiAccount, now);

        var outsurance = AddMandate(DebiCheck(customer, "OUTsurance", "OUTSURANCE", "OUT-9930417", 780.00m, 1170.00m, anchor.Day, dateAdjustmentAllowed: true, now), now);
        Collect(outsurance, anchor.AddDays(2), 780.00m, ThandiAccount, now);       // different day, but date adjustment is allowed
        Collect(outsurance, anchor.AddMonths(-1), 780.00m, ThandiAccount, now);
    }

    private void SeedSipho(DateTimeOffset now, DateOnly today)
    {
        var customer = DemoData.Sipho;

        var telkom = AddMandate(Registered(customer, "Telkom", "TELKOM", "TEL-60017732", 399.00m, collectionDay: 2), now);
        Collect(telkom, today.AddDays(-4), 399.00m, SiphoAccount, now);
        Collect(telkom, today.AddDays(-34), 399.00m, SiphoAccount, now);

        var anchor = DebiCheckAnchor(today.AddDays(-15));
        var discovery = AddMandate(DebiCheck(customer, "Discovery Health", "DISCOVERY", "DH-71002256", 2140.00m, 3210.00m, anchor.Day, dateAdjustmentAllowed: false, now), now);
        Collect(discovery, anchor, 2140.00m, SiphoAccount, now);
        Collect(discovery, anchor.AddMonths(-1), 2140.00m, SiphoAccount, now);
    }

    /// <summary>EFT debit orders have no mandate, so the collection itself names the creditor and the contract.</summary>
    private void SeedThandiEft(DateTimeOffset now, DateOnly today)
    {
        var rates = new CollectingCreditor("City of Johannesburg", "COJ RATES", "COJ-550218734");
        CollectEft(rates, today.AddDays(-6), 1486.20m, now);
        CollectEft(rates, today.AddDays(-37), 1452.75m, now, id: DemoData.AgentRejectedDebitOrderId);
        CollectEft(rates, today.AddDays(-67), 1452.75m, now);  // window closed

        var insurance = new CollectingCreditor("King Price Insurance", "KING PRICE", "KP-88120031");
        CollectEft(insurance, today.AddDays(-2), 912.00m, now, CollectionResponse.Rejected("AM04"));
        CollectEft(insurance, today.AddDays(-32), 912.00m, now);
    }

    private void CollectEft(
        CollectingCreditor creditor, DateOnly actionDate, decimal amount, DateTimeOffset now, CollectionResponse? response = null, Guid? id = null)
    {
        _sequence++;
        var datePart = actionDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        // The EFT prefix keeps these IDs apart from the mandate collections, which may have been seeded on an earlier run.
        var request = new CollectionRequest(
            $"ACMEEFT{datePart}{_sequence:D6}",
            $"{creditor.ContractReference}-{datePart}",
            Money.Zar(amount),
            actionDate,
            SequenceType.Recurring,
            $"{creditor.ShortName} {creditor.ContractReference}",
            ThandiAccount);

        db.DebitOrders.Add(DebitOrder.RecordEft(id ?? Guid.NewGuid(), DemoData.Thandi, creditor, request, response ?? CollectionResponse.AcceptedResponse, now));
    }

    private void SeedThandiCard(DateTimeOffset now, DateOnly today)
    {
        // Bought online without 3-D Secure, so "I did not make this transaction" is offered.
        AddCard(DemoData.Thandi, ThandiAccount, SettledPurchase(ThandiCard, CardScheme.Visa, "Takealot", "Cape Town", "5311", 1899.00m, today.AddDays(-3), CardEntryMode.ECommerce, CardholderVerification.None), now, DemoData.PendingCardId);

        // Chip and PIN, and contactless below the PIN limit.
        AddCard(DemoData.Thandi, ThandiAccount, SettledPurchase(ThandiCard, CardScheme.Visa, "Checkers", "Sandton", "5411", 842.35m, today.AddDays(-6), CardEntryMode.Chip, CardholderVerification.Pin), now);
        AddCard(DemoData.Thandi, ThandiAccount, SettledPurchase(ThandiCard, CardScheme.Visa, "Woolworths", "Rosebank", "5411", 315.90m, today.AddDays(-2), CardEntryMode.Contactless, CardholderVerification.None), now, DemoData.WithdrawnCardId);

        // A subscription, which offers "I cancelled this subscription".
        AddCard(DemoData.Thandi, ThandiAccount, SettledPurchase(ThandiCard, CardScheme.Visa, "Netflix", "Johannesburg", "4899", 199.00m, today.AddDays(-9), CardEntryMode.Recurring, CardholderVerification.None), now, DemoData.RefundedCardId);

        // Not settled: nothing to dispute.
        AddCard(DemoData.Thandi, ThandiAccount, SettledPurchase(ThandiCard, CardScheme.Visa, "Uber", "Johannesburg", "4121", 148.00m, today.AddDays(-1), CardEntryMode.ECommerce, CardholderVerification.ThreeDSecure) with
        {
            Status = CardSettlementStatus.Pending,
            PostingDate = null,
        }, now);
        AddCard(DemoData.Thandi, ThandiAccount, SettledPurchase(ThandiCard, CardScheme.Visa, "Engen", "Midrand", "5541", 950.00m, today.AddDays(-4), CardEntryMode.Chip, CardholderVerification.Pin) with
        {
            Status = CardSettlementStatus.Declined,
            PostingDate = null,
        }, now);

        // Already refunded in full by the merchant.
        AddCard(DemoData.Thandi, ThandiAccount, SettledPurchase(ThandiCard, CardScheme.Visa, "Pick n Pay", "Fourways", "5411", 1204.60m, today.AddDays(-15), CardEntryMode.Chip, CardholderVerification.Pin) with
        {
            RefundedAmount = Money.Zar(1204.60m),
        }, now);

        // Cash withdrawals are not purchases.
        AddCard(DemoData.Thandi, ThandiAccount, SettledPurchase(ThandiCard, CardScheme.Visa, "ACME Bank ATM", "Sandton", "6011", 2000.00m, today.AddDays(-7), CardEntryMode.Chip, CardholderVerification.Pin) with
        {
            Kind = CardTransactionKind.CashWithdrawal,
        }, now);

        // The 120-day window: its last day, and the day after it closed.
        AddCard(DemoData.Thandi, ThandiAccount, SettledPurchase(ThandiCard, CardScheme.Visa, "Showmax", "Randburg", "4899", 99.00m, today.AddDays(-120), CardEntryMode.Recurring, CardholderVerification.None), now);
        AddCard(DemoData.Thandi, ThandiAccount, SettledPurchase(ThandiCard, CardScheme.Visa, "Showmax", "Randburg", "4899", 99.00m, today.AddDays(-121), CardEntryMode.Recurring, CardholderVerification.None), now);

        // The simulated scheme declines a dispute of this purchase.
        AddCard(DemoData.Thandi, ThandiAccount, SettledPurchase(ThandiCard, CardScheme.Visa, "Takealot", "Cape Town", "5311", 2499.00m, today.AddDays(-11), CardEntryMode.ECommerce, CardholderVerification.ThreeDSecure) with
        {
            AcquirerReferenceNumber = DemoData.DeclinedCardReference,
        }, now, DemoData.DeclinedCardId);
    }

    private void SeedSiphoCard(DateTimeOffset now, DateOnly today)
    {
        AddCard(DemoData.Sipho, SiphoAccount, SettledPurchase(SiphoCard, CardScheme.Mastercard, "Checkers", "Durban", "5411", 562.10m, today.AddDays(-5), CardEntryMode.Chip, CardholderVerification.Pin), now);
        AddCard(DemoData.Sipho, SiphoAccount, SettledPurchase(SiphoCard, CardScheme.Mastercard, "Takealot", "Cape Town", "5311", 749.00m, today.AddDays(-8), CardEntryMode.ECommerce, CardholderVerification.None), now, DemoData.SiphoPendingCardId);
        AddCard(DemoData.Sipho, SiphoAccount, SettledPurchase(SiphoCard, CardScheme.Mastercard, "Showmax", "Randburg", "4899", 99.00m, today.AddDays(-12), CardEntryMode.Recurring, CardholderVerification.None), now);
    }

    /// <summary>A settled purchase in South Africa, posted the day after the card was used. Callers change what differs.</summary>
    private CardTransactionDetails SettledPurchase(
        string cardNumber, CardScheme scheme, string merchant, string city, string categoryCode, decimal amount, DateOnly postingDate,
        CardEntryMode entryMode, CardholderVerification verification)
    {
        _sequence++;
        return new CardTransactionDetails(
            cardNumber,
            scheme,
            CardTransactionKind.Purchase,
            Money.Zar(amount),
            merchant,
            city,
            "ZA",
            categoryCode,
            postingDate.AddDays(-1),
            postingDate,
            (_sequence * 7919 % 1_000_000).ToString("D6", CultureInfo.InvariantCulture),
            $"7{postingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}{_sequence:D14}",
            entryMode,
            verification,
            CardSettlementStatus.Settled,
            Money.Zar(0m));
    }

    /// <param name="id">Fixed for the transactions the demo disputes are raised against.</param>
    private void AddCard(Guid customer, string account, CardTransactionDetails details, DateTimeOffset now, Guid? id = null) =>
        db.CardTransactions.Add(CardTransaction.Record(id ?? Guid.NewGuid(), customer, account, details, now));

    /// <summary>DebiCheck collection days are capped at the 28th so that going back a month always lands on the same day.</summary>
    private static DateOnly DebiCheckAnchor(DateOnly date) => date.Day > 28 ? new DateOnly(date.Year, date.Month, 28) : date;

    private static MandateDetails Registered(Guid customer, string creditor, string shortName, string contract, decimal instalment, int collectionDay) => new(
        Guid.NewGuid(), customer, MandateReference(contract), contract, MandateType.Registered, creditor, shortName,
        Money.Zar(instalment), Money.Zar(instalment * 1.5m), CollectionFrequency.Monthly, collectionDay,
        new DateOnly(2024, 2, collectionDay), DateAdjustmentAllowed: true, TrackingEnabled: true);

    private static MandateDetails DebiCheck(Guid customer, string creditor, string shortName, string contract, decimal instalment, decimal maximum, int collectionDay, bool dateAdjustmentAllowed, DateTimeOffset now) => new(
        Guid.NewGuid(), customer, MandateReference(contract), contract, MandateType.DebiCheck, creditor, shortName,
        Money.Zar(instalment), Money.Zar(maximum), CollectionFrequency.Monthly, collectionDay,
        new DateOnly(2025, 3, collectionDay), dateAdjustmentAllowed, TrackingEnabled: true,
        AuthenticationType: "Real-time (banking app)", AuthenticatedAt: now.AddMonths(-20));

    private static string MandateReference(string contract) => $"MRN{Math.Abs(StableHash(contract)) % 1_000_000_000:D9}";

    private static int StableHash(string value) => value.Aggregate(17, (hash, c) => unchecked((hash * 31) + c));

    private Mandate AddMandate(MandateDetails details, DateTimeOffset now)
    {
        var mandate = Mandate.Create(details, now);
        db.Mandates.Add(mandate);
        return mandate;
    }

    private void CollectMonthlyHistory(Mandate mandate, DateOnly latest, int months, decimal amount, string account, DateTimeOffset now)
    {
        for (var i = 0; i < months; i++)
        {
            Collect(mandate, latest.AddDays(-30 * i), amount, account, now);
        }
    }

    private void Collect(
        Mandate mandate,
        DateOnly actionDate,
        decimal amount,
        string account,
        DateTimeOffset now,
        CollectionResponse? response = null,
        Guid? id = null,
        string? transactionId = null)
    {
        _sequence++;
        var datePart = actionDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var request = new CollectionRequest(
            transactionId ?? $"ACME{datePart}{_sequence:D6}",
            $"{mandate.ContractReference}-{datePart}",
            Money.Zar(amount),
            actionDate,
            SequenceType.Recurring,
            $"{mandate.CreditorShortName} {mandate.ContractReference}",
            account);

        db.DebitOrders.Add(DebitOrder.Record(id ?? Guid.NewGuid(), mandate, request, response ?? CollectionResponse.AcceptedResponse, now));
    }
}
