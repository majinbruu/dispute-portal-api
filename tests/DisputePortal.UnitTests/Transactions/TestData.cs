using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.UnitTests.Transactions;

internal static class TestData
{
    public static readonly Guid Customer = Guid.Parse("00000000-0000-4000-8000-0000000000aa");
    public static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly Today = new(2026, 11, 22);

    public static Mandate RegisteredMandate(Guid? customerId = null) => Mandate.Create(
        new MandateDetails(
            Guid.NewGuid(), customerId ?? Customer, "MRN000000001", "VOD-1", MandateType.Registered, "Vodacom", "VODACOM",
            Money.Zar(599m), Money.Zar(898.50m), CollectionFrequency.Monthly, CollectionDay: 1,
            new DateOnly(2024, 2, 1), DateAdjustmentAllowed: true, TrackingEnabled: true),
        Now);

    public static Mandate DebiCheckMandate(
        decimal instalment = 449m,
        decimal maximum = 673.50m,
        int collectionDay = 15,
        bool dateAdjustmentAllowed = false,
        CollectionFrequency frequency = CollectionFrequency.Monthly,
        DateOnly? firstCollectionDate = null) => Mandate.Create(
        new MandateDetails(
            Guid.NewGuid(), Customer, "MRN000000002", "MTN-1", MandateType.DebiCheck, "MTN South Africa", "MTN SA",
            Money.Zar(instalment), Money.Zar(maximum), frequency, collectionDay,
            firstCollectionDate ?? new DateOnly(2025, 3, collectionDay), dateAdjustmentAllowed, TrackingEnabled: true,
            AuthenticationType: "Real-time (banking app)", AuthenticatedAt: Now.AddYears(-1)),
        Now);

    public static CollectionRequest Request(DateOnly actionDate, decimal amount = 599m, string transactionId = "ACME20261101000001") => new(
        transactionId, "E2E-1", Money.Zar(amount), actionDate, SequenceType.Recurring, "VODACOM VOD-1", "1234567890");

    public static DebitOrder DebitOrder(Mandate mandate, DateOnly actionDate, decimal amount = 599m, CollectionResponse? response = null) =>
        Modules.Transactions.Domain.DebitOrder.Record(Guid.NewGuid(), mandate, Request(actionDate, amount), response ?? CollectionResponse.AcceptedResponse, Now);
}
