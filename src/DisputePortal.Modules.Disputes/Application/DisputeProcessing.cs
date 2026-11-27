using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DisputePortal.Modules.Disputes.Contracts;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DisputePortal.Modules.Disputes.Application;

/// <summary>Settings for the simulated back office.</summary>
internal sealed class DisputeProcessingOptions
{
    public const string Section = "Workers";

    /// <summary>Whether this instance runs the background workers. Lets the same image run as an API pod or a worker pod.</summary>
    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// Approves a submitted dispute automatically once it has waited this long, without an agent.
    /// 0 switches this off, which is the default: a dispute waits until an agent approves or rejects it.
    /// </summary>
    public int AutoApproveAfterSeconds { get; set; }

    /// <summary>How long the scheme "takes" to answer.</summary>
    public int OutcomeDelaySeconds { get; set; } = 20;

    public int BatchSize { get; set; } = 20;

    /// <summary>How long an idempotency key is kept. A client retry is only expected within minutes of the first attempt.</summary>
    public int IdempotencyKeyRetentionHours { get; set; } = 24;

    /// <summary>Disputes submitted at or before this moment are approved automatically. Null when automatic approval is off.</summary>
    public DateTimeOffset? AutoApproveCutoff(DateTimeOffset now) =>
        AutoApproveAfterSeconds > 0 ? now.AddSeconds(-AutoApproveAfterSeconds) : null;
}

internal sealed class SchemeSimulatorOptions
{
    public const string Section = "Simulator";

    /// <summary>
    /// Disputes of transactions with these scheme references are declined: a debit order by its pacs.003 TxId,
    /// a card transaction by its acquirer reference number. Every other dispute is refunded.
    /// </summary>
    public List<string> DeclineSchemeReferences { get; set; } = [];
}

/// <summary>Stands in for the payment scheme and the bank on the other side of it.</summary>
internal sealed class SimulatedSchemeGateway(IOptions<SchemeSimulatorOptions> options) : ISchemeGateway
{
    public const string DebitOrderDeclineNote =
        "The service provider's bank rejected this dispute because the debit order was already reversed. Contact the service provider.";

    public const string CardDeclineNote =
        "The merchant provided proof that the charge is valid. Contact the merchant.";

    public SchemeOutcome Submit(Dispute dispute)
    {
        if (!options.Value.DeclineSchemeReferences.Contains(dispute.SchemeReference, StringComparer.Ordinal))
        {
            return SchemeOutcome.AcceptedOutcome;
        }

        return SchemeOutcome.Declined(dispute.TransactionType == DisputedTransactionType.Card ? CardDeclineNote : DebitOrderDeclineNote);
    }
}

/// <summary>Sends approved disputes to the scheme and applies the scheme's answer. Pure logic: the caller loads the dispute and saves the result.</summary>
internal sealed class DisputeProcessingService(ISchemeGateway scheme, TimeProvider timeProvider)
{
    /// <param name="approvedBy">The agent who approved the dispute, or the system when it was approved automatically.</param>
    public void SendToScheme(Dispute dispute, DisputeActor approvedBy)
    {
        var now = timeProvider.GetUtcNow();
        dispute.MarkSentToScheme(SchemePayloadFactory.Create(dispute, now), approvedBy, now);
    }

    public void ApplySchemeOutcome(Dispute dispute)
    {
        var now = timeProvider.GetUtcNow();
        var outcome = scheme.Submit(dispute);

        if (outcome.Accepted)
        {
            dispute.MarkRefunded(now);
        }
        else
        {
            dispute.MarkDeclined(outcome.DeclineNote!, now);
        }

        PortalMetrics.DisputesCompleted.Add(1, new KeyValuePair<string, object?>("outcome", dispute.Status.ToString()));
    }
}

/// <summary>What is sent to the scheme for a dispute: a pacs.004 for a debit order, a chargeback record for a card transaction.</summary>
internal static class SchemePayloadFactory
{
    public static string Create(Dispute dispute, DateTimeOffset now) => dispute.TransactionType == DisputedTransactionType.Card
        ? ChargebackRecordFactory.Create(dispute, now)
        : ReturnMessageFactory.Create(dispute, now);
}

/// <summary>
/// Builds a simulated pacs.004 (PaymentReturn) for a debit order dispute, as JSON using ISO 20022 element names.
/// It is stored on the dispute as evidence of what would be sent; no real ISO XML is produced.
/// </summary>
internal static class ReturnMessageFactory
{
    public const string InstructingAgent = "ACME Bank";

    public static string Create(Dispute dispute, DateTimeOffset now)
    {
        var original = new JsonObject
        {
            ["ReqdColltnDt"] = dispute.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Cdtr"] = new JsonObject { ["Nm"] = dispute.CounterpartyName },
            ["DbtrAcct"] = new JsonObject { ["Id"] = dispute.AccountMasked },
        };

        // An EFT debit order has no mandate to refer to.
        if (dispute.MandateReference is not null)
        {
            original["MndtRltdInf"] = new JsonObject { ["MndtId"] = dispute.MandateReference };
        }

        var message = new JsonObject
        {
            ["MsgDefIdr"] = "pacs.004.001.11",
            ["GrpHdr"] = new JsonObject
            {
                ["MsgId"] = $"RTN-{dispute.Id:N}",
                ["CreDtTm"] = now.ToString("O"),
                ["NbOfTxs"] = "1",
            },
            ["TxInf"] = new JsonObject
            {
                ["RtrId"] = dispute.Reference,
                ["OrgnlEndToEndId"] = dispute.OriginalEndToEndId,
                ["OrgnlTxId"] = dispute.SchemeReference,
                ["RtrdIntrBkSttlmAmt"] = new JsonObject
                {
                    ["Ccy"] = dispute.Currency,
                    ["Amt"] = dispute.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                },
                ["RtrRsnInf"] = new JsonObject
                {
                    ["Orgtr"] = new JsonObject { ["Nm"] = InstructingAgent },
                    ["Rsn"] = new JsonObject { ["Cd"] = dispute.ReasonCode },
                },
                ["OrgnlTxRef"] = original,
            },
        };

        return message.ToJsonString();
    }
}

