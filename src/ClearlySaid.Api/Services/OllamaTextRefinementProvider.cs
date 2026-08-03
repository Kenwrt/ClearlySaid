using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClearlySaid.Api.Services;

public sealed class OllamaTextRefinementProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<OllamaTextRefinementProvider> logger) : ITextRefinementProvider
{
    public string Name => "ollama";
    public string Model => configuration["Ollama:Model"] ?? "qwen3-vl:4b-instruct";

    public async Task<TextRefinementResult> RefineAsync(
        string text,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var client = httpClientFactory.CreateClient("Ollama");
        var request = new
        {
            model = Model,
            system = RefinementPrompt.Instructions,
            prompt = text.Trim(),
            stream = false,
            options = new { temperature = 0.1, num_predict = 300 }
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

                return new TextRefinementResult(output, Name, Model, stopwatch.ElapsedMilliseconds);
            }
            catch (JsonException exception)
            {
                throw new DefiniteProviderFailureException("Ollama returned an invalid response.", exception);
            }
        }
    }
}
