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
        endpoints.MapPost("/api/account/login", LoginAsync).RequireRateLimiting("login");
        endpoints.MapPost("/api/account/email/verify", VerifyEmailAsync).RequireRateLimiting("account");
        endpoints.MapPost("/api/account/email/resend", ResendVerificationAsync).RequireRateLimiting("account");
        endpoints.MapPost("/api/account/password/forgot", ForgotPasswordAsync).RequireRateLimiting("account");
        endpoints.MapPost("/api/account/password/reset", ResetPasswordAsync).RequireRateLimiting("account");
        endpoints.MapPost("/api/account/invitation/accept", AcceptInvitationAsync).RequireRateLimiting("account");
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
        TransactionalEmailService emailService,
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
        if (result is null)
            return Results.Problem("An account with that email already exists.", statusCode: StatusCodes.Status409Conflict);
        var activation = await database.CreateAccountTokenAsync(result.Email, "verify_email", TimeSpan.FromHours(24), true, cancellationToken);
        await emailService.SendVerificationAsync(result.Email, activation.Token, cancellationToken);
        return Results.Accepted(value: new RegistrationResponse("Check your email to activate your ClearlySaid account."));
    }

    private static async Task<IResult> VerifyEmailAsync(TokenRequest request, ClearlySaidDatabase database,
        TransactionalEmailService emailService, CancellationToken cancellationToken)
    {
        var email = await database.VerifyEmailAsync(request.Token, cancellationToken);
        if (email is null) return Results.Problem("This activation link is invalid or expired.", statusCode: 400);
        await emailService.SendWelcomeAsync(email, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> ResendVerificationAsync(EmailRequest request, ClearlySaidDatabase database,
        TransactionalEmailService emailService, CancellationToken cancellationToken)
    {
        var activation = await database.CreateAccountTokenAsync(request.Email, "verify_email", TimeSpan.FromHours(24), true, cancellationToken);
        if (activation.UserId != Guid.Empty) await emailService.SendVerificationAsync(request.Email.Trim(), activation.Token, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> ForgotPasswordAsync(EmailRequest request, ClearlySaidDatabase database,
        TransactionalEmailService emailService, CancellationToken cancellationToken)
    {
        var reset = await database.CreateAccountTokenAsync(request.Email, "password_reset", TimeSpan.FromMinutes(30), false, cancellationToken);
        if (reset.UserId != Guid.Empty) await emailService.SendPasswordResetAsync(request.Email.Trim(), reset.Token, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> ResetPasswordAsync(PasswordResetRequest request, ClearlySaidDatabase database,
        CancellationToken cancellationToken)
    {
        if (!IsValidPassword(request.Password))
            return Results.Problem("Use at least 12 characters, including an uppercase letter, lowercase letter, and number.", statusCode: 400);
        return await database.ResetPasswordWithTokenAsync(request.Token, request.Password, cancellationToken)
            ? Results.Ok() : Results.Problem("This password-reset link is invalid or expired.", statusCode: 400);
    }

    private static async Task<IResult> AcceptInvitationAsync(
        AcceptInvitationRequest request,
        ClearlySaidDatabase database,
        TransactionalEmailService emailService,
        CancellationToken cancellationToken)
    {
        if (!IsValidPassword(request.Password))
            return Results.Problem("Use at least 12 characters, including an uppercase letter, lowercase letter, and number.", statusCode: 400);

        var email = await database.AcceptInvitationAsync(request.Token, request.Password, cancellationToken);
        if (email is null)
            return Results.Problem("This invitation link is invalid or expired.", statusCode: 400);

        await emailService.SendWelcomeAsync(email, cancellationToken);
        return Results.Ok();
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
