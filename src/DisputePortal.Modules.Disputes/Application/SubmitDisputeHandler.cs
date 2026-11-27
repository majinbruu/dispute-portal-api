using System.Security.Cryptography;
using System.Text;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Disputes.Application;

internal sealed record SubmitDisputeCommand(Guid CustomerId, Guid TransactionId, string ReasonId, string IdempotencyKey);

internal sealed record SubmitDisputeOutcome(Dispute Dispute, bool Replayed);

/// <summary>
/// Raises a dispute. Safe to retry: the same idempotency key with the same body returns the original dispute,
/// and a unique index on the transaction is the final guard against a double submission. A withdrawn dispute
/// does not count, so the same transaction can be disputed again.
/// </summary>
internal sealed class SubmitDisputeHandler(ITransactionDirectory transactions, IDisputeRepository disputes, TimeProvider timeProvider)
{
    public async Task<Result<SubmitDisputeOutcome>> HandleAsync(SubmitDisputeCommand command, CancellationToken cancellationToken)
    {
        var requestHash = HashRequest(command);

        var replay = await FindReplayAsync(command, requestHash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        // The lookup is scoped to the customer, so someone else's transaction looks exactly like one that does not exist.
        var found = await transactions.FindAsync(command.TransactionId, command.CustomerId, cancellationToken);
        if (found is null)
        {
            return DisputeErrors.TransactionNotFound;
        }

        var transaction = DisputeMapper.ToDisputedTransaction(found);
        if (DisputeReasons.Resolve(command.ReasonId, transaction) is null)
        {
            return DisputeErrors.ReasonNotAllowed(command.ReasonId);
        }

        if (!found.IsEligible)
        {
            return Error.Unprocessable(found.IneligibilityCode!, found.IneligibilityMessage!);
        }

        if (await disputes.ExistsForTransactionAsync(transaction.TransactionId, cancellationToken))
        {
            return DisputeErrors.AlreadyDisputed;
        }

        var now = timeProvider.GetUtcNow();
        var dispute = Dispute.Submit(Guid.NewGuid(), DisputeReference.New(now), command.CustomerId, transaction, command.ReasonId, comment: null, now);

        var idempotencyRecord = new IdempotencyRecord(command.CustomerId, command.IdempotencyKey, requestHash, dispute.Id, now);
        if (!await disputes.TryAddAsync(dispute, idempotencyRecord, cancellationToken))
        {
            // Another request won the race. It was either the same request retried, or a second dispute for the same transaction.
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
        var canonical = $"{command.TransactionId:N}|{command.ReasonId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

internal static class DisputeErrors
{
    public static readonly Error TransactionNotFound = Error.NotFound("transaction_not_found", "The transaction was not found.");

    public static readonly Error DisputeNotFound = Error.NotFound("dispute_not_found", "The dispute was not found.");

    public static readonly Error AlreadyDisputed = Error.Conflict("already_disputed", "This transaction has already been disputed.");

    public static readonly Error NotWithdrawable = Error.Conflict(
        "dispute_not_withdrawable", "This dispute can no longer be withdrawn because it has already been reviewed.");

    public static readonly Error IdempotencyKeyReused = Error.Conflict(
        "idempotency_key_reused", "This Idempotency-Key was already used for a different request.");

    public static Error ReasonNotAllowed(string reasonId) => Error.Unprocessable(
        "reason_not_allowed", $"Reason '{reasonId}' cannot be used for this transaction.");
}

internal static class DisputeReference
{
    // No 0/O or 1/I, so a reference read over the phone cannot be misheard.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string New(DateTimeOffset now) =>
        $"DSP-{now:yyyyMMdd}-{RandomNumberGenerator.GetString(Alphabet, 6)}";
}
