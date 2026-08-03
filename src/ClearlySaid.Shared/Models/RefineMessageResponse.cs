namespace ClearlySaid.Shared.Models;

public sealed record RefineMessageResponse(
    string Message,
    Guid? RequestId = null,
    string? Provider = null,
    string? Model = null,
    long? LatencyMilliseconds = null,
    bool FallbackUsed = false,
    string? FailureReason = null,
    int? EstimatedInputTokens = null);
