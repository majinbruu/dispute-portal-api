namespace DisputePortal.SharedKernel;

/// <summary>
/// The signed-in bank agent. Like the customer, the identifier always comes from the validated access token.
/// It is recorded on every decision and comment the agent makes.
/// </summary>
public interface ICurrentAgent
{
    Guid AgentId { get; }
}
