using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClearlySaid.Shared.Models;
using ClearlySaid.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace ClearlySaid.Web.Services;

public sealed class StripeWebBillingService(
    IHttpClientFactory httpClientFactory,
    IAccessTokenStore tokenStore,
    NavigationManager navigation,
    IConfiguration configuration) : IBillingService
{
    public bool IsAvailable => RequiredSettings.All(key =>
        !string.IsNullOrWhiteSpace(configuration[key]));
    public BillingCheckoutMode CheckoutMode =>
        IsAvailable ? BillingCheckoutMode.Stripe : BillingCheckoutMode.Unavailable;
    public string? AvailabilityMessage => IsAvailable
        ? null
        : "Online subscriptions are coming soon. You can keep using the Free plan in the meantime.";

    private static readonly string[] RequiredSettings =
    [
        "Stripe:SecretKey",
        "Stripe:WebhookSecret",
        "Stripe:Prices:StandardMonthly",
        "Stripe:Prices:StandardAnnual",
        "Stripe:Prices:ProMonthly",
        "Stripe:Prices:ProAnnual"
    ];

    public Task StartCheckoutAsync(
        string plan,
        string interval,
        CancellationToken cancellationToken = default) =>
        RedirectAsync(
            "api/billing/stripe/checkout",
            new StripeCheckoutRequest(plan, interval),
            cancellationToken);

    public Task ManageBillingAsync(CancellationToken cancellationToken = default) =>
        RedirectAsync("api/billing/stripe/portal", body: null, cancellationToken);

    private async Task RedirectAsync(
        string uri,
        object? body,
        CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new AccountApiException("Sign in before managing a ClearlySaid subscription.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await httpClientFactory.CreateClient("Public")
            .SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AccountApiException(await ReadProblemAsync(response, cancellationToken));
        }

        var redirect = await response.Content.ReadFromJsonAsync<BillingRedirectResponse>(cancellationToken)
            ?? throw new AccountApiException("ClearlySaid did not receive a billing URL.");
        if (!Uri.TryCreate(redirect.Url, UriKind.Absolute, out var target) ||
            target.Scheme != Uri.UriSchemeHttps ||
            !(target.Host.Equals("stripe.com", StringComparison.OrdinalIgnoreCase) ||
              target.Host.EndsWith(".stripe.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new AccountApiException("ClearlySaid received an invalid billing URL.");
        }

        navigation.NavigateTo(target.ToString(), forceLoad: true);
    }

    private static async Task<string> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var problem = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (problem.RootElement.TryGetProperty("detail", out var detail) &&
                !string.IsNullOrWhiteSpace(detail.GetString()))
            {
                return detail.GetString()!;
            }
        }
        catch (JsonException)
        {
        }

        return "ClearlySaid billing is temporarily unavailable. Please try again.";
    }
}
