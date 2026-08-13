using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ClearlySaid.Web.Services;

public sealed class TransactionalEmailService(IHttpClientFactory clients, IConfiguration configuration, ILogger<TransactionalEmailService> logger)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Resend:ApiKey"]);

    public Task SendVerificationAsync(string email, string token, CancellationToken ct) => SendAsync(
        email, "Activate your ClearlySaid account", "Activate account",
        "Confirm your email address to activate your ClearlySaid account.", Link("verify-email", token), ct);

    public Task SendWelcomeAsync(string email, CancellationToken ct) => SendAsync(
        email, "Welcome to ClearlySaid", "Welcome to ClearlySaid",
        "Your email is verified and your account is ready. You can now sign in and start improving messages.", BaseUrl(), ct);

    public Task SendPasswordResetAsync(string email, string token, CancellationToken ct) => SendAsync(
        email, "Reset your ClearlySaid password", "Reset password",
        "Use this secure link within 30 minutes to choose a new password. If you did not request this, you can ignore this email.",
        Link("reset-password", token), ct);

    public Task SendCancellationAsync(string email, DateTimeOffset? accessEndsAt, CancellationToken ct) => SendAsync(
        email, "Your ClearlySaid subscription is scheduled to end", "Subscription canceled",
        accessEndsAt is null ? "Your subscription cancellation was recorded." : $"Your paid access remains available through {accessEndsAt:MMMM d, yyyy}.",
        BaseUrl(), ct);

    public Task SendBillingSummaryAsync(string email, string plan, long amountPaid, string currency,
        DateTimeOffset periodStart, DateTimeOffset periodEnd, string? receiptUrl, CancellationToken ct) => SendAsync(
        email, "Your monthly ClearlySaid billing summary", "Billing summary",
        $"Plan: {plan}. Amount paid: {(amountPaid / 100m):0.00} {currency.ToUpperInvariant()}. Billing period: {periodStart:MMM d, yyyy} through {periodEnd:MMM d, yyyy}.",
        receiptUrl ?? BaseUrl(), ct);

    private async Task SendAsync(string to, string subject, string heading, string message, string actionUrl, CancellationToken ct)
    {
        var key = configuration["Resend:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            logger.LogWarning("Email not sent because Resend is not configured. Subject: {Subject}", subject);
            return;
        }
        var from = configuration["Resend:From"] ?? "ClearlySaid <account@clearlysaid.ai>";
        var html = $"<div style=\"font-family:Arial,sans-serif;max-width:600px;margin:auto\"><h1>{System.Net.WebUtility.HtmlEncode(heading)}</h1><p>{System.Net.WebUtility.HtmlEncode(message)}</p><p><a href=\"{System.Net.WebUtility.HtmlEncode(actionUrl)}\" style=\"background:#3157d5;color:white;padding:12px 18px;text-decoration:none;border-radius:6px\">Continue to ClearlySaid</a></p><p>Need help? Reply to support@clearlysaid.ai.</p></div>";
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{to}|{subject}|{actionUrl}"))));
        request.Content = JsonContent.Create(new { from, to = new[] { to }, subject, html, text = $"{heading}\n\n{message}\n\n{actionUrl}\n\nSupport: support@clearlysaid.ai" });
        using var response = await clients.CreateClient("Resend").SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Email delivery failed with status {(int)response.StatusCode}.");
    }

    private string BaseUrl() => (configuration["PublicBaseUrl"] ?? "https://clearlysaid.ai/").TrimEnd('/') + "/";
    private string Link(string path, string token) => $"{BaseUrl()}{path}?token={Uri.EscapeDataString(token)}";
}
