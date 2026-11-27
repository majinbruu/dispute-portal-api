using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DisputePortal.Modules.Disputes.Infrastructure;
using DisputePortal.Modules.Notifications.Infrastructure;
using DisputePortal.Modules.Transactions.Infrastructure;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;

namespace DisputePortal.IntegrationTests.Infrastructure;

/// <summary>
/// A database as version 1 of the portal left it: the schema of the last migrations before version 2, holding
/// rows written the way version 1 wrote them. Version 1 knew only debit orders, kept them in a schema called
/// debit_orders, and identified a dispute reason by its ISO code.
/// </summary>
internal static class VersionOneDatabase
{
    // The last migration of each module before version 2. Notifications has not changed since.
    private const string LastTransactionsMigration = "20261123184049_RenameCollectionStatuses";
    private const string LastDisputesMigration = "20261126094416_IndexIdempotencyKeyCreatedAt";
    private const string OldTransactionsSchema = "debit_orders";

    public static readonly Guid Customer = DemoData.Thandi;

    public static readonly Guid RegisteredMandate = Id(0x0101);
    public static readonly Guid DebiCheckMandate = Id(0x0102);

    public static readonly Guid AcceptedRegistered = Id(0x0201);
    public static readonly Guid RejectedRegistered = Id(0x0202);
    public static readonly Guid DebiCheckWithinTerms = Id(0x0203);
    public static readonly Guid DebiCheckAboveMaximum = Id(0x0204);
    public static readonly Guid RejectedDebiCheckWithinTerms = Id(0x0205);

    /// <summary>One dispute per version 1 reason code, in the statuses version 1 had.</summary>
    public static readonly IReadOnlyList<OldDispute> Disputes =
    [
        new(Id(0x0301), "DSP-20261126-AAAAAA", AcceptedRegistered, "ACME-V1-0001", 599.00m, "MD01", "Submitted"),
        new(Id(0x0302), "DSP-20261126-BBBBBB", DebiCheckAboveMaximum, "ACME-V1-0004", 899.00m, "AM09", "Refunded"),
        new(Id(0x0303), "DSP-20261126-CCCCCC", Id(0x0206), "ACME-V1-0006", 599.00m, "DT01", "SentToScheme"),
        new(Id(0x0304), "DSP-20261126-DDDDDD", Id(0x0207), "ACME-V1-0007", 599.00m, "MD06", "Declined"),
        new(Id(0x0305), "DSP-20261126-EEEEEE", Id(0x0208), "ACME-V1-0008", 599.00m, "AM05", "Refunded"),
    ];

    public static readonly Guid UndeliveredSubmittedMessage = Id(0x0401);
    public static readonly Guid DeliveredRefundedMessage = Id(0x0402);
    public static readonly Guid FailedOnceDeclinedMessage = Id(0x0403);

    public static readonly Guid ExistingNotification = Id(0x0501);

    public const string IdempotencyKey = "version-one-key";
    public const string ReturnPayload = """{"MsgDefIdr": "pacs.004.001.11", "TxInf": {"RtrId": "DSP-20261126-BBBBBB"}}""";
    public const string DeclineNote = "The service provider's bank rejected this dispute.";

    public static readonly DateTimeOffset CreatedAt = TestClock.Start.AddDays(-30);
    public static readonly DateTimeOffset SubmittedAt = TestClock.Start.AddDays(-1);

    public static OldDispute SubmittedDispute => Disputes[0];

    public static OldDispute RefundedDispute => Disputes[1];

    public static OldDispute SentDispute => Disputes[2];

    public static OldDispute DeclinedDispute => Disputes[3];

    /// <summary>Applies each module's migrations up to the last one of version 1, exactly as version 1 would have.</summary>
    public static async Task MigrateAsync(string connectionString)
    {
        await MigrateToAsync<TransactionsDbContext>(connectionString, OldTransactionsSchema, LastTransactionsMigration, options => new(options));
        await MigrateToAsync<DisputesDbContext>(connectionString, DisputesDbContext.Schema, LastDisputesMigration, options => new(options));
        await MigrateToAsync<NotificationsDbContext>(connectionString, NotificationsDbContext.Schema, targetMigration: null, options => new(options));
    }

