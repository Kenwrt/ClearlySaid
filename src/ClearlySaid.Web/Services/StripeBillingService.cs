using ClearlySaid.Shared.Models;
using ClearlySaid.Web.Data;
using Stripe;
using Stripe.Checkout;

namespace ClearlySaid.Web.Services;

public sealed class StripeBillingService(
    IConfiguration configuration,
    ClearlySaidDatabase database,
    TransactionalEmailService emailService,
    ILogger<StripeBillingService> logger)
{
    private const string UserMetadataKey = "clearlysaid_user_id";
    private const string PlanMetadataKey = "clearlysaid_plan_id";

    public async Task<string> CreateCheckoutAsync(
        AuthenticatedUser user,
        StripeCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var plan = SubscriptionPlans.Find(request.Plan);
        if (plan is null || !plan.IsPurchasable)
        {
            throw new StripeBillingRequestException("Select a purchasable ClearlySaid plan.");
        }

        var account = await database.GetAccountAsync(user.Id, cancellationToken);
        if (account.Plan != SubscriptionPlans.Free)
        {
            throw new StripeBillingRequestException(
                string.Equals(account.SubscriptionProvider, "stripe", StringComparison.OrdinalIgnoreCase)
                    ? "Use Manage billing to change an existing ClearlySaid web subscription."
                    : "Your ClearlySaid subscription is already managed by another billing provider.");
        }

        var interval = request.Interval.Trim().ToLowerInvariant();
        if (interval is not BillingIntervals.Monthly and not BillingIntervals.Annual)
        {
            throw new StripeBillingRequestException("Select monthly or annual billing.");
        }

        var priceId = GetPriceId(plan.Id, interval);
        var customerId = await database.GetStripeCustomerReferenceAsync(user.Id, cancellationToken);
        var baseUrl = GetPublicBaseUrl();
        var metadata = new Dictionary<string, string>
        {
            [UserMetadataKey] = user.Id.ToString("D"),
            [PlanMetadataKey] = plan.Id
        };
        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            ClientReferenceId = user.Id.ToString("D"),
            Customer = customerId,
            CustomerEmail = customerId is null ? user.Email : null,
            SuccessUrl = $"{baseUrl}?billing=success&session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{baseUrl}?billing=canceled",
            AllowPromotionCodes = true,
            AutomaticTax = new SessionAutomaticTaxOptions
            {
                Enabled = configuration.GetValue<bool>("Stripe:AutomaticTaxEnabled")
            },
            LineItems =
            [
                new SessionLineItemOptions { Price = priceId, Quantity = 1 }
            ],
            Metadata = metadata,
            SubscriptionData = new SessionSubscriptionDataOptions { Metadata = metadata }
        };

        var session = await new SessionService(CreateClient()).CreateAsync(
            options,
            cancellationToken: cancellationToken);
        return RequireStripeUrl(session.Url);
    }

    public async Task<string> CreatePortalAsync(
        AuthenticatedUser user,
        CancellationToken cancellationToken)
    {
        var customerId = await database.GetStripeCustomerReferenceAsync(user.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new StripeBillingRequestException(
                "Complete a ClearlySaid web subscription before opening billing management.");
        }

        var options = new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = customerId,
            ReturnUrl = GetPublicBaseUrl()
        };
        var configuredPortal = configuration["Stripe:PortalConfigurationId"];
        if (!string.IsNullOrWhiteSpace(configuredPortal))
        {
            options.Configuration = configuredPortal;
        }

        var session = await new Stripe.BillingPortal.SessionService(CreateClient()).CreateAsync(
            options,
            cancellationToken: cancellationToken);
        return RequireStripeUrl(session.Url);
    }

    public async Task<DateTimeOffset?> CancelAtPeriodEndAsync(AuthenticatedUser user, CancellationToken cancellationToken)
    {
        var subscriptionId = await database.GetStripeSubscriptionReferenceAsync(user.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(subscriptionId))
            throw new StripeBillingRequestException("No active ClearlySaid web subscription was found.");
        var subscription = await new SubscriptionService(CreateClient()).UpdateAsync(
            subscriptionId, new SubscriptionUpdateOptions { CancelAtPeriodEnd = true }, cancellationToken: cancellationToken);
        return subscription.Items.Data.OrderByDescending(item => item.CurrentPeriodEnd).FirstOrDefault()?.CurrentPeriodEnd;
    }

    public async Task ProcessWebhookAsync(
        string payload,
        string signature,
        CancellationToken cancellationToken)
    {
        var webhookSecret = RequireConfiguration("Stripe:WebhookSecret");
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                payload,
                signature,
                webhookSecret,
                tolerance: 300,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException exception)
        {
            throw new StripeWebhookException("The Stripe webhook signature is invalid.", exception);
        }

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed" when stripeEvent.Data.Object is Session checkout:
                if (!string.IsNullOrWhiteSpace(checkout.SubscriptionId))
                {
                    var checkoutSubscription = await new SubscriptionService(CreateClient()).GetAsync(
                        checkout.SubscriptionId,
                        cancellationToken: cancellationToken);
                    await ApplySubscriptionAsync(stripeEvent, checkoutSubscription, cancellationToken);
                }
                break;

            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
                if (stripeEvent.Data.Object is Subscription changedSubscription)
                {
                    await ApplySubscriptionAsync(stripeEvent, changedSubscription, cancellationToken);
                }
                break;
        }

        if (stripeEvent.Type == "invoice.paid")
            await SendInvoiceSummaryAsync(payload, cancellationToken);
    }

    private async Task SendInvoiceSummaryAsync(string payload, CancellationToken cancellationToken)
    {
        using var json = System.Text.Json.JsonDocument.Parse(payload);
        var invoice = json.RootElement.GetProperty("data").GetProperty("object");
        var customerId = invoice.TryGetProperty("customer", out var customer) ? customer.GetString() : null;
        if (string.IsNullOrWhiteSpace(customerId)) return;
        var userId = await database.FindUserIdByStripeCustomerAsync(customerId, cancellationToken);
        if (userId is null) return;
        var account = await database.GetAccountAsync(userId.Value, cancellationToken);
        var amount = invoice.TryGetProperty("amount_paid", out var amountPaid) ? amountPaid.GetInt64() : 0;
        var currency = invoice.TryGetProperty("currency", out var currencyValue) ? currencyValue.GetString() ?? "usd" : "usd";
        var receipt = invoice.TryGetProperty("hosted_invoice_url", out var receiptValue) ? receiptValue.GetString() : null;
        var start = invoice.TryGetProperty("period_start", out var startValue) ? DateTimeOffset.FromUnixTimeSeconds(startValue.GetInt64()) : DateTimeOffset.UtcNow;
        var end = invoice.TryGetProperty("period_end", out var endValue) ? DateTimeOffset.FromUnixTimeSeconds(endValue.GetInt64()) : account.PeriodEndsAt;
        await emailService.SendBillingSummaryAsync(account.Email, account.Plan, amount, currency, start, end, receipt, cancellationToken);
    }

    private async Task ApplySubscriptionAsync(
        Event stripeEvent,
        Subscription subscription,
        CancellationToken cancellationToken)
    {
        var item = subscription.Items.Data.OrderByDescending(candidate => candidate.CurrentPeriodEnd).FirstOrDefault();
        var priceId = item?.Price?.Id;
        if (item is null || string.IsNullOrWhiteSpace(priceId))
        {
            throw new StripeWebhookException("The Stripe subscription does not contain a recurring price.");
        }

        var plan = FindPlanByPriceId(priceId)
            ?? throw new StripeWebhookException($"Stripe price '{priceId}' is not configured for ClearlySaid.");
        var userId = TryReadUserId(subscription.Metadata);
        if (userId is null && !string.IsNullOrWhiteSpace(subscription.CustomerId))
        {
            userId = await database.FindUserIdByStripeCustomerAsync(
                subscription.CustomerId,
                cancellationToken);
        }

        if (userId is null)
        {
            logger.LogWarning(
                "Ignoring Stripe event {EventId} because its subscription is not linked to a ClearlySaid account.",
                stripeEvent.Id);
            return;
        }

        await database.ApplyBillingSubscriptionAsync(
            new BillingSubscriptionUpdate(
                "stripe",
                stripeEvent.Id,
                stripeEvent.Type,
                ToOffset(stripeEvent.Created),
                userId.Value,
                subscription.CustomerId,
                subscription.Id,
                priceId,
                plan.Id,
                subscription.Status,
                ToOffset(item.CurrentPeriodStart),
                ToOffset(item.CurrentPeriodEnd)),
            cancellationToken);
    }

    private SubscriptionPlan? FindPlanByPriceId(string priceId) =>
        SubscriptionPlans.All.FirstOrDefault(plan =>
            plan.IsPurchasable &&
            (string.Equals(GetOptionalPriceId(plan.Id, BillingIntervals.Monthly), priceId, StringComparison.Ordinal) ||
             string.Equals(GetOptionalPriceId(plan.Id, BillingIntervals.Annual), priceId, StringComparison.Ordinal)));

    private static Guid? TryReadUserId(IReadOnlyDictionary<string, string>? metadata) =>
        metadata is not null &&
        metadata.TryGetValue(UserMetadataKey, out var value) &&
        Guid.TryParse(value, out var userId)
            ? userId
            : null;

    private StripeClient CreateClient() => new(RequireConfiguration("Stripe:SecretKey"));

    private string GetPriceId(string planId, string interval) =>
        GetOptionalPriceId(planId, interval)
        ?? throw new StripeBillingConfigurationException(
            $"Stripe pricing for the ClearlySaid {planId} {interval} plan is not configured.");

    private string? GetOptionalPriceId(string planId, string interval)
    {
        var planSegment = char.ToUpperInvariant(planId[0]) + planId[1..].ToLowerInvariant();
        var intervalSegment = interval == BillingIntervals.Annual ? "Annual" : "Monthly";
        var value = configuration[$"Stripe:Prices:{planSegment}{intervalSegment}"];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private string GetPublicBaseUrl()
    {
        var configured = configuration["PublicBaseUrl"]
            ?? "https://clearlysaid.ai/";
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new StripeBillingConfigurationException("PublicBaseUrl must be an absolute HTTPS URL.");
        }

        return uri.ToString().TrimEnd('/');
    }

    private string RequireConfiguration(string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value)
            ? throw new StripeBillingConfigurationException($"{key} is not configured on Web01.")
            : value.Trim();
    }

    private static string RequireStripeUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("stripe.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".stripe.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new StripeBillingConfigurationException("Stripe returned an invalid billing URL.");
        }

        return uri.ToString();
    }

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

public sealed class StripeBillingRequestException(string message) : Exception(message);

public sealed class StripeBillingConfigurationException(string message) : Exception(message);

public sealed class StripeWebhookException : Exception
{
    public StripeWebhookException(string message) : base(message) { }
    public StripeWebhookException(string message, Exception innerException) : base(message, innerException) { }
}
