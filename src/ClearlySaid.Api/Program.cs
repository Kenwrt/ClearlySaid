using System.Security.Cryptography;
using System.Text;
using ClearlySaid.Api.Services;
using ClearlySaid.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

builder.Services.AddHttpClient("OpenAI", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("Ollama", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ollama:BaseUrl"] ?? "http://10.168.168.5:11434/");
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Ollama:TimeoutSeconds", 25));
});
builder.Services.AddScoped<OllamaTextRefinementProvider>();
builder.Services.AddScoped<OpenAiTextRefinementProvider>();
builder.Services.AddScoped<ITextRefinementProvider, FallbackTextRefinementProvider>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<OllamaAvailabilityCircuit>();
builder.Services.AddHostedService<OllamaModelWarmupService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", application = "ClearlySaid.Api" }));

app.MapPost("/api/messages/refine", async (
    HttpRequest httpRequest,
    RefineMessageRequest request,
    ITextRefinementProvider refinementProvider,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!HasValidServiceToken(httpRequest, configuration))
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(request.Message)] = ["A message is required."]
        });
    }

    var maximumCharacters = configuration.GetValue("Refinement:MaximumInputCharacters", 5000);
    if (request.Message.Length > maximumCharacters)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(request.Message)] = [$"The message must be {maximumCharacters:N0} characters or fewer."]
        });
    }

    if (!MessageStyleCatalog.TryNormalize(request.Style, out var style))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(request.Style)] = ["Select valid message style options."]
        });
    }

    if (request.RequestId is null || request.RequestId == Guid.Empty ||
        request.UserId is null || request.UserId == Guid.Empty)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(request.RequestId)] = ["A request ID and user ID are required for internal requests."]
        });
    }

    try
    {
        var result = await refinementProvider.RefineAsync(
            request.Message,
            style,
            request.RequestId.Value,
            cancellationToken);
        return Results.Ok(new RefineMessageResponse(
            result.Text,
            request.RequestId,
            result.Provider,
            result.Model,
            result.LatencyMilliseconds,
            result.FallbackUsed,
            result.FailureReason,
            EstimateTokens(request.Message)));
    }
    catch (OpenAiConfigurationException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (OpenAiServiceException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (AmbiguousProviderFailureException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (TextRefinementProviderException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

static bool HasValidServiceToken(HttpRequest request, IConfiguration configuration)
{
    var expectedToken = configuration["CLEARLYSAID_INTERNAL_API_TOKEN"];
    var suppliedToken = request.Headers["X-ClearlySaid-Service-Token"].ToString();

    if (string.IsNullOrWhiteSpace(expectedToken) || string.IsNullOrWhiteSpace(suppliedToken))
    {
        return false;
    }

    var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
    var suppliedBytes = Encoding.UTF8.GetBytes(suppliedToken);
    return expectedBytes.Length == suppliedBytes.Length &&
        CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
}

static int EstimateTokens(string text) => (int)Math.Ceiling(text.Length / 4d);

public partial class Program;