    public static async Task InsertRowsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await InsertMandateAsync(connection, RegisteredMandate, "Registered", "Vodacom", "VODACOM", "VOD-7741203", "MRN000000001", 599.00m, 898.50m);
        await InsertMandateAsync(connection, DebiCheckMandate, "DebiCheck", "MTN South Africa", "MTN SA", "MTN-C-88120345", "MRN000000002", 449.00m, 673.50m);

        // Version 1 stored the mandate decision even on a rejected collection, and refused the dispute when it was read.
        await InsertDebitOrderAsync(connection, AcceptedRegistered, RegisteredMandate, "ACME-V1-0001", 599.00m, daysAgo: 7, "Accepted", true, "RegisteredMandate");
        await InsertDebitOrderAsync(connection, RejectedRegistered, RegisteredMandate, "ACME-V1-0002", 599.00m, daysAgo: 6, "Rejected", true, "RegisteredMandate");
        await InsertDebitOrderAsync(connection, DebiCheckWithinTerms, DebiCheckMandate, "ACME-V1-0003", 449.00m, daysAgo: 12, "Accepted", false, "WithinMandateTerms");
        await InsertDebitOrderAsync(connection, DebiCheckAboveMaximum, DebiCheckMandate, "ACME-V1-0004", 899.00m, daysAgo: 42, "Accepted", true, "AmountAboveMandateMaximum");
        await InsertDebitOrderAsync(connection, RejectedDebiCheckWithinTerms, DebiCheckMandate, "ACME-V1-0005", 449.00m, daysAgo: 5, "Rejected", false, "WithinMandateTerms");
        foreach (var dispute in Disputes.Skip(2))
        {
            await InsertDebitOrderAsync(connection, dispute.DebitOrderId, RegisteredMandate, dispute.TransactionId, dispute.Amount, daysAgo: 20, "Accepted", true, "RegisteredMandate");
        }

        foreach (var dispute in Disputes)
        {
            await InsertDisputeAsync(connection, dispute);
        }

        await InsertOutboxMessageAsync(connection, UndeliveredSubmittedMessage, SubmittedDispute, outcomeNote: null, attempts: 0, processed: false);
        await InsertOutboxMessageAsync(connection, DeliveredRefundedMessage, RefundedDispute, "Your account has been credited.", attempts: 1, processed: true);
        await InsertOutboxMessageAsync(connection, FailedOnceDeclinedMessage, DeclinedDispute, DeclineNote, attempts: 1, processed: false);

        await InsertAsync(connection, "notifications.notifications", new()
        {
            ["id"] = ExistingNotification,
            ["customer_id"] = Customer,
            ["source_message_id"] = DeliveredRefundedMessage,
            ["category"] = "DisputeRefunded",
            ["title"] = "Your dispute was successful",
            ["body"] = $"Dispute {RefundedDispute.Reference} is successful. Your account has been credited with R899.00.",
            ["related_dispute_id"] = RefundedDispute.Id,
            ["created_at"] = SubmittedAt,
            ["read_at"] = null,
        });

