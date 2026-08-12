using System.Security.Cryptography;
using System.Text;
using ClearlySaid.Shared.Models;
using ClearlySaid.Web.Data;
using Google;
using Google.Apis.AndroidPublisher.v3;
using Google.Apis.AndroidPublisher.v3.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;

namespace ClearlySaid.Web.Services;

public sealed class GooglePlayBillingService(
    IConfiguration configuration,
    ClearlySaidDatabase database,
    ILogger<GooglePlayBillingService> logger)
{
    private const string Active = "SUBSCRIPTION_STATE_ACTIVE";
    private const string GracePeriod = "SUBSCRIPTION_STATE_IN_GRACE_PERIOD";
    private const string Canceled = "SUBSCRIPTION_STATE_CANCELED";
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private AndroidPublisherService? publisher;

    public async Task<GooglePurchaseVerificationResponse> VerifyAsync(
        AuthenticatedUser user,
        GooglePurchaseVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var expectedPackage = configuration["GooglePlay:PackageName"] ?? "com.clearlysaid.app";
        if (!string.Equals(request.PackageName, expectedPackage, StringComparison.Ordinal))
        {
            throw new GooglePlayPurchaseException("The Google Play package is not recognized.");
        }

        var plan = SubscriptionPlans.All.FirstOrDefault(candidate =>
            string.Equals(candidate.GooglePlayProductId, request.ProductId, StringComparison.Ordinal));
        if (plan is null || !plan.IsPurchasable)
        {
            throw new GooglePlayPurchaseException("The Google Play product is not recognized.");
        }

        var existingOwner = await database.FindUserIdByBillingReferenceAsync(
            "google", request.PurchaseToken, cancellationToken);
        if (existingOwner is not null && existingOwner != user.Id)
        {
            throw new GooglePlayPurchaseException("This Google Play purchase belongs to another ClearlySaid account.");
        }

        var service = await GetPublisherAsync(cancellationToken);
        SubscriptionPurchaseV2 purchase;
        try
        {
            purchase = await service.Purchases.Subscriptionsv2
                .Get(expectedPackage, request.PurchaseToken)
                .ExecuteAsync(cancellationToken);
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode is System.Net.HttpStatusCode.NotFound)
        {
            throw new GooglePlayPurchaseException("Google Play could not verify this subscription.", exception);
        }

        var lineItem = purchase.LineItems?.FirstOrDefault(item =>
            string.Equals(item.ProductId, request.ProductId, StringComparison.Ordinal));
        if (lineItem is null)
        {
            throw new GooglePlayPurchaseException("The verified Google Play subscription does not match the selected plan.");
        }

        var basePlanId = lineItem.OfferDetails?.BasePlanId;
        if (basePlanId is not (BillingIntervals.Monthly or BillingIntervals.Annual))
        {
            throw new GooglePlayPurchaseException("The verified Google Play billing period is not recognized.");
        }

        var expectedAccountId = GetObfuscatedAccountId(user.Id);
        var actualAccountId = purchase.ExternalAccountIdentifiers?.ObfuscatedExternalAccountId;
        if (!string.Equals(actualAccountId, expectedAccountId, StringComparison.Ordinal))
        {
            throw new GooglePlayPurchaseException("Sign in with the ClearlySaid account that started this Google Play purchase.");
        }

        var periodStartsAt = purchase.StartTimeDateTimeOffset
            ?? throw new GooglePlayPurchaseException("Google Play has not completed this subscription yet.");
        var periodEndsAt = lineItem.ExpiryTimeDateTimeOffset
            ?? throw new GooglePlayPurchaseException("Google Play did not return a subscription expiration date.");
        var status = MapStatus(purchase.SubscriptionState, periodEndsAt);
        var eventCreatedAt = DateTimeOffset.UtcNow;
        var orderReference = lineItem.LatestSuccessfulOrderId ?? request.PurchaseToken;
        var eventId = $"{request.PurchaseToken}:{purchase.SubscriptionState}:{periodEndsAt.UtcTicks}";

        await database.ApplyBillingSubscriptionAsync(
            new BillingSubscriptionUpdate(
                "google",
                eventId,
                "google.play.subscription.verified",
                eventCreatedAt,
                user.Id,
                expectedAccountId,
                request.PurchaseToken,
                $"{request.ProductId}:{basePlanId}",
                plan.Id,
                status,
                periodStartsAt,
                periodEndsAt),
            cancellationToken);

        var shouldAcknowledge = string.Equals(
            purchase.AcknowledgementState,
            "ACKNOWLEDGEMENT_STATE_PENDING",
            StringComparison.Ordinal);
        if (shouldAcknowledge && status is "active" or "past_due")
        {
            await service.Purchases.Subscriptions.Acknowledge(
                    new SubscriptionPurchasesAcknowledgeRequest(),
                    expectedPackage,
                    request.ProductId,
                    request.PurchaseToken)
                .ExecuteAsync(cancellationToken);
            shouldAcknowledge = false;
        }

        logger.LogInformation(
            "Verified Google Play subscription {OrderReference} for user {UserId} as {Status}.",
            orderReference,
            user.Id,
            status);

        return new GooglePurchaseVerificationResponse(
            await database.GetAccountAsync(user.Id, cancellationToken),
            shouldAcknowledge);
    }

    public static string GetObfuscatedAccountId(Guid userId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId.ToString("N"))))
            .ToLowerInvariant();

    private static string MapStatus(string? state, DateTimeOffset periodEndsAt) => state switch
    {
        Active => "active",
        GracePeriod => "past_due",
        Canceled when periodEndsAt > DateTimeOffset.UtcNow => "active",
        "SUBSCRIPTION_STATE_PENDING" => "pending",
        "SUBSCRIPTION_STATE_ON_HOLD" => "on_hold",
        "SUBSCRIPTION_STATE_PAUSED" => "paused",
        "SUBSCRIPTION_STATE_EXPIRED" => "expired",
        _ => "inactive"
    };

    private async Task<AndroidPublisherService> GetPublisherAsync(CancellationToken cancellationToken)
    {
        if (publisher is not null)
        {
            return publisher;
        }

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (publisher is not null)
            {
                return publisher;
            }

            var credentialPath = configuration["GooglePlay:ServiceAccountJsonPath"];
            if (string.IsNullOrWhiteSpace(credentialPath) || !File.Exists(credentialPath))
            {
                throw new GooglePlayConfigurationException(
                    "Google Play purchase verification is waiting for the service account credential on Web01.");
            }

            var serviceAccount = await CredentialFactory.FromFileAsync<ServiceAccountCredential>(
                credentialPath,
                cancellationToken);
            var credential = serviceAccount.ToGoogleCredential()
                .CreateScoped(AndroidPublisherService.Scope.Androidpublisher);
            publisher = new AndroidPublisherService(new BaseClientService.Initializer
            {
                ApplicationName = "ClearlySaid",
                HttpClientInitializer = credential
            });
            return publisher;
        }
        finally
        {
            initializationLock.Release();
        }
    }
}

public sealed class GooglePlayConfigurationException(string message) : Exception(message);

public sealed class GooglePlayPurchaseException : Exception
{
    public GooglePlayPurchaseException(string message) : base(message) { }
    public GooglePlayPurchaseException(string message, Exception innerException) : base(message, innerException) { }
}
