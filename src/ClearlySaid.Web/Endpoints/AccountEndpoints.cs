using System.ComponentModel.DataAnnotations;
using ClearlySaid.Shared.Models;
using ClearlySaid.Web.Data;
using ClearlySaid.Web.Services;

namespace ClearlySaid.Web.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapClearlySaidAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/account/register", RegisterAsync).RequireRateLimiting("account");
        endpoints.MapPost("/api/account/login", LoginAsync).RequireRateLimiting("account");
        endpoints.MapGet("/api/account/me", GetAccountAsync);
        endpoints.MapPost("/api/account/logout", LogoutAsync);
        endpoints.MapDelete("/api/account", DeleteAccountAsync);
        endpoints.MapGet("/api/subscriptions/plans", GetSubscriptionPlans);
        endpoints.MapPost("/api/billing/google/verify", VerifyGooglePurchaseAsync);
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken)
    {
        if (!IsValidEmail(request.Email))
        {
            return Results.Problem("Enter a valid email address.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!IsValidPassword(request.Password))
        {
            return Results.Problem(
                "Use at least 12 characters, including an uppercase letter, lowercase letter, and number.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await database.RegisterAsync(request.Email, request.Password, cancellationToken);
        return result is null
            ? Results.Problem("An account with that email already exists.", statusCode: StatusCodes.Status409Conflict)
            : Results.Ok(result);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken)
    {
        var result = await database.LoginAsync(request.Email, request.Password, cancellationToken);
        return result is null
            ? Results.Problem("The email or password is incorrect.", statusCode: StatusCodes.Status401Unauthorized)
            : Results.Ok(result);
    }

    private static async Task<IResult> GetAccountAsync(
        HttpRequest request,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateAsync(request, database, cancellationToken);
        return user is null
            ? Results.Unauthorized()
            : Results.Ok(await database.GetAccountAsync(user.Id, cancellationToken));
    }

    private static async Task<IResult> LogoutAsync(
        HttpRequest request,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken)
    {
        var token = GetBearerToken(request);
        if (!string.IsNullOrWhiteSpace(token))
        {
            await database.RevokeTokenAsync(token, cancellationToken);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAccountAsync(
        HttpRequest request,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateAsync(request, database, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        await database.DeleteAccountAsync(user.Id, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> VerifyGooglePurchaseAsync(
        HttpRequest request,
        GooglePurchaseVerificationRequest purchase,
        ClearlySaidDatabase database,
        GooglePlayBillingService googlePlayBilling,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateAsync(request, database, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(purchase.PurchaseToken))
        {
            return Results.Problem("A Google Play purchase token is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            return Results.Ok(await googlePlayBilling.VerifyAsync(user, purchase, cancellationToken));
        }
        catch (GooglePlayConfigurationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (GooglePlayPurchaseException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static IResult GetSubscriptionPlans() => Results.Ok(
        SubscriptionPlans.All.Where(plan => !plan.IsInternal));

    public static async Task<AuthenticatedUser?> AuthenticateAsync(
        HttpRequest request,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken) =>
        await database.AuthenticateAsync(GetBearerToken(request), cancellationToken);

    private static string? GetBearerToken(HttpRequest request)
    {
        var value = request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? value[7..].Trim()
            : null;
    }

    private static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email) && new EmailAddressAttribute().IsValid(email);

    private static bool IsValidPassword(string password) =>
        password is { Length: >= 12 } &&
        password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit);
}
