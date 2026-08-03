using System.Net.Http.Json;
using System.Text.Json;
using ClearlySaid.Shared.Models;
using ClearlySaid.Shared.Services;

namespace ClearlySaid.App.Services;

public sealed class RemoteMessageRefinementService(HttpClient httpClient) : IMessageRefinementService
{
    public async Task<string> RefineAsync(string message, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/messages/refine",
            new RefineMessageRequest(message),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await TryReadProblemDetailAsync(response, cancellationToken);
            throw new InvalidOperationException(
                detail ?? "Your message couldn't be improved right now. Please try again.");
        }

        var result = await response.Content.ReadFromJsonAsync<RefineMessageResponse>(cancellationToken);
        return result?.Message ?? throw new InvalidOperationException("Web01 returned an empty message.");
    }

    private static async Task<string?> TryReadProblemDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var problem = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return problem.RootElement.TryGetProperty("detail", out var detail)
                ? detail.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
