#if ANDROID
using System.Security.Cryptography;
using System.Text;
using Android.BillingClient.Api;
using ClearlySaid.Shared.Models;
using ClearlySaid.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;

namespace ClearlySaid.App.Services;

public sealed class GooglePlayBillingService : IBillingService, IDisposable
{
    private const string PackageName = "com.clearlysaid.app";
    private readonly ClearlySaidApiClient apiClient;
    private readonly ILogger<GooglePlayBillingService> logger;
    private readonly BillingClient billingClient;
    private readonly SemaphoreSlim restoreLock = new(1, 1);

    public GooglePlayBillingService(
        ClearlySaidApiClient apiClient,
        ILogger<GooglePlayBillingService> logger)
    {
        this.apiClient = apiClient;
        this.logger = logger;

        var pendingPurchases = PendingPurchasesParams.NewBuilder()
            .EnableOneTimeProducts()
            .Build();
        var builder = BillingClient.NewBuilder(Android.App.Application.Context)
            .EnablePendingPurchases(pendingPurchases)
            .EnableAutoServiceReconnection();
        builder.SetListener(OnPurchasesUpdated);
        billingClient = builder.Build();
        apiClient.AccountChanged += OnAccountChanged;
    }

    public bool IsAvailable => true;
    public BillingCheckoutMode CheckoutMode => BillingCheckoutMode.AppStore;
    public string? AvailabilityMessage => null;