/// <summary>
/// Builds a simulated chargeback record for a card dispute, as JSON. It takes the place of the pacs.004:
/// card disputes go to the card scheme, which has its own message formats and does not use ISO 20022.
/// </summary>
internal static class ChargebackRecordFactory
{
    public static string Create(Dispute dispute, DateTimeOffset now)
    {
        var record = new JsonObject
        {
            ["recordType"] = "Chargeback",
            ["chargebackReference"] = dispute.Reference,
            ["scheme"] = dispute.CardScheme,
            ["reasonCode"] = dispute.ReasonCode,
            ["acquirerReferenceNumber"] = dispute.SchemeReference,
            ["amount"] = dispute.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            ["currency"] = dispute.Currency,
            ["merchantName"] = dispute.CounterpartyName,
            ["transactionDate"] = dispute.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["postingDate"] = dispute.PostingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["createdAt"] = now.ToString("O"),
        };

        return record.ToJsonString();
    }
}

/// <summary>Turns something that happened to a dispute into the outbox message that tells other modules about it.</summary>
internal static class OutboxMessageFactory
{
    public static OutboxMessage Create(Dispute dispute, IDisputeDomainEvent domainEvent) => domainEvent switch
    {
        DisputeStatusChangedDomainEvent statusChanged => Create(
            nameof(DisputeStatusChanged),
            new DisputeStatusChanged(
                dispute.Id,
                dispute.CustomerId,
                dispute.Reference,
                statusChanged.Status.ToString(),
                dispute.TransactionType.ToString(),
                dispute.Amount,
                dispute.Currency,
                dispute.CounterpartyName,
                statusChanged.Status is DisputeStatus.Refunded or DisputeStatus.Declined ? dispute.OutcomeNote : null,
                statusChanged.OccurredAt),
            statusChanged.OccurredAt),
        AgentCommentAddedDomainEvent commentAdded => Create(
            nameof(AgentCommentAdded),
            new AgentCommentAdded(dispute.Id, dispute.CustomerId, dispute.Reference, commentAdded.CommentId, commentAdded.OccurredAt),
            commentAdded.OccurredAt),
        _ => throw new ArgumentOutOfRangeException(nameof(domainEvent), domainEvent.GetType().Name, "There is no outbox message for this event."),
    };

    private static OutboxMessage Create<T>(string type, T payload, DateTimeOffset occurredAt) =>
        new(Guid.NewGuid(), type, JsonSerializer.Serialize(payload), occurredAt);
}

/// <summary>
/// Delivers one outbox message to the consumers of its type and records the result on the message.
/// The message type decides how the payload is read and which handlers receive it.
/// </summary>
internal sealed class OutboxProcessor(
    IEnumerable<IDisputeStatusChangedHandler> statusChangedHandlers,
    IEnumerable<IAgentCommentAddedHandler> agentCommentAddedHandlers,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger)
{
    public async Task ProcessAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            switch (message.Type)
            {
                case nameof(DisputeStatusChanged):
                    var statusChanged = Read<DisputeStatusChanged>(message);
                    foreach (var handler in statusChangedHandlers)
                    {
                        await handler.HandleAsync(message.Id, statusChanged, cancellationToken);
                    }

                    break;

                case nameof(AgentCommentAdded):
                    var commentAdded = Read<AgentCommentAdded>(message);
                    foreach (var handler in agentCommentAddedHandlers)
                    {
                        await handler.HandleAsync(message.Id, commentAdded, cancellationToken);
                    }

                    break;

                default:
                    // Treated like any other failed delivery: retried, then abandoned and alerted on, never silently dropped.
                    throw new InvalidOperationException($"There is no consumer for outbox messages of type '{message.Type}'.");
            }

            message.MarkProcessed(timeProvider.GetUtcNow());
            PortalMetrics.OutboxMessagesDispatched.Add(1);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A failed delivery must not stop the batch. The message is retried with backoff and abandoned after the limit.
            message.MarkFailed(exception.Message, timeProvider.GetUtcNow());
            PortalMetrics.OutboxMessagesFailed.Add(1);
            logger.LogError(exception, "Outbox message {MessageId} failed on attempt {Attempt}", message.Id, message.Attempts);

            if (message.IsAbandoned)
            {
                // Nothing retries this message again. The counter drives the alert in deploy/monitoring.
                PortalMetrics.OutboxMessagesAbandoned.Add(1);
                logger.LogCritical(
                    "Outbox message {MessageId} of type {MessageType} was abandoned after {Attempts} attempts and needs manual attention",
                    message.Id, message.Type, message.Attempts);
            }
        }
    }

    private static T Read<T>(OutboxMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload) ?? throw new JsonException("The outbox payload was empty.");
}
