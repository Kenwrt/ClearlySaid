namespace ClearlySaid.Shared.Models;

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record SecurityNoticeAcknowledgementRequest(bool DoNotDisplayAgain);

public sealed record EmailRequest(string Email);

public sealed record TokenRequest(string Token);

public sealed record PasswordResetRequest(string Token, string Password);

public sealed record AcceptInvitationRequest(string Token, string Password);

public sealed record RegistrationResponse(string Message);

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, AccountInfo Account);

public sealed record AccountInfo(
    Guid Id,
    string Email,
    string Plan,
    int MonthlyAllowance,
    int UsedThisPeriod,
    DateTimeOffset PeriodEndsAt,
    string Role = "User",
    string? SubscriptionProvider = null,
    bool SecurityNoticeDismissed = false)
{
    public bool IsUnlimited => Role == AccountRoles.Admin;
    public int Remaining => Math.Max(0, MonthlyAllowance - UsedThisPeriod);
}

public static class AccountRoles
{
    public const string User = "User";
    public const string Admin = "Admin";
}

public sealed record SubscriptionPlan(
    string Id,
    string DisplayName,
    int MonthlyAllowance,
    bool IsPurchasable,
    bool IsInternal,
    string Description,
    decimal MonthlyPrice,
    decimal AnnualPrice,
    string? GooglePlayProductId);

public static class SubscriptionPlans
{
    public const string Free = "free";
    public const string Development = "development";
    public const string Standard = "standard";
    public const string Pro = "pro";

    public static readonly SubscriptionPlan FreePlan = new(
        Free, "Free", 20, false, false, "Try ClearlySaid with a small monthly allowance.", 0m, 0m, null);
    public static readonly SubscriptionPlan DevelopmentPlan = new(
        Development, "Development", 10_000, false, true, "Internal testing and development access.", 0m, 0m, null);
    public static readonly SubscriptionPlan StandardPlan = new(
        Standard, "Standard", 300, true, false, "A practical baseline subscription for regular use.", 2.49m, 24.99m,
        "clearlysaid_standard");
    public static readonly SubscriptionPlan ProPlan = new(
        Pro, "Pro", 1_000, true, false, "Higher-volume access for frequent use.", 4.99m, 49.99m,
        "clearlysaid_pro");

    public static IReadOnlyList<SubscriptionPlan> All { get; } =
        [FreePlan, DevelopmentPlan, StandardPlan, ProPlan];

    public static SubscriptionPlan? Find(string? id) =>
        All.FirstOrDefault(plan => string.Equals(plan.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static SubscriptionPlan GetRequired(string? id) =>
        Find(id) ?? throw new ArgumentException("Select a valid subscription plan.", nameof(id));
}

public sealed record GooglePurchaseVerificationRequest(
    string ProductId,
    string PurchaseToken,
    string PackageName);

public sealed record GooglePurchaseVerificationResponse(
    AccountInfo Account,
    bool ShouldAcknowledge);

public static class BillingIntervals
{
    public const string Monthly = "monthly";
    public const string Annual = "annual";
}

public sealed record StripeCheckoutRequest(string Plan, string Interval);

public sealed record BillingRedirectResponse(string Url);

public sealed record CancelSubscriptionResponse(string Message, DateTimeOffset? AccessEndsAt);
