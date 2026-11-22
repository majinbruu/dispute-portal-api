using System.Globalization;
using DisputePortal.Modules.DebitOrders.Domain;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Modules.DebitOrders.Infrastructure;

/// <summary>
/// Stands in for the scheme: creates demo mandates and feeds simulated pacs.003 / pacs.002 pairs through the same
/// <see cref="DebitOrder.Record"/> path a real collection would take. Dates are relative to today, so the
/// 60-day scenarios hold whenever the demo is run. Customers and account numbers are synthetic.
/// </summary>
internal sealed class DebitOrdersSeeder(DebitOrdersDbContext db, TimeProvider timeProvider)
{
    private const string ThandiAccount = "1234567890";
    private const string SiphoAccount = "9876504321";

    private int _sequence;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Mandates.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var today = SouthAfricanTime.Today(timeProvider);

        SeedThandi(now, today);
        SeedSipho(now, today);

        await db.SaveChangesAsync(cancellationToken);
    }

    private void SeedThandi(DateTimeOffset now, DateOnly today)
    {
        var customer = DemoData.Thandi;

        // Registered mandates: collections are disputable inside the 60-day window.
        var vodacom = AddMandate(Registered(customer, "Vodacom", "VODACOM", "VOD-7741203", 599.00m, collectionDay: 1), now);
        Collect(vodacom, today.AddDays(-5), 599.00m, ThandiAccount, now);
        Collect(vodacom, today.AddDays(-35), 599.00m, ThandiAccount, now);
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
