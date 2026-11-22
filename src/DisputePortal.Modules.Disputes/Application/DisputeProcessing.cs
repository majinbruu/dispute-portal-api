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

    /// <summary>How long a dispute waits before it is sent to the scheme.</summary>
    public int SendDelaySeconds { get; set; } = 10;

    /// <summary>How long the scheme "takes" to answer.</summary>
    public int OutcomeDelaySeconds { get; set; } = 20;

    public int BatchSize { get; set; } = 20;
}

internal sealed class SchemeSimulatorOptions
{
    public const string Section = "Simulator";

    /// <summary>Returns for these original transaction IDs are rejected, which produces a Declined dispute.</summary>
    public List<string> RejectReturnsForTransactionIds { get; set; } = [];
}

/// <summary>Stands in for the scheme and the creditor bank.</summary>
internal sealed class SimulatedSchemeGateway(IOptions<SchemeSimulatorOptions> options) : ISchemeGateway
{
    public SchemeReturnResult SubmitReturn(Dispute dispute) =>
        options.Value.RejectReturnsForTransactionIds.Contains(dispute.OriginalTransactionId, StringComparer.Ordinal)
            ? SchemeReturnResult.Rejected("The service provider's bank rejected this dispute because the debit order was already reversed. Contact the service provider.")
            : SchemeReturnResult.AcceptedResult;
}

/// <summary>Moves disputes through their lifecycle. Pure logic: the worker supplies locked disputes and saves the result.</summary>
internal sealed class DisputeProcessingService(ISchemeGateway scheme, TimeProvider timeProvider)
{
    public void SendToScheme(Dispute dispute)
    {
        var now = timeProvider.GetUtcNow();
        dispute.MarkSentToScheme(ReturnMessageFactory.Create(dispute, now), now);
    }

    public void ApplySchemeOutcome(Dispute dispute)
    {
        var now = timeProvider.GetUtcNow();
        var result = scheme.SubmitReturn(dispute);

        if (result.Accepted)
        {
            dispute.MarkRefunded(now);
        }
        else
        {
            dispute.MarkDeclined(result.RejectionReason!, now);
        }

        PortalMetrics.DisputesCompleted.Add(1, new KeyValuePair<string, object?>("outcome", dispute.Status.ToString()));
    }
}

/// <summary>
/// Builds a simulated pacs.004 (PaymentReturn) as JSON using ISO 20022 element names.
/// It is stored on the dispute as evidence of what would be sent; no real ISO XML is produced.
/// </summary>
internal static class ReturnMessageFactory
{
    public const string InstructingAgent = "ACME Bank";

    public static string Create(Dispute dispute, DateTimeOffset now)
    {
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
                ["OrgnlTxId"] = dispute.OriginalTransactionId,
                ["RtrdIntrBkSttlmAmt"] = new JsonObject
                {
                    ["Ccy"] = dispute.Currency,
                    ["Amt"] = dispute.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                },
                ["RtrRsnInf"] = new JsonObject
                {
                    ["Orgtr"] = new JsonObject { ["Nm"] = InstructingAgent },
                    ["Rsn"] = new JsonObject { ["Cd"] = dispute.ReasonCode },
                },
                ["OrgnlTxRef"] = new JsonObject
                {
                    ["ReqdColltnDt"] = dispute.ActionDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    ["MndtRltdInf"] = new JsonObject { ["MndtId"] = dispute.MandateReference },
                    ["Cdtr"] = new JsonObject { ["Nm"] = dispute.CreditorName },
                    ["DbtrAcct"] = new JsonObject { ["Id"] = dispute.DebtorAccountMasked },
                },
            },
        };

        return message.ToJsonString();
    }
}

/// <summary>Delivers one outbox message to its consumers and records the result on the message.</summary>
internal sealed class OutboxProcessor(IEnumerable<IDisputeStatusChangedHandler> handlers, TimeProvider timeProvider, ILogger<OutboxProcessor> logger)
{
    public async Task ProcessAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<DisputeStatusChanged>(message.Payload)
                ?? throw new JsonException("The outbox payload was empty.");

            foreach (var handler in handlers)
            {
                await handler.HandleAsync(message.Id, payload, cancellationToken);
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
        }
    }
}