    public async Task StartCheckoutAsync(
        string plan,
        string interval,
        CancellationToken cancellationToken = default)
    {
        var account = apiClient.CurrentAccount
            ?? throw new AccountApiException("Sign in before purchasing a ClearlySaid subscription.");
        var definition = SubscriptionPlans.GetRequired(plan);
        if (!definition.IsPurchasable || string.IsNullOrWhiteSpace(definition.GooglePlayProductId))
        {
            throw new AccountApiException("Select a ClearlySaid subscription that is available for purchase.");
        }

        if (interval is not (BillingIntervals.Monthly or BillingIntervals.Annual))
        {
            throw new AccountApiException("Select monthly or annual billing.");
        }

        await EnsureConnectedAsync(cancellationToken);
        var product = QueryProductDetailsParams.Product.NewBuilder()
            .SetProductId(definition.GooglePlayProductId)
            .SetProductType(BillingClient.ProductType.Subs)
            .Build();
        var query = QueryProductDetailsParams.NewBuilder()
            .SetProductList([product])
            .Build();
        var productResult = await QueryProductDetailsAsync(query, cancellationToken);
        EnsureSuccess(productResult.Result, "Google Play could not load the ClearlySaid subscriptions.");

        var details = productResult.ProductDetails.FirstOrDefault(candidate =>
            string.Equals(candidate.ProductId, definition.GooglePlayProductId, StringComparison.Ordinal));
        var offer = details?.GetSubscriptionOfferDetails()?.FirstOrDefault(candidate =>
            string.Equals(candidate.BasePlanId, interval, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(candidate.OfferId));
        if (details is null || offer is null)
        {
            throw new AccountApiException(
                "This ClearlySaid subscription is not active in Google Play yet. Please try again after the Play merchant setup is complete.");
        }

        var productParams = BillingFlowParams.ProductDetailsParams.NewBuilder()
            .SetProductDetails(details)
            .SetOfferToken(offer.OfferToken)
            .Build();
        var flow = BillingFlowParams.NewBuilder()
            .SetProductDetailsParamsList([productParams])
            .SetObfuscatedAccountId(GetObfuscatedAccountId(account.Id))
            .Build();
        var activity = Platform.CurrentActivity
            ?? throw new AccountApiException("Google Play checkout needs an active app screen.");
        var launchResult = billingClient.LaunchBillingFlow(activity, flow);
        EnsureSuccess(launchResult, "Google Play checkout could not start.");
    }

    public async Task ManageBillingAsync(CancellationToken cancellationToken = default)
    {
        var plan = SubscriptionPlans.Find(apiClient.CurrentAccount?.Plan);
        var productQuery = !string.IsNullOrWhiteSpace(plan?.GooglePlayProductId)
            ? $"&sku={Uri.EscapeDataString(plan.GooglePlayProductId)}"
            : string.Empty;
        var target = new Uri(
            $"https://play.google.com/store/account/subscriptions?package={PackageName}{productQuery}");
        if (!await Browser.Default.OpenAsync(target, BrowserLaunchMode.SystemPreferred))
        {
            throw new AccountApiException("ClearlySaid could not open Google Play subscription management.");
        }
    }

    private async Task<(BillingResult Result, IList<ProductDetails> ProductDetails)> QueryProductDetailsAsync(
        QueryProductDetailsParams query,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<(BillingResult, IList<ProductDetails>)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        var listener = new ProductDetailsResponseListener(completion);
        billingClient.QueryProductDetails(query, listener);

        var result = await completion.Task;
        GC.KeepAlive(listener);
        return result;
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (billingClient.IsReady)
        {
            return;
        }

        var result = await billingClient.StartConnectionAsync().WaitAsync(cancellationToken);
        EnsureSuccess(result, "Google Play billing is not available on this device.");
    }

    private void OnPurchasesUpdated(BillingResult result, IList<Purchase> purchases)
    {
        if (result.ResponseCode == BillingResponseCode.UserCancelled)
        {
            return;
        }

        if (result.ResponseCode != BillingResponseCode.Ok)
        {
            logger.LogWarning(
                "Google Play purchase update failed with {Code}: {Message}",
                result.ResponseCode,
                result.DebugMessage);
            return;
        }

        _ = VerifyPurchasesAsync(purchases, CancellationToken.None);
    }

    private void OnAccountChanged(object? sender, EventArgs args)
    {
        if (apiClient.CurrentAccount is not null)
        {
            _ = RestorePurchasesAsync();
        }
    }

    private async Task RestorePurchasesAsync()
    {
        if (!await restoreLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            await EnsureConnectedAsync(CancellationToken.None);
            var query = QueryPurchasesParams.NewBuilder()
                .SetProductType(BillingClient.ProductType.Subs)
                .Build();
            var result = await billingClient.QueryPurchasesAsync(query);
            EnsureSuccess(result.Result, "Google Play could not restore subscriptions.");
            await VerifyPurchasesAsync(result.Purchases, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Google Play subscription restoration did not complete.");
        }
        finally
        {
            restoreLock.Release();
        }
    }

    private async Task VerifyPurchasesAsync(IEnumerable<Purchase> purchases, CancellationToken cancellationToken)
    {
        foreach (var purchase in purchases.Where(candidate => candidate.PurchaseState == PurchaseState.Purchased))
        {
            var productId = purchase.Products.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(productId))
            {
                continue;
            }

            try
            {
                var verification = await apiClient.VerifyGooglePurchaseAsync(
                    new GooglePurchaseVerificationRequest(productId, purchase.PurchaseToken, PackageName),
                    cancellationToken);
                if (verification.ShouldAcknowledge && !purchase.IsAcknowledged)
                {
                    var acknowledge = AcknowledgePurchaseParams.NewBuilder()
                        .SetPurchaseToken(purchase.PurchaseToken)
                        .Build();
                    EnsureSuccess(
                        await billingClient.AcknowledgePurchaseAsync(acknowledge),
                        "Google Play could not finish the subscription purchase.");
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Google Play purchase verification failed.");
            }
        }
    }

    private static string GetObfuscatedAccountId(Guid userId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId.ToString("N"))))
            .ToLowerInvariant();

    private static void EnsureSuccess(BillingResult result, string message)
    {
        if (result.ResponseCode != BillingResponseCode.Ok)
        {
            throw new AccountApiException(
                string.IsNullOrWhiteSpace(result.DebugMessage) ? message : $"{message} {result.DebugMessage}");
        }
    }

    private sealed class ProductDetailsResponseListener(
        TaskCompletionSource<(BillingResult, IList<ProductDetails>)> completion)
        : Java.Lang.Object, IProductDetailsResponseListener
    {
        public void OnProductDetailsResponse(BillingResult result, QueryProductDetailsResult productResult) =>
            completion.TrySetResult((result, productResult.ProductDetailsList));
    }

    public void Dispose()
    {
        apiClient.AccountChanged -= OnAccountChanged;
        billingClient.EndConnection();
        billingClient.Dispose();
        restoreLock.Dispose();
    }
}
#endif
