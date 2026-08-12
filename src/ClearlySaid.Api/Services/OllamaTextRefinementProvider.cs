using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using ClearlySaid.Shared.Models;

namespace ClearlySaid.Api.Services;

public sealed class OllamaTextRefinementProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<OllamaTextRefinementProvider> logger) : ITextRefinementProvider
{
    public string Name => "ollama";
    public string Model => configuration["Ollama:Model"] ?? "qwen3-vl:4b-instruct";
    private int KeepAlive => configuration.GetValue("Ollama:KeepAlive", -1);
    private int MaximumOutputTokens => configuration.GetValue("Ollama:MaximumOutputTokens", 128);

    public async Task<TextRefinementResult> RefineAsync(
        string text,
        MessageStyleOptions? style,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var client = httpClientFactory.CreateClient("Ollama");
        var request = new
        {
            model = Model,
            system = RefinementPrompt.BuildInstructions(style),
            prompt = text.Trim(),
            stream = false,
            keep_alive = KeepAlive,
            options = new { temperature = 0.1, num_predict = MaximumOutputTokens }
        };

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("api/generate", request, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AmbiguousProviderFailureException(
                "The primary provider timed out; the request was not sent to a second provider to avoid duplicate processing.",
                exception);
        }
        catch (HttpRequestException exception) when (
            exception.HttpRequestError is HttpRequestError.ConnectionError or
                HttpRequestError.NameResolutionError)
        {
            throw new DefiniteProviderFailureException("The Ollama service could not be reached.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new AmbiguousProviderFailureException(
                "The connection to the primary provider was interrupted; automatic failover was suppressed.",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Ollama failed request {RequestId} with HTTP {StatusCode}.",
                    requestId,
                    response.StatusCode);
                throw new DefiniteProviderFailureException(
                    $"Ollama returned HTTP {(int)response.StatusCode}.");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var output = document.RootElement.TryGetProperty("response", out var value)
                    ? value.GetString()?.Trim()
                    : null;
                if (string.IsNullOrWhiteSpace(output))
                {
                    throw new DefiniteProviderFailureException("Ollama returned an empty response.");
                }

                LogTimingDetails(document.RootElement, requestId);

                return new TextRefinementResult(output, Name, Model, stopwatch.ElapsedMilliseconds);
            }
            catch (JsonException exception)
            {
                throw new DefiniteProviderFailureException("Ollama returned an invalid response.", exception);
            }
        }
    }

    private void LogTimingDetails(JsonElement response, Guid requestId)
    {
        var totalMilliseconds = GetDurationMilliseconds(response, "total_duration");
        var loadMilliseconds = GetDurationMilliseconds(response, "load_duration");
        var promptMilliseconds = GetDurationMilliseconds(response, "prompt_eval_duration");
        var generationMilliseconds = GetDurationMilliseconds(response, "eval_duration");
        var promptTokens = GetInt64(response, "prompt_eval_count");
        var outputTokens = GetInt64(response, "eval_count");
        var tokensPerSecond = generationMilliseconds > 0
            ? outputTokens * 1000d / generationMilliseconds
            : 0d;

        logger.LogInformation(
            "Ollama timings for request {RequestId}: total {TotalMilliseconds} ms, load {LoadMilliseconds} ms, " +
            "prompt {PromptMilliseconds} ms ({PromptTokens} tokens), generation {GenerationMilliseconds} ms " +
            "({OutputTokens} tokens, {TokensPerSecond:F1} tokens/sec).",
            requestId,
            totalMilliseconds,
            loadMilliseconds,
            promptMilliseconds,
            promptTokens,
            generationMilliseconds,
            outputTokens,
            tokensPerSecond);
    }

    private static long GetDurationMilliseconds(JsonElement response, string propertyName) =>
        GetInt64(response, propertyName) / 1_000_000;

    private static long GetInt64(JsonElement response, string propertyName) =>
        response.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var result)
            ? result
            : 0;
}