        // Version 1 hashed the debit order ID and the reason code of the request.
        await InsertAsync(connection, "disputes.idempotency_keys", new()
        {
            ["customer_id"] = Customer,
            ["key"] = IdempotencyKey,
            ["request_hash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{SubmittedDispute.DebitOrderId:N}|{SubmittedDispute.ReasonCode}"))),
            ["dispute_id"] = SubmittedDispute.Id,
            ["created_at"] = SubmittedAt,
        });
    }

    private static async Task MigrateToAsync<TContext>(
        string connectionString, string historySchema, string? targetMigration, Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", historySchema))
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var context = create(options);
        await context.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private static Task InsertMandateAsync(
        NpgsqlConnection connection, Guid id, string type, string creditor, string shortName, string contract, string reference, decimal instalment, decimal maximum) =>
        InsertAsync(connection, "debit_orders.mandates", new()
        {
            ["id"] = id,
            ["customer_id"] = Customer,
            ["mandate_reference"] = reference,
            ["contract_reference"] = contract,
            ["type"] = type,
            ["creditor_name"] = creditor,
            ["creditor_short_name"] = shortName,
            ["instalment_amount"] = instalment,
            ["maximum_collection_amount"] = maximum,
            ["currency"] = "ZAR",
            ["frequency"] = "Monthly",
            ["collection_day"] = 15,
            ["first_collection_date"] = new DateOnly(2025, 3, 15),
            ["date_adjustment_allowed"] = false,
            ["tracking_enabled"] = true,
            ["authentication_type"] = type == "DebiCheck" ? "Real-time (banking app)" : null,
            ["authenticated_at"] = type == "DebiCheck" ? CreatedAt.AddYears(-1) : null,
            ["status"] = "Active",
            ["created_at"] = CreatedAt,
        });

    private static Task InsertDebitOrderAsync(
        NpgsqlConnection connection, Guid id, Guid mandateId, string transactionId, decimal amount, int daysAgo, string status, bool disputableUnderMandate, string reason)
    {
        var registered = mandateId == RegisteredMandate;
        var actionDate = ActionDate(daysAgo);

        return InsertAsync(connection, "debit_orders.debit_orders", new()
        {
            ["id"] = id,
            ["customer_id"] = Customer,
            ["mandate_id"] = mandateId,
            ["mandate_type"] = registered ? "Registered" : "DebiCheck",
            ["mandate_reference"] = registered ? "MRN000000001" : "MRN000000002",
            ["transaction_id"] = transactionId,
            ["end_to_end_id"] = $"E2E-{transactionId}",
            ["amount"] = amount,
            ["currency"] = "ZAR",
            ["action_date"] = actionDate,
            ["sequence_type"] = "Recurring",
            ["creditor_name"] = registered ? "Vodacom" : "MTN South Africa",
            ["creditor_short_name"] = registered ? "VODACOM" : "MTN SA",
            ["statement_text"] = registered ? "VODACOM VOD-7741203" : "MTN SA MTN-C-88120345",
            ["debtor_account_masked"] = "******7890",
            ["status"] = status,
            ["status_reason_code"] = status == "Rejected" ? "AM04" : null,
            ["is_disputable_under_mandate"] = disputableUnderMandate,
            ["disputability_reason"] = reason,
            ["last_dispute_date"] = actionDate.AddDays(60),
            ["created_at"] = CreatedAt,
        });
    }

    private static async Task InsertDisputeAsync(NpgsqlConnection connection, OldDispute dispute)
    {
        var sent = dispute.Status != "Submitted";
        var completed = dispute.Status is "Refunded" or "Declined";
        var debiCheck = dispute.DebitOrderId == DebiCheckAboveMaximum;

        await InsertAsync(connection, "disputes.disputes", new()
        {
            ["id"] = dispute.Id,
            ["reference"] = dispute.Reference,
            ["customer_id"] = Customer,
            ["debit_order_id"] = dispute.DebitOrderId,
            ["original_transaction_id"] = dispute.TransactionId,
            ["original_end_to_end_id"] = $"E2E-{dispute.TransactionId}",
            ["amount"] = dispute.Amount,
            ["currency"] = "ZAR",
            ["action_date"] = ActionDate(daysAgo: 20),
            ["mandate_type"] = debiCheck ? "DebiCheck" : "Registered",
            ["mandate_reference"] = debiCheck ? "MRN000000002" : "MRN000000001",
            ["creditor_name"] = debiCheck ? "MTN South Africa" : "Vodacom",
            ["debtor_account_masked"] = "******7890",
            ["reason_code"] = dispute.ReasonCode,
            ["status"] = dispute.Status,
            ["submitted_at"] = SubmittedAt,
            ["sent_to_scheme_at"] = sent ? SubmittedAt.AddSeconds(15) : null,
            ["completed_at"] = completed ? SubmittedAt.AddSeconds(35) : null,
            ["outcome_note"] = dispute.Status switch
            {
                "Refunded" => "Your account has been credited.",
                "Declined" => DeclineNote,
                _ => null,
            },
            ["return_payload"] = sent ? new Jsonb(ReturnPayload) : null,
        });

        await InsertEventAsync(connection, dispute, "Submitted", "We received your dispute.", SubmittedAt);
        if (sent)
        {
            await InsertEventAsync(connection, dispute, "SentToScheme", "Your dispute was sent for processing.", SubmittedAt.AddSeconds(15));
        }

        if (completed)
        {
            var outcome = dispute.Status == "Refunded" ? "Your account has been credited." : DeclineNote;
            await InsertEventAsync(connection, dispute, dispute.Status, outcome, SubmittedAt.AddSeconds(35));
        }
    }

    private static Task InsertEventAsync(NpgsqlConnection connection, OldDispute dispute, string status, string description, DateTimeOffset occurredAt) =>
        InsertAsync(connection, "disputes.dispute_events", new()
        {
            ["id"] = Guid.NewGuid(),
            ["dispute_id"] = dispute.Id,
            ["status"] = status,
            ["description"] = description,
            ["occurred_at"] = occurredAt,
        });

    /// <summary>The payload is the version 1 contract: it names the creditor and has no transaction type.</summary>
    private static Task InsertOutboxMessageAsync(
        NpgsqlConnection connection, Guid id, OldDispute dispute, string? outcomeNote, int attempts, bool processed) =>
        InsertAsync(connection, "disputes.outbox_messages", new()
        {
            ["id"] = id,
            ["type"] = "DisputeStatusChanged",
            ["payload"] = new Jsonb(JsonSerializer.Serialize(new
            {
                DisputeId = dispute.Id,
                CustomerId = Customer,
                dispute.Reference,
                dispute.Status,
                dispute.Amount,
                Currency = "ZAR",
                CreditorName = dispute.DebitOrderId == DebiCheckAboveMaximum ? "MTN South Africa" : "Vodacom",
                OutcomeNote = outcomeNote,
                OccurredAt = SubmittedAt,
            })),
            ["occurred_at"] = SubmittedAt,
            ["processed_at"] = processed ? SubmittedAt.AddSeconds(2) : null,
            ["attempts"] = attempts,
            ["next_attempt_at"] = SubmittedAt.AddSeconds(10),
            ["last_error"] = attempts > 0 && !processed ? "The notifications database was not reachable." : null,
        });

    private static async Task InsertAsync(NpgsqlConnection connection, string table, Dictionary<string, object?> row)
    {
        var columns = string.Join(", ", row.Keys);
        var placeholders = string.Join(", ", row.Keys.Select((_, index) => $"${index + 1}"));

        await using var command = new NpgsqlCommand($"INSERT INTO {table} ({columns}) VALUES ({placeholders})", connection);
        foreach (var value in row.Values)
        {
            command.Parameters.Add(value is Jsonb json
                ? new NpgsqlParameter { Value = json.Text, NpgsqlDbType = NpgsqlDbType.Jsonb }
                : new NpgsqlParameter { Value = value ?? DBNull.Value });
        }

        await command.ExecuteNonQueryAsync();
    }

    public static DateOnly ActionDate(int daysAgo) => DateOnly.FromDateTime(TestClock.Start.UtcDateTime).AddDays(-daysAgo);

    private static Guid Id(int number) => Guid.Parse($"0000{number:x4}-0000-4000-8000-000000000001");

    internal sealed record OldDispute(Guid Id, string Reference, Guid DebitOrderId, string TransactionId, decimal Amount, string ReasonCode, string Status);

    private sealed record Jsonb(string Text);
}
