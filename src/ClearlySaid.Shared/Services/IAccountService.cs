using ClearlySaid.Shared.Models;

namespace ClearlySaid.Shared.Services;

public interface IAccountService
{
    AccountInfo? CurrentAccount { get; }
    bool IsInitialized { get; }
    event EventHandler? AccountChanged;
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<string> RegisterAsync(string email, string password, CancellationToken cancellationToken = default);
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(string token, string password, CancellationToken cancellationToken = default);
    Task AcceptInvitationAsync(string token, string password, CancellationToken cancellationToken = default);
    Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default);
    Task ResendVerificationAsync(string email, CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    Task DeleteAccountAsync(CancellationToken cancellationToken = default);
}

public interface IAccessTokenStore
{
    Task<string?> GetAsync();
    Task SetAsync(string token);
    Task RemoveAsync();
}

public interface IBillingService
{
    bool IsAvailable { get; }
    BillingCheckoutMode CheckoutMode { get; }
    string? AvailabilityMessage { get; }
    Task StartCheckoutAsync(string plan, string interval, CancellationToken cancellationToken = default);
    Task ManageBillingAsync(CancellationToken cancellationToken = default);
    Task<CancelSubscriptionResponse> CancelSubscriptionAsync(CancellationToken cancellationToken = default);
}

public enum BillingCheckoutMode
{
    Unavailable,
    Stripe,
    ExternalWebsite,
    AppStore
}

public sealed class UnavailableBillingService : IBillingService
{
    public bool IsAvailable => false;
    public BillingCheckoutMode CheckoutMode => BillingCheckoutMode.Unavailable;
    public string AvailabilityMessage => "Subscriptions are not available in this app build yet.";

    public Task StartCheckoutAsync(
        string plan,
        string interval,
        CancellationToken cancellationToken = default) =>
        throw new AccountApiException("Purchases for this app are managed by your device's app store.");

    public Task ManageBillingAsync(CancellationToken cancellationToken = default) =>
        throw new AccountApiException("Billing management is not available on this device yet.");

    public Task<CancelSubscriptionResponse> CancelSubscriptionAsync(CancellationToken cancellationToken = default) =>
        throw new AccountApiException("Subscription cancellation is managed by your device's app store.");
}

public interface IAdminService
{
    Task<IReadOnlyList<AdminUser>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<AdminUser> CreateUserAsync(CreateAdminUserRequest request, CancellationToken cancellationToken = default);
    Task<AdminUser> UpdateUserAsync(Guid userId, UpdateAdminUserRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default);
    Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminDiagnosticEvent>> GetDiagnosticsAsync(int limit = 250, CancellationToken cancellationToken = default);
}

public sealed class AccountApiException(string message) : Exception(message);
