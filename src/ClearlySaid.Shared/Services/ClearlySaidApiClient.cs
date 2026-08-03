using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClearlySaid.Shared.Models;

namespace ClearlySaid.Shared.Services;

public sealed class ClearlySaidApiClient(HttpClient httpClient, IAccessTokenStore tokenStore) :
    IAccountService,
    IMessageRefinementService
{
    public AccountInfo? CurrentAccount { get; private set; }
    public bool IsInitialized { get; private set; }
    public event EventHandler? AccountChanged;

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
        await CompleteAuthenticationAsync(response, cancellationToken);
    }

    public async Task RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "api/account/register", new RegisterRequest(email, password), cancellationToken);
        await CompleteAuthenticationAsync(response, cancellationToken);
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

    public async Task<string> RefineAsync(string message, CancellationToken cancellationToken = default)
    {
        var token = await tokenStore.GetAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new AccountApiException("Sign in to improve your message.");
        }

        using var request = CreateAuthorizedRequest(HttpMethod.Post, "api/messages/refine", token);
        request.Content = JsonContent.Create(new RefineMessageRequest(message, Guid.NewGuid()));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<RefineMessageResponse>(cancellationToken);
        await RefreshAsync(cancellationToken);
        return result?.Message ?? throw new AccountApiException("The message service returned an empty response.");
    }

    private async Task CompleteAuthenticationAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            await EnsureSuccessAsync(response, cancellationToken);
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
        CancellationToken cancellationToken)
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
            HttpStatusCode.TooManyRequests => "You have reached your current usage limit.",
            _ => "ClearlySaid is temporarily unavailable. Please try again."
        });
    }

    private void SetAccount(AccountInfo? account)
    {
        CurrentAccount = account;
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }
}
