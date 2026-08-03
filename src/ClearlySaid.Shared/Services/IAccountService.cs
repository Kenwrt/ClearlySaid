using ClearlySaid.Shared.Models;

namespace ClearlySaid.Shared.Services;

public interface IAccountService
{
    AccountInfo? CurrentAccount { get; }
    bool IsInitialized { get; }
    event EventHandler? AccountChanged;
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task RegisterAsync(string email, string password, CancellationToken cancellationToken = default);
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

public sealed class AccountApiException(string message) : Exception(message);
