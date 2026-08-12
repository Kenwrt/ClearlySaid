using System.Diagnostics;
using System.Net.Http.Json;

namespace ClearlySaid.Api.Services;

public sealed class OllamaModelWarmupService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<OllamaModelWarmupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var model = configuration["Ollama:Model"] ?? "qwen3-vl:4b-instruct";
        var keepAlive = configuration.GetValue("Ollama:KeepAlive", -1);
        var client = httpClientFactory.CreateClient("Ollama");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await client.PostAsJsonAsync(
                "api/generate",
                new
                {
                    model,
                    prompt = string.Empty,
                    stream = false,
                    keep_alive = keepAlive
                },
                cancellationToken);

            response.EnsureSuccessStatusCode();
            logger.LogInformation(
                "Ollama model {Model} warmed and retained with keep-alive {KeepAlive} in {ElapsedMilliseconds} ms.",
                model,
                keepAlive,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Ollama model {Model} could not be warmed during startup; requests will continue normally.",
                model);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
