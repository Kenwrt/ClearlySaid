using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using ClearlySaid.Shared.Models;
using ClearlySaid.Web.Data;
using ClearlySaid.Web.Services;
using ClearlySaid.Web.Services.Messaging;

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
        endpoints.MapPost("/api/account/security-notice/acknowledge", AcknowledgeSecurityNoticeAsync);
        endpoints.MapPut("/api/account/profile/phone", UpdatePhoneProfileAsync).RequireRateLimiting("account");
        endpoints.MapPost("/api/account/profile/phone/verification/send", ResendPhoneVerificationAsync).RequireRateLimiting("account");
        endpoints.MapPost("/api/account/profile/phone/verify", VerifyPhoneAsync).RequireRateLimiting("account");
        endpoints.MapDelete("/api/account", DeleteAccountAsync);
        endpoints.MapGet("/api/subscriptions/plans", GetSubscriptionPlans);
        endpoints.MapPost("/api/billing/google/verify", VerifyGooglePurchaseAsync);
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        ClearlySaidDatabase database,
        TransactionalEmailService emailService,
        IWebHostEnvironment environment,
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

        if (environment.IsStaging() && !emailService.IsConfigured)
        {
            await database.VerifyEmailAsync(activation.Token, cancellationToken);
            return Results.Accepted(value: new RegistrationResponse(
                "Your local staging account is ready. You can sign in."));
        }

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
        ISmsConsentSynchronizer consentSynchronizer,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateAsync(request, database, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var account = await database.GetAccountAsync(user.Id, cancellationToken);
        if (account.PhoneNumber is not null && account.SmsConsentStatus is SmsConsentStatuses.OptedIn or SmsConsentStatuses.PendingVerification)
            await consentSynchronizer.SetConsentAsync(user.Id, account.PhoneNumber, false, cancellationToken);
        await database.DeleteAccountAsync(user.Id, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> AcknowledgeSecurityNoticeAsync(
        HttpRequest request,
        SecurityNoticeAcknowledgementRequest acknowledgement,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateAsync(request, database, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        await database.RecordSecurityNoticeAcknowledgementAsync(
            user.Id,
            acknowledgement.DoNotDisplayAgain,
            cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdatePhoneProfileAsync(
        HttpRequest request,
        UpdatePhoneProfileRequest profile,
        ClearlySaidDatabase database,
        ISmsMessageSender smsSender,
        ISmsConsentSynchronizer consentSynchronizer,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateAsync(request, database, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (profile.GrantSmsConsent && profile.WithdrawSmsConsent)
        {
            return Results.Problem("Choose either consent or opt out, not both.", statusCode: 400);
        }

        var normalizedPhone = NormalizeNorthAmericanPhone(profile.PhoneNumber);
        if (!string.IsNullOrWhiteSpace(profile.PhoneNumber) && normalizedPhone is null)
        {
            return Results.Problem(
                "Enter a valid 10-digit US or Canadian mobile number.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (profile.GrantSmsConsent && normalizedPhone is null)
        {
            return Results.Problem("Enter a mobile number before providing text-message consent.", statusCode: 400);
        }

        var existing = await database.GetAccountAsync(user.Id, cancellationToken);
        var updated = await database.UpdatePhoneProfileAsync(
            user.Id, normalizedPhone, profile.GrantSmsConsent, profile.WithdrawSmsConsent, cancellationToken);

        if (existing.PhoneNumber is not null &&
            (!string.Equals(existing.PhoneNumber, normalizedPhone, StringComparison.Ordinal) || profile.WithdrawSmsConsent))
        {
            await consentSynchronizer.SetConsentAsync(user.Id, existing.PhoneNumber, false, cancellationToken);
        }
        if (normalizedPhone is not null)
        {
            await consentSynchronizer.SetConsentAsync(
                user.Id, normalizedPhone, profile.GrantSmsConsent || updated.SmsConsentStatus == SmsConsentStatuses.OptedIn,
                cancellationToken);
        }

        if (profile.GrantSmsConsent && normalizedPhone is not null)
        {
            if (!smsSender.IsConfigured && environment.IsProduction())
                return Results.Problem("Text messaging is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            if (smsSender.IsConfigured)
            {
                try
                {
                    await CreateAndSendPhoneVerificationAsync(user.Id, normalizedPhone, database, smsSender, cancellationToken);
                }
                catch
                {
                    await database.InvalidatePhoneVerificationAsync(user.Id, CancellationToken.None);
                    return Results.Problem("Your consent was saved, but the verification text could not be sent. Try again.", statusCode: StatusCodes.Status502BadGateway);
                }
            }
        }

        return Results.Ok(updated);
    }

    private static async Task<IResult> ResendPhoneVerificationAsync(
        HttpRequest request, ClearlySaidDatabase database, ISmsMessageSender smsSender,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateAsync(request, database, cancellationToken);
        if (user is null) return Results.Unauthorized();
        var account = await database.GetAccountAsync(user.Id, cancellationToken);
        if (account.PhoneNumber is null || account.SmsConsentStatus != SmsConsentStatuses.PendingVerification)
            return Results.Problem("No phone verification is pending.", statusCode: StatusCodes.Status409Conflict);
        if (!smsSender.IsConfigured)
            return Results.Problem("Text messaging is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        try
        {
            var expiresAt = await CreateAndSendPhoneVerificationAsync(
                user.Id, account.PhoneNumber, database, smsSender, cancellationToken);
            return Results.Ok(new SendPhoneVerificationResponse(expiresAt));
        }
        catch
        {
            await database.InvalidatePhoneVerificationAsync(user.Id, CancellationToken.None);
            return Results.Problem("The verification text could not be sent. Try again.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> VerifyPhoneAsync(
        HttpRequest request, VerifyPhoneRequest verification, ClearlySaidDatabase database,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateAsync(request, database, cancellationToken);
        if (user is null) return Results.Unauthorized();
        if (verification.Code is null || verification.Code.Length != 6 || !verification.Code.All(char.IsDigit))
            return Results.Problem("Enter the six-digit verification code.", statusCode: StatusCodes.Status400BadRequest);
        var salt = await database.GetPhoneVerificationSaltAsync(user.Id, cancellationToken);
        if (salt is null)
            return Results.Problem("The verification code is invalid or expired.", statusCode: StatusCodes.Status400BadRequest);
        var account = await database.ConfirmPhoneVerificationAsync(
            user.Id, HashVerificationCode(salt, verification.Code), cancellationToken);
        return account is null
            ? Results.Problem("The verification code is invalid or expired.", statusCode: StatusCodes.Status400BadRequest)
            : Results.Ok(account);
    }

    private static async Task<DateTimeOffset> CreateAndSendPhoneVerificationAsync(
        Guid userId, string destination, ClearlySaidDatabase database,
        ISmsMessageSender sender, CancellationToken cancellationToken)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var salt = RandomNumberGenerator.GetBytes(32);
        var expiresAt = await database.SavePhoneVerificationAsync(
            userId, salt, HashVerificationCode(salt, code), TimeSpan.FromMinutes(10), cancellationToken);
        await sender.SendAsync(new SmsMessage(
            userId,
            destination,
            $"ClearlySaid verification code: {code}. Expires in 10 minutes. Do not share it. Reply STOP to opt out.",
            SmsMessageCategories.AccountAndService,
            $"clearlysaid:phone-verification:{userId:N}:{DateTimeOffset.UtcNow:yyyyMMddHHmm}"), cancellationToken);
        return expiresAt;
    }

    private static byte[] HashVerificationCode(byte[] salt, string code) =>
        SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(code)]);

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

    private static string? NormalizeNorthAmericanPhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '1') digits = digits[1..];
        return digits.Length == 10 && digits[0] is >= '2' and <= '9'
            ? $"+1{digits}"
            : null;
    }
}
