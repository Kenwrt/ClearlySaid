using ClearlySaid.Shared.Models;

namespace ClearlySaid.Api.Services;

public interface ITextRefinementProvider
{
    string Name { get; }
    string Model { get; }
    Task<TextRefinementResult> RefineAsync(
        string text,
        MessageStyleOptions? style,
        Guid requestId,
        CancellationToken cancellationToken);
}

public sealed record TextRefinementResult(
    string Text,
    string Provider,
    string Model,
    long LatencyMilliseconds,
    bool FallbackUsed = false,
    string? FailureReason = null);

public class TextRefinementProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);

// Only definite failures are eligible for automatic failover. An ambiguous failure may mean
// the provider is still processing the request, so retrying it elsewhere could duplicate work.
public sealed class DefiniteProviderFailureException(string message, Exception? innerException = null)
    : TextRefinementProviderException(message, innerException);

public sealed class AmbiguousProviderFailureException(string message, Exception? innerException = null)
    : TextRefinementProviderException(message, innerException);
