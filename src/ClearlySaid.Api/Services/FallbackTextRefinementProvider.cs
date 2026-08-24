using ClearlySaid.Shared.Models;
using System.Diagnostics;

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
        MessageStyleOptions? style,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await primary.RefineAsync(text, style, requestId, cancellationToken);
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

            var failureCode = GetFailureCode(exception.Message);
            logger.LogWarning(
                "Primary provider definitely failed request {RequestId} with {FailureCode}; using OpenAI fallback.",
                requestId, failureCode);
            var fallbackStopwatch = Stopwatch.StartNew();
            var result = await fallback.RefineAsync(text, style, requestId, cancellationToken);
            logger.LogInformation(
                "Refinement request {RequestId} completed with fallback {Provider}/{Model} in {Latency} ms.",
                requestId, result.Provider, result.Model, result.LatencyMilliseconds);
            var primaryLatency = exception.Data["PreflightLatencyMilliseconds"] as long? ?? 0;
            var events = new List<RefinementDiagnosticEvent>
            {
                new("OllamaPreflightCompleted", primary.Name, primary.Model,
                    primaryLatency, false, false, failureCode),
                new("OpenAIFallbackStarted", fallback.Name, fallback.Model,
                    0, true, true, failureCode)
            };
            if (result.DiagnosticEvents is not null)
            {
                events.AddRange(result.DiagnosticEvents);
            }
            else
            {
                events.Add(new("OpenAIFallbackCompleted", result.Provider, result.Model,
                    fallbackStopwatch.ElapsedMilliseconds, true, true));
            }
            return result with
            {
                FallbackUsed = true,
                FailureReason = failureCode,
                DiagnosticEvents = events
            };
        }
    }

    private static string GetFailureCode(string reason) => reason switch
    {
        var value when value.Contains("circuit", StringComparison.OrdinalIgnoreCase) => "OLLAMA_CIRCUIT_OPEN",
        var value when value.Contains("timed out", StringComparison.OrdinalIgnoreCase) => "OLLAMA_PREFLIGHT_TIMEOUT",
        var value when value.Contains("could not be reached", StringComparison.OrdinalIgnoreCase) => "OLLAMA_UNAVAILABLE",
        var value when value.Contains("empty response", StringComparison.OrdinalIgnoreCase) => "OLLAMA_EMPTY_RESPONSE",
        var value when value.Contains("invalid response", StringComparison.OrdinalIgnoreCase) => "OLLAMA_INVALID_RESPONSE",
        var value when value.Contains("HTTP", StringComparison.OrdinalIgnoreCase) => "OLLAMA_HTTP_ERROR",
        _ => "OLLAMA_PROVIDER_FAILURE"
    };
}
