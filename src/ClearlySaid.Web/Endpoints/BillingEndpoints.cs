using ClearlySaid.Shared.Models;
using ClearlySaid.Web.Data;
using ClearlySaid.Web.Services;

namespace ClearlySaid.Web.Endpoints;

public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapClearlySaidBillingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/billing/stripe/checkout", CreateCheckoutAsync)
            .RequireRateLimiting("account");
        endpoints.MapPost("/api/billing/stripe/portal", CreatePortalAsync)
            .RequireRateLimiting("account");
        endpoints.MapPost("/api/billing/stripe/cancel", CancelAsync)
            .RequireRateLimiting("account");
        endpoints.MapPost("/api/billing/stripe/webhook", ProcessWebhookAsync)
            .DisableAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> CancelAsync(HttpRequest request, ClearlySaidDatabase database,
        StripeBillingService billing, TransactionalEmailService email, CancellationToken cancellationToken)
    {
        var user = await AccountEndpoints.AuthenticateAsync(request, database, cancellationToken);
        if (user is null) return Results.Unauthorized();
        try
        {
            var endsAt = await billing.CancelAtPeriodEndAsync(user, cancellationToken);
            await email.SendCancellationAsync(user.Email, endsAt, cancellationToken);
            return Results.Ok(new CancelSubscriptionResponse("Your subscription will not renew.", endsAt));
        }
        catch (StripeBillingRequestException ex) { return Results.Problem(ex.Message, statusCode: 400); }
        catch (StripeBillingConfigurationException ex) { return Results.Problem(ex.Message, statusCode: 503); }
    }

    private static async Task<IResult> CreateCheckoutAsync(
        HttpRequest httpRequest,
        StripeCheckoutRequest request,
        ClearlySaidDatabase database,
        StripeBillingService billing,
        CancellationToken cancellationToken)
    {
        var user = await AccountEndpoints.AuthenticateAsync(httpRequest, database, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(new BillingRedirectResponse(
                await billing.CreateCheckoutAsync(user, request, cancellationToken)));
        }
        catch (StripeBillingRequestException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (StripeBillingConfigurationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> CreatePortalAsync(
        HttpRequest httpRequest,
        ClearlySaidDatabase database,
        StripeBillingService billing,
        CancellationToken cancellationToken)
    {
        var user = await AccountEndpoints.AuthenticateAsync(httpRequest, database, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(new BillingRedirectResponse(
                await billing.CreatePortalAsync(user, cancellationToken)));
        }
        catch (StripeBillingRequestException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (StripeBillingConfigurationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> ProcessWebhookAsync(
        HttpRequest request,
        StripeBillingService billing,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("Stripe-Signature", out var signatures))
        {
            return Results.BadRequest();
        }

        using var reader = new StreamReader(request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        try
        {
            await billing.ProcessWebhookAsync(payload, signatures.ToString(), cancellationToken);
            return Results.Ok();
        }
        catch (StripeWebhookException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (StripeBillingConfigurationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
