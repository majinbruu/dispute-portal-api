using System.Security.Cryptography;
using System.Text;
using DisputePortal.Modules.DebitOrders.Contracts;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Disputes.Application;

internal sealed record SubmitDisputeCommand(Guid CustomerId, Guid DebitOrderId, string ReasonCode, string IdempotencyKey);

internal sealed record SubmitDisputeOutcome(Dispute Dispute, bool Replayed);

/// <summary>
/// Raises a dispute. Safe to retry: the same idempotency key with the same body returns the original dispute,
/// and a unique index on the debit order is the final guard against a double submission.
/// </summary>
internal sealed class SubmitDisputeHandler(IDebitOrderDirectory debitOrders, IDisputeRepository disputes, TimeProvider timeProvider)
{
    public async Task<Result<SubmitDisputeOutcome>> HandleAsync(SubmitDisputeCommand command, CancellationToken cancellationToken)
    {
        var requestHash = HashRequest(command);

        var replay = await FindReplayAsync(command, requestHash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        // The lookup is scoped to the customer, so someone else's debit order looks exactly like one that does not exist.
        var debitOrder = await debitOrders.FindAsync(command.DebitOrderId, command.CustomerId, cancellationToken);
        if (debitOrder is null)
        {
            return DisputeErrors.DebitOrderNotFound;
        }

        var reason = DisputeReasons.Find(command.ReasonCode);
        if (reason is null || !reason.IsAllowedFor(debitOrder.MandateType))
        {
            return DisputeErrors.ReasonNotAllowed(command.ReasonCode, debitOrder.MandateType);
        }

        if (!debitOrder.IsEligible)
        {
            return Error.Unprocessable(debitOrder.IneligibilityCode!, debitOrder.IneligibilityMessage!);
        }

        if (await disputes.ExistsForDebitOrderAsync(debitOrder.Id, cancellationToken))
        {
            return DisputeErrors.AlreadyDisputed;
        }

        var now = timeProvider.GetUtcNow();
        var dispute = Dispute.Submit(
            Guid.NewGuid(),
            DisputeReference.New(now),
            command.CustomerId,
            new DisputedDebitOrder(
                debitOrder.Id,
                debitOrder.TransactionId,
                debitOrder.EndToEndId,
                new Money(debitOrder.Amount, debitOrder.Currency),
                debitOrder.ActionDate,
                debitOrder.MandateType,
                debitOrder.MandateReference,
                debitOrder.CreditorName,
                debitOrder.DebtorAccountMasked),
            reason,
            now);

        var idempotencyRecord = new IdempotencyRecord(command.CustomerId, command.IdempotencyKey, requestHash, dispute.Id, now);
        if (!await disputes.TryAddAsync(dispute, idempotencyRecord, cancellationToken))
        {
            // Another request won the race. It was either the same request retried, or a second dispute for the same debit order.
            return await FindReplayAsync(command, requestHash, cancellationToken) ?? DisputeErrors.AlreadyDisputed;
        }

        PortalMetrics.DisputesSubmitted.Add(1);
        return Result<SubmitDisputeOutcome>.Success(new SubmitDisputeOutcome(dispute, Replayed: false));
    }

    private async Task<Result<SubmitDisputeOutcome>?> FindReplayAsync(SubmitDisputeCommand command, string requestHash, CancellationToken cancellationToken)
    {
        var record = await disputes.FindIdempotencyRecordAsync(command.CustomerId, command.IdempotencyKey, cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return DisputeErrors.IdempotencyKeyReused;
        }

        var original = await disputes.FindAsync(record.DisputeId, command.CustomerId, cancellationToken);
        return original is null
            ? DisputeErrors.IdempotencyKeyReused
            : Result<SubmitDisputeOutcome>.Success(new SubmitDisputeOutcome(original, Replayed: true));
    }

    private static string HashRequest(SubmitDisputeCommand command)
    {
        var canonical = $"{command.DebitOrderId:N}|{command.ReasonCode}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

internal static class DisputeErrors
{
    public static readonly Error DebitOrderNotFound = Error.NotFound("debit_order_not_found", "The debit order was not found.");

    public static readonly Error DisputeNotFound = Error.NotFound("dispute_not_found", "The dispute was not found.");

    public static readonly Error AlreadyDisputed = Error.Conflict("already_disputed", "This debit order has already been disputed.");

    public static readonly Error IdempotencyKeyReused = Error.Conflict(
        "idempotency_key_reused", "This Idempotency-Key was already used for a different request.");

    public static Error ReasonNotAllowed(string reasonCode, string mandateType) => Error.Unprocessable(
        "reason_not_allowed", $"Reason '{reasonCode}' cannot be used for a {mandateType} debit order.");
}

internal static class DisputeReference
{
    // No 0/O or 1/I, so a reference read over the phone cannot be misheard.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string New(DateTimeOffset now) =>
        $"DSP-{now:yyyyMMdd}-{RandomNumberGenerator.GetString(Alphabet, 6)}";
}
