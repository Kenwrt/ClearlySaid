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
