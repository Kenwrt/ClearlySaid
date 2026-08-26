using Wright.Messaging.Client;
using Wright.Messaging.Contracts;

namespace ClearlySaid.Web.Services.Messaging;

public sealed class WrightSmsMessageSender(IMessagingClient client) : ISmsMessageSender
{
    public bool IsConfigured => true;

    public async Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        var result = await client.SendAsync(new SendTextRequest(
            message.DestinationE164,
            message.Body,
            message.UserId.ToString("D"),
            message.IdempotencyKey), cancellationToken);
        return new SmsSendResult(result.ProviderMessageId ?? result.MessageId.ToString("D"), DateTimeOffset.UtcNow);
    }
}

public sealed class WrightSmsConsentSynchronizer(IMessagingClient client) : ISmsConsentSynchronizer
{
    public bool IsConfigured => true;
    public async Task SetConsentAsync(Guid userId, string destinationE164, bool optedIn, CancellationToken cancellationToken = default) =>
        _ = await client.SetConsentAsync(new UpdateConsentRequest(
            destinationE164, optedIn, userId.ToString("D")), cancellationToken);
}

public sealed class UnconfiguredSmsMessaging : ISmsMessageSender, ISmsConsentSynchronizer
{
    public bool IsConfigured => false;
    public Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Text messaging is not configured.");
    public Task SetConsentAsync(Guid userId, string destinationE164, bool optedIn, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
