using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClearlySaid.Shared.Models;

namespace ClearlySaid.Shared.Services;

public sealed class ClearlySaidApiClient(HttpClient httpClient, IAccessTokenStore tokenStore) :
    IAccountService,
    IMessageRefinementService,
    IAdminService
{
    public AccountInfo? CurrentAccount { get; private set; }
    public bool IsInitialized { get; private set; }
    public event EventHandler? AccountChanged;
    public event EventHandler? LoginSucceeded;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsInitialized)
        {
            return;
        }

        IsInitialized = true;
        await RefreshAsync(cancellationToken);
    }

    public async Task LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "api/account/login", new LoginRequest(email, password), cancellationToken);
        await CompleteAuthenticationAsync(
            response,
            cancellationToken,
            "You've reached the maximum number of sign-in attempts. Please wait five minutes before trying again.");
        LoginSucceeded?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "api/account/register", new RegisterRequest(email, password), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<RegistrationResponse>(cancellationToken))?.Message
            ?? "Check your email to activate your ClearlySaid account.";
    }

    public Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default) =>
        PostAccountActionAsync("api/account/password/forgot", new EmailRequest(email), cancellationToken);

    public Task ResetPasswordAsync(string token, string password, CancellationToken cancellationToken = default) =>
        PostAccountActionAsync("api/account/password/reset", new PasswordResetRequest(token, password), cancellationToken);

    public Task AcceptInvitationAsync(string token, string password, CancellationToken cancellationToken = default) =>
        PostAccountActionAsync("api/account/invitation/accept", new AcceptInvitationRequest(token, password), cancellationToken);

    public Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default) =>
        PostAccountActionAsync("api/account/email/verify", new TokenRequest(token), cancellationToken);

    public Task ResendVerificationAsync(string email, CancellationToken cancellationToken = default) =>
        PostAccountActionAsync("api/account/email/resend", new EmailRequest(email), cancellationToken);

    private async Task PostAccountActionAsync(string uri, object body, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(uri, body, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var token = await tokenStore.GetAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            SetAccount(null);
            return;
        }

        using var request = CreateAuthorizedRequest(HttpMethod.Get, "api/account/me", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await tokenStore.RemoveAsync();
            SetAccount(null);
            return;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        SetAccount(await response.Content.ReadFromJsonAsync<AccountInfo>(cancellationToken));
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var token = await tokenStore.GetAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/account/logout", token);
            using var response = await httpClient.SendAsync(request, cancellationToken);
        }

        await tokenStore.RemoveAsync();
        SetAccount(null);
    }

    public async Task AcknowledgeSecurityNoticeAsync(
        bool doNotDisplayAgain,
        CancellationToken cancellationToken = default)
    {
        var token = await tokenStore.GetAsync();
        if (string.IsNullOrWhiteSpace(token) || CurrentAccount is null)
        {
            return;
        }

        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/account/security-notice/acknowledge", token);
        request.Content = JsonContent.Create(new SecurityNoticeAcknowledgementRequest(doNotDisplayAgain));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        if (doNotDisplayAgain)
        {
            SetAccount(CurrentAccount with { SecurityNoticeDismissed = true });
        }
    }

    public async Task UpdatePhoneProfileAsync(
        UpdatePhoneProfileRequest profile,
        CancellationToken cancellationToken = default)
    {
        var token = await tokenStore.GetAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new AccountApiException("Sign in to update your profile.");
        }

        using var request = CreateAuthorizedRequest(HttpMethod.Put, "api/account/profile/phone", token);
        request.Content = JsonContent.Create(profile);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        SetAccount(await response.Content.ReadFromJsonAsync<AccountInfo>(cancellationToken)
            ?? throw new AccountApiException("The account service returned an empty response."));
    }

    public async Task DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        var token = await tokenStore.GetAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        using var request = CreateAuthorizedRequest(HttpMethod.Delete, "api/account", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await tokenStore.RemoveAsync();
        SetAccount(null);
    }

    public async Task<GooglePurchaseVerificationResponse> VerifyGooglePurchaseAsync(
        GooglePurchaseVerificationRequest purchase,
        CancellationToken cancellationToken = default)
    {
        var token = await tokenStore.GetAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new AccountApiException("Sign in before purchasing a ClearlySaid subscription.");
        }

        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/billing/google/verify", token);
        request.Content = JsonContent.Create(purchase);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var verification = await response.Content.ReadFromJsonAsync<GooglePurchaseVerificationResponse>(cancellationToken)
            ?? throw new AccountApiException("Google Play returned an empty verification response.");
        SetAccount(verification.Account);
        return verification;
    }

    public async Task<string> RefineAsync(
        string message,
        MessageStyleOptions? style = null,
        CancellationToken cancellationToken = default)
    {
        var token = await tokenStore.GetAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new AccountApiException("Sign in to improve your message.");
        }

        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/messages/refine", token);
        request.Content = JsonContent.Create(new RefineMessageRequest(message, Guid.NewGuid(), Style: style));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<RefineMessageResponse>(cancellationToken);
        await RefreshAsync(cancellationToken);
        return result?.Message ?? throw new AccountApiException("The message service returned an empty response.");
    }

    public async Task<IReadOnlyList<AdminUser>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        await SendAdminAsync<List<AdminUser>>(HttpMethod.Get, "api/admin/users", null, cancellationToken);

    public Task<AdminUser> CreateUserAsync(
        CreateAdminUserRequest request,
        CancellationToken cancellationToken = default) =>
        SendAdminAsync<AdminUser>(HttpMethod.Post, "api/admin/users", request, cancellationToken);

    public Task<AdminUser> UpdateUserAsync(
        Guid userId,
        UpdateAdminUserRequest request,
        CancellationToken cancellationToken = default) =>
        SendAdminAsync<AdminUser>(HttpMethod.Put, $"api/admin/users/{userId}", request, cancellationToken);

    public async Task ResetPasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken = default) =>
        await SendAdminAsync<object>(
            HttpMethod.Post,
            $"api/admin/users/{userId}/reset-password",
            new ResetAdminPasswordRequest(newPassword),
            cancellationToken,
            allowEmptyResponse: true);

    public async Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await SendAdminAsync<object>(
            HttpMethod.Delete,
            $"api/admin/users/{userId}",
            null,
            cancellationToken,
            allowEmptyResponse: true);

    public async Task<IReadOnlyList<AdminUserActivity>> GetUserActivityAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default) =>
        await SendAdminAsync<List<AdminUserActivity>>(
            HttpMethod.Get,
            $"api/admin/activity?fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}",
            null,
            cancellationToken);

    public async Task<IReadOnlyList<AdminDiagnosticEvent>> GetDiagnosticsAsync(
        int limit = 250,
        CancellationToken cancellationToken = default) =>
        await SendAdminAsync<List<AdminDiagnosticEvent>>(
            HttpMethod.Get,
            $"api/admin/diagnostics?limit={Math.Clamp(limit, 1, 500)}",
            null,
            cancellationToken);

    private async Task<T> SendAdminAsync<T>(
        HttpMethod method,
        string uri,
        object? body,
        CancellationToken cancellationToken,
        bool allowEmptyResponse = false)
    {
        var token = await tokenStore.GetAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new AccountApiException("Sign in with an administrator account.");
        }

        using var request = CreateAuthorizedRequest(method, uri, token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        if (allowEmptyResponse)
        {
            return default!;
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new AccountApiException("The admin service returned an empty response.");
    }

    private async Task CompleteAuthenticationAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        string? tooManyRequestsMessage = null)
    {
        using (response)
        {
            await EnsureSuccessAsync(response, cancellationToken, tooManyRequestsMessage);
            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken)
                ?? throw new AccountApiException("The account service returned an empty response.");
            await tokenStore.SetAsync(auth.AccessToken);
            SetAccount(auth.Account);
        }
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string uri,
        string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        string? tooManyRequestsMessage = null)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? detail = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var problem = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            detail = problem.RootElement.TryGetProperty("detail", out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
        }

        throw new AccountApiException(detail ?? response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
            HttpStatusCode.Forbidden => "Administrator access is required.",
            HttpStatusCode.TooManyRequests =>
                tooManyRequestsMessage ?? "You have reached your current usage limit.",
            HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout =>
                "ClearlySaid took longer than expected. Please submit your message again.",
            _ => "ClearlySaid is temporarily unavailable. Please try again."
        });
    }

    private void SetAccount(AccountInfo? account)
    {
        CurrentAccount = account;
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }
}
