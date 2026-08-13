using System.Net.Http.Json;
using System.Text.Json;
using ClearlySaid.Shared.Models;

namespace ClearlySaid.Web.Services;

public sealed class Api01MessageRefinementService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
{
    public async Task<RefineMessageResponse> RefineWithMetadataAsync(
        string message,
        Guid requestId,
        Guid userId,
        MessageStyleOptions? style,
        CancellationToken cancellationToken = default)
    {
        var serviceToken = configuration["CLEARLYSAID_INTERNAL_API_TOKEN"];
        if (string.IsNullOrWhiteSpace(serviceToken))
        {
            throw new Api01ConfigurationException(
                "ClearlySaid's private message service isn't configured yet.");
        }

        var client = httpClientFactory.CreateClient("Api01");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/messages/refine")
        {
            Content = JsonContent.Create(new RefineMessageRequest(message, requestId, userId, style))
        };
        request.Headers.Add("X-ClearlySaid-Service-Token", serviceToken);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Api01ServiceException(
                "ClearlySaid took longer than expected. Please submit your message again.");
        }
        catch (HttpRequestException)
        {
            throw new Api01ServiceException(
                "ClearlySaid's private message service is temporarily unavailable.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await TryReadProblemDetailAsync(response, cancellationToken);
                if (response.StatusCode is System.Net.HttpStatusCode.RequestTimeout or
                    System.Net.HttpStatusCode.GatewayTimeout)
                {
                    throw new Api01ServiceException(
                        "ClearlySaid took longer than expected. Please submit your message again.");
                }

                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    throw new Api01ConfigurationException(
                        detail ?? "ClearlySaid's private message service isn't configured yet.");
                }

                throw new Api01ServiceException(
                    detail ?? "The message couldn't be improved right now. Please try again.");
            }

            var result = await response.Content.ReadFromJsonAsync<RefineMessageResponse>(cancellationToken);
            return result ?? throw new Api01ServiceException(
                "The message service returned an empty response.");
        }
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

public sealed class Api01ConfigurationException(string message) : Exception(message);
public sealed class Api01ServiceException(string message) : Exception(message);
