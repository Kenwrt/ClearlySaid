namespace ClearlySaid.Api.Services;

public sealed class FallbackTextRefinementProvider(
    OllamaTextRefinementProvider primary,
    OpenAiTextRefinementProvider fallback,
    IConfiguration configuration,
    ILogger<FallbackTextRefinementProvider> logger) : ITextRefinementProvider
{
    public string Name => "fallback-router";
    public string Model => primary.Model;

    public async Task<TextRefinementResult> RefineAsync(
        string text,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await primary.RefineAsync(text, requestId, cancellationToken);
            logger.LogInformation(
                "Refinement request {RequestId} completed with {Provider}/{Model} in {Latency} ms.",
                requestId, result.Provider, result.Model, result.LatencyMilliseconds);
            return result;
        }
        catch (DefiniteProviderFailureException exception)
        {
            if (!configuration.GetValue("Routing:OpenAiFallbackEnabled", true))
            {
                throw;
            }

            logger.LogWarning(
                exception,
                "Primary provider definitely failed request {RequestId}; using OpenAI fallback.",
                requestId);
            var result = await fallback.RefineAsync(text, requestId, cancellationToken);
            logger.LogInformation(
                "Refinement request {RequestId} completed with fallback {Provider}/{Model} in {Latency} ms.",
                requestId, result.Provider, result.Model, result.LatencyMilliseconds);
            return result with { FallbackUsed = true, FailureReason = SanitizeReason(exception.Message) };
        }
    }

    private static string SanitizeReason(string reason) =>
        reason.Length <= 250 ? reason : reason[..250];
}
