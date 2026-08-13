using ClearlySaid.Shared.Services;
using ClearlySaid.Shared.Models;

namespace ClearlySaid.App.Services;

public sealed class MauiWebsiteBillingService : IBillingService
{
    public bool IsAvailable => AppSettings.ExternalPurchaseLinksEnabled;

    public BillingCheckoutMode CheckoutMode => IsAvailable
        ? BillingCheckoutMode.ExternalWebsite
        : BillingCheckoutMode.Unavailable;

    public string? AvailabilityMessage => IsAvailable
        ? "You will finish the secure subscription on the ClearlySaid website. Sign in there with the same account you use in this app."
        : "Subscriptions are coming soon. The Free plan includes 20 message improvements each month.";

    public async Task StartCheckoutAsync(
        string plan,
        string interval,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new AccountApiException(AvailabilityMessage!);
        }

        var target = new Uri(
            $"{AppSettings.SubscriptionWebsiteUrl}&plan={Uri.EscapeDataString(plan)}&interval={Uri.EscapeDataString(interval)}#plans");
        if (!await Browser.Default.OpenAsync(target, BrowserLaunchMode.SystemPreferred))
        {
            throw new AccountApiException("ClearlySaid could not open the subscription website.");
        }
    }

    public async Task ManageBillingAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new AccountApiException(AvailabilityMessage!);
        }

        if (!await Browser.Default.OpenAsync(
                new Uri($"{AppSettings.SubscriptionWebsiteUrl}#plans"),
                BrowserLaunchMode.SystemPreferred))
        {
            throw new AccountApiException("ClearlySaid could not open the billing website.");
        }
    }

    public async Task<CancelSubscriptionResponse> CancelSubscriptionAsync(CancellationToken cancellationToken = default)
    {
        await ManageBillingAsync(cancellationToken);
        return new("Use the billing website to confirm cancellation.", null);
    }
}
