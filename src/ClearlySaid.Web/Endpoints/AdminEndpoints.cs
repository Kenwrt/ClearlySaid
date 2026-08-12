using System.ComponentModel.DataAnnotations;
using ClearlySaid.Shared.Models;
using ClearlySaid.Web.Data;

namespace ClearlySaid.Web.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapClearlySaidAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/users", GetUsersAsync);
        endpoints.MapPost("/api/admin/users", CreateUserAsync).RequireRateLimiting("account");
        endpoints.MapPut("/api/admin/users/{userId:guid}", UpdateUserAsync).RequireRateLimiting("account");
        endpoints.MapPost("/api/admin/users/{userId:guid}/reset-password", ResetPasswordAsync).RequireRateLimiting("account");
        endpoints.MapDelete("/api/admin/users/{userId:guid}", DeleteUserAsync).RequireRateLimiting("account");
        endpoints.MapGet("/api/admin/diagnostics", GetDiagnosticsAsync);
        return endpoints;
    }

    private static async Task<IResult> GetUsersAsync(
        HttpRequest request,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken) =>
        await RequireAdminAsync(request, database, cancellationToken) is null
            ? Results.StatusCode(StatusCodes.Status403Forbidden)
            : Results.Ok(await database.GetAdminUsersAsync(cancellationToken));

    private static async Task<IResult> CreateUserAsync(
        HttpRequest httpRequest,
        CreateAdminUserRequest request,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken)
    {
        var admin = await RequireAdminAsync(httpRequest, database, cancellationToken);
        if (admin is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!IsValidEmail(request.Email) || !IsValidPassword(request.Password) ||
            !IsValidRole(request.Role) || !IsValidPlan(request.Plan))
        {
            return Results.Problem("Enter a valid email, strong password, role, and subscription plan.", statusCode: 400);
        }

        var user = await database.CreateAdminUserAsync(request, cancellationToken);
        if (user is null) return Results.Conflict();
        await AuditAsync(database, admin, "UserCreated", $"Created {user.Email} as {user.Role}.", user.Id, cancellationToken);
        return Results.Created($"/api/admin/users/{user.Id}", user);
    }

    private static async Task<IResult> UpdateUserAsync(
        HttpRequest httpRequest,
        Guid userId,
        UpdateAdminUserRequest request,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken)
    {
        var admin = await RequireAdminAsync(httpRequest, database, cancellationToken);
        if (admin is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (admin.Id == userId && (request.Role != AccountRoles.Admin || request.IsDisabled))
        {
            return Results.Problem("Administrators cannot disable or demote their own account.", statusCode: 409);
        }
        if (!IsValidEmail(request.Email) || !IsValidRole(request.Role) ||
            !IsValidPlan(request.Plan))
        {
            return Results.Problem("Enter a valid email, role, and subscription plan.", statusCode: 400);
        }

        try
        {
            var user = await database.UpdateAdminUserAsync(userId, request, cancellationToken);
            if (user is null) return Results.Problem("The email is already used or the user was not found.", statusCode: 409);
            await AuditAsync(database, admin, "UserUpdated", $"Updated {user.Email} ({user.Role}).", user.Id, cancellationToken);
            return Results.Ok(user);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message, statusCode: 409);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> ResetPasswordAsync(
        HttpRequest httpRequest,
        Guid userId,
        ResetAdminPasswordRequest request,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken)
    {
        var admin = await RequireAdminAsync(httpRequest, database, cancellationToken);
        if (admin is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!IsValidPassword(request.NewPassword))
        {
            return Results.Problem(
                "Use at least 12 characters, including uppercase, lowercase, and a number.",
                statusCode: 400);
        }

        if (!await database.ResetAdminPasswordAsync(userId, request.NewPassword, cancellationToken))
        {
            return Results.NotFound();
        }
        await AuditAsync(database, admin, "PasswordReset", "Reset a user password and revoked their sessions.", userId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteUserAsync(
        HttpRequest httpRequest,
        Guid userId,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken)
    {
        var admin = await RequireAdminAsync(httpRequest, database, cancellationToken);
        if (admin is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (admin.Id == userId)
        {
            return Results.Problem("Administrators cannot delete their own account from the console.", statusCode: 409);
        }

        try
        {
            await database.DeleteAdminUserAsync(userId, cancellationToken);
            await AuditAsync(database, admin, "UserDeleted", "Anonymized and disabled a user account.", userId, cancellationToken);
            return Results.NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message, statusCode: 409);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> GetDiagnosticsAsync(
        HttpRequest request,
        int? limit,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken) =>
        await RequireAdminAsync(request, database, cancellationToken) is null
            ? Results.StatusCode(StatusCodes.Status403Forbidden)
            : Results.Ok(await database.GetAdminDiagnosticsAsync(limit ?? 250, cancellationToken));

    private static async Task<AuthenticatedUser?> RequireAdminAsync(
        HttpRequest request,
        ClearlySaidDatabase database,
        CancellationToken cancellationToken)
    {
        var user = await AccountEndpoints.AuthenticateAsync(request, database, cancellationToken);
        return user?.Role == AccountRoles.Admin ? user : null;
    }

    private static Task AuditAsync(
        ClearlySaidDatabase database,
        AuthenticatedUser admin,
        string eventName,
        string message,
        Guid targetUserId,
        CancellationToken cancellationToken) =>
        database.RecordDiagnosticAsync(
            "Information", "Administration", eventName,
            $"{message} Actor: {admin.Email}; target: {targetUserId}.",
            admin.Id, null, cancellationToken);

    private static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email) && new EmailAddressAttribute().IsValid(email);

    private static bool IsValidPassword(string password) =>
        password is { Length: >= 12 } && password.Any(char.IsUpper) &&
        password.Any(char.IsLower) && password.Any(char.IsDigit);

    private static bool IsValidRole(string role) => role is AccountRoles.User or AccountRoles.Admin;
    private static bool IsValidPlan(string plan) => SubscriptionPlans.Find(plan) is not null;
}
