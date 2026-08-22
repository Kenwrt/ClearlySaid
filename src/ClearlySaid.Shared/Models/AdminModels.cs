namespace ClearlySaid.Shared.Models;

public sealed record AdminUser(
    Guid Id,
    string Email,
    string Role,
    string Plan,
    int MonthlyAllowance,
    int UsedThisPeriod,
    string SubscriptionStatus,
    string? SubscriptionProvider,
    DateTimeOffset PeriodEndsAt,
    bool IsDisabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record AdminUserActivity(
    Guid UserId,
    string Email,
    string Role,
    DateTimeOffset? LastLoginAt,
    int ProcessedMessages,
    DateTimeOffset? LastProcessedAt);

public sealed record CreateAdminUserRequest(
    string Email,
    string Role,
    string Plan);

public sealed record UpdateAdminUserRequest(
    string Email,
    string Role,
    string Plan,
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
