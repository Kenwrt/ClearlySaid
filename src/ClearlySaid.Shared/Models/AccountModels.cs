namespace ClearlySaid.Shared.Models;

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, AccountInfo Account);

public sealed record AccountInfo(
    Guid Id,
    string Email,
    string Plan,
    int MonthlyAllowance,
    int UsedThisPeriod,
    DateTimeOffset PeriodEndsAt)
{
    public int Remaining => Math.Max(0, MonthlyAllowance - UsedThisPeriod);
}

public sealed record GooglePurchaseVerificationRequest(
    string ProductId,
    string PurchaseToken,
    string PackageName);
