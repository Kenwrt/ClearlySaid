namespace ClearlySaid.Shared.Models;

public sealed record RefineMessageRequest(
    string? Message,
    Guid? RequestId = null,
    Guid? UserId = null);
