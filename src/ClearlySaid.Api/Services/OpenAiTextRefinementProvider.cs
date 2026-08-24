using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClearlySaid.Shared.Models;

namespace ClearlySaid.Api.Services;

public sealed class OpenAiTextRefinementProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<OpenAiTextRefinementProvider> logger) : ITextRefinementProvider
{
    public string Name => "openai";
    public string Model => configuration["OpenAI:Model"] ?? "gpt-5.6-terra";

    public async Task<TextRefinementResult> RefineAsync(
        string text,
        MessageStyleOptions? style,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["OPENAI_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new OpenAiConfigurationException(
                "ClearlySaid's OpenAI fallback isn't configured.");
        }

        var stopwatch = Stopwatch.StartNew();
        var client = httpClientFactory.CreateClient("OpenAI");
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "responses");
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        requestMessage.Headers.Add("Idempotency-Key", requestId.ToString("N"));
        requestMessage.Content = JsonContent.Create(new
        {
            model = Model,
            instructions = RefinementPrompt.BuildInstructions(style),
            input = text.Trim(),
            reasoning = new { effort = "none" },
            text = new { verbosity = "low" },
            max_output_tokens = 300
        });

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(requestMessage, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OpenAiServiceException(
                "The OpenAI fallback took too long to respond.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new OpenAiServiceException(
                "The OpenAI fallback is temporarily unavailable.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "OpenAI failed request {RequestId} with HTTP {StatusCode}.",
                    requestId,
                    response.StatusCode);
                throw new OpenAiServiceException("The message could not be improved right now. Please try again.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var output = ExtractOutputText(document.RootElement);
            if (string.IsNullOrWhiteSpace(output))
            {
                throw new OpenAiServiceException("The fallback service returned an empty message.");
            }

            return new TextRefinementResult(
                RefinementPrompt.NormalizeOutput(output),
                Name,
                Model,
                stopwatch.ElapsedMilliseconds,
                DiagnosticEvents:
                [new("OpenAIFallbackCompleted", Name, Model,
                    stopwatch.ElapsedMilliseconds, true, true)]);
        }
    }

    private static string? ExtractOutputText(JsonElement response)
    {
        if (!response.TryGetProperty("output", out var output))
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content))
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) &&
                    type.GetString() == "output_text" &&
                    part.TryGetProperty("text", out var text))
                {
                    return text.GetString()?.Trim();
                }
            }
        }

        return null;
    }
}

public sealed class OpenAiConfigurationException(string message) : Exception(message);
public sealed class OpenAiServiceException(string message, Exception? innerException = null)
    : Exception(message, innerException);
