namespace ClearlySaid.Web.Services.Messaging;

// A future provider-specific NuGet integration implements this contract.
// Callers must verify consent and phone verification before invoking a sender.
public interface ISmsMessageSender
{
    bool IsConfigured { get; }
    Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}

public interface ISmsConsentSynchronizer
{
    bool IsConfigured { get; }
    Task SetConsentAsync(Guid userId, string destinationE164, bool optedIn, CancellationToken cancellationToken = default);
}

public sealed record SmsMessage(
    Guid UserId,
    string DestinationE164,
    string Body,
    string Category,
    string IdempotencyKey);

public sealed record SmsSendResult(string ProviderMessageId, DateTimeOffset AcceptedAt);

public static class SmsMessageCategories
{
    public const string AccountAndService = "account_and_service";
}
