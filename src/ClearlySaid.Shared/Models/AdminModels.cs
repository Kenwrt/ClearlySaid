namespace ClearlySaid.Shared.Models;

public sealed record AdminUser(
    Guid Id,
    string Email,
    string Role,
    string Plan,
    int MonthlyAllowance,
    int UsedThisPeriod,
    bool IsDisabled,
    DateTimeOffset CreatedAt);

public sealed record CreateAdminUserRequest(
    string Email,
    string Password,
    string Role,
    string Plan,
    int MonthlyAllowance);

public sealed record UpdateAdminUserRequest(
    string Email,
    string Role,
    string Plan,
    int MonthlyAllowance,
    bool IsDisabled);

public sealed record ResetAdminPasswordRequest(string NewPassword);

public sealed record AdminDiagnosticEvent(
    long Id,
    DateTimeOffset CreatedUtc,
    string Severity,
    string Category,
    string EventName,
    string? UserEmail,
    Guid? RequestId,
    int? InputCharacterCount,
    int? EstimatedInputTokens,
    int? OutputCharacterCount,
    string? Provider,
    string? Model,
    long? LatencyMilliseconds,
    bool Succeeded,
    bool FallbackUsed,
    string? Message);
