namespace ClearlySaid.Shared.Models;

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record SecurityNoticeAcknowledgementRequest(bool DoNotDisplayAgain);

public sealed record UpdatePhoneProfileRequest(
    string? PhoneNumber,
    bool TransactionalConsent,
    bool MarketingConsent,
    bool ConfirmsAuthority);

public sealed record VerifyPhoneRequest(string Code);

public sealed record SendPhoneVerificationResponse(DateTimeOffset ExpiresAt);

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
    bool SecurityNoticeDismissed = false,
    string? PhoneNumber = null,
    bool PhoneVerified = false,
    string SmsConsentStatus = SmsConsentStatuses.NotProvided,
    DateTimeOffset? SmsConsentedAt = null,
    DateTimeOffset? SmsTransactionalConsentAt = null,
    DateTimeOffset? SmsMarketingConsentAt = null,
    DateTimeOffset? FreeTrialEndsAt = null)
{
    public bool IsUnlimited => Role == AccountRoles.Admin;
    public bool IsTrialExpired => !IsUnlimited && Plan == SubscriptionPlans.Free && FreeTrialEndsAt <= DateTimeOffset.UtcNow;
    // Compatibility field for older clients; refinement counts no longer control access.
    public int Remaining => IsTrialExpired ? 0 : int.MaxValue;
}

public static class SmsConsentStatuses
{
    public const string NotProvided = "NotProvided";
    public const string PendingVerification = "PendingVerification";
    public const string OptedIn = "OptedIn";
    public const string OptedOut = "OptedOut";
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
        Free, "Free", int.MaxValue, false, false, "Try ClearlySaid with a limited free trial.", 0m, 0m, null);
    public static readonly SubscriptionPlan DevelopmentPlan = new(
        Development, "Development", int.MaxValue, false, true, "Internal testing and development access.", 0m, 0m, null);
    public static readonly SubscriptionPlan StandardPlan = new(
        Standard, "Standard", int.MaxValue, true, false, "Standard subscription access with no refinement limit.", 1.99m, 24.99m,
        "clearlysaid_standard");
    public static readonly SubscriptionPlan ProPlan = new(
        Pro, "Pro", int.MaxValue, true, false, "Pro subscription access with no refinement limit.", 2.49m, 49.99m,
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
