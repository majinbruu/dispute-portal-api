namespace DisputePortal.Modules.Disputes.Domain;

internal enum DisputeActorType
{
    Customer,
    Agent,

    /// <summary>A background worker: the automatic approval, or the scheme's answer.</summary>
    System,
}

/// <summary>
/// Who caused something to happen to a dispute. The subject id is the token subject of the customer or the agent.
/// The system has none.
/// </summary>
internal readonly record struct DisputeActor
{
    private DisputeActor(DisputeActorType type, Guid? subjectId)
    {
        Type = type;
        SubjectId = subjectId;
    }

    public DisputeActorType Type { get; }

    public Guid? SubjectId { get; }

    public static DisputeActor System { get; } = new(DisputeActorType.System, null);

    public static DisputeActor Customer(Guid customerId) => new(DisputeActorType.Customer, customerId);

    public static DisputeActor Agent(Guid agentId) => new(DisputeActorType.Agent, agentId);
}
