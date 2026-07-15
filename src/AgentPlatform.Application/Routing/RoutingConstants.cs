namespace AgentPlatform.Application.Routing;

/// <summary>
/// Contains shared constants used across the routing subsystem for cost calculations.
/// </summary>
public static class RoutingConstants
{
    /// <summary>
    /// The default vector store collection name used for RAG context retrieval.
    /// </summary>
    public const string DefaultVectorCollection = "default";

    /// <summary>
    /// The divisor used to convert per-million-token pricing to a per-token cost.
    /// </summary>
    public const int CostPerMillionDivisor = 1_000_000;

    /// <summary>
    /// The default cost per single token used when a provider is not found in the pricing table.
    /// </summary>
    public const decimal DefaultCostPerUnit = 1.00m / CostPerMillionDivisor;
}
