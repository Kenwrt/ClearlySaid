using System.Net;
using System.Threading.RateLimiting;
using ClearlySaid.Shared.Models;
using ClearlySaid.Shared.Services;
using ClearlySaid.Web.Components;
using ClearlySaid.Web.Data;
using ClearlySaid.Web.Endpoints;
using ClearlySaid.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys");
Directory.CreateDirectory(dataProtectionPath);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName("ClearlySaid");

builder.Services.AddHttpClient("Api01", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Api01:BaseUrl"] ?? "https://api01/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<IPasswordHasher<UserCredential>, PasswordHasher<UserCredential>>();
builder.Services.AddSingleton<ClearlySaidDatabase>();
builder.Services.AddSingleton<StripeBillingService>();
builder.Services.AddSingleton<GooglePlayBillingService>();
builder.Services.AddSingleton<TransactionalEmailService>();
builder.Services.AddScoped<IAccessTokenStore, BrowserAccessTokenStore>();
builder.Services.AddScoped<ClearlySaidApiClient>(services => new ClearlySaidApiClient(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("Public"),
    services.GetRequiredService<IAccessTokenStore>()));
builder.Services.AddScoped<IAccountService>(services => services.GetRequiredService<ClearlySaidApiClient>());
builder.Services.AddScoped<IMessageRefinementService>(services => services.GetRequiredService<ClearlySaidApiClient>());
builder.Services.AddScoped<IAdminService>(services => services.GetRequiredService<ClearlySaidApiClient>());
builder.Services.AddScoped<IBillingService, StripeWebBillingService>();
builder.Services.AddScoped<IRegistrationSuccessTracker, MetaPixelRegistrationSuccessTracker>();
builder.Services.AddScoped<Api01MessageRefinementService>();
builder.Services.AddSingleton<ActiveRefinementRequests>();
builder.Services.AddScoped<IDictationService, BrowserDictationService>();
builder.Services.AddHttpClient("Public", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["PublicBaseUrl"] ?? "https://clearlysaid.ai/");
    client.Timeout = TimeSpan.FromSeconds(35);
});
builder.Services.AddHttpClient("Resend", client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
#if NET10_0_OR_GREATER
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
#else
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
#endif
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var detail = context.HttpContext.Request.Path.Equals(
            "/api/account/login",
            StringComparison.OrdinalIgnoreCase)
            ? "You've reached the maximum number of sign-in attempts. Please wait five minutes before trying again."
            : "Too many requests. Please wait and try again.";
        await Results.Problem(detail: detail, statusCode: StatusCodes.Status429TooManyRequests)
            .ExecuteAsync(context.HttpContext);
    };
    options.AddPolicy("refine", context => RateLimitPartition.GetFixedWindowLimiter(
        GetClientPartitionKey(context, builder.Configuration),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        GetClientPartitionKey(context, builder.Configuration),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("account", context => RateLimitPartition.GetFixedWindowLimiter(
        GetClientPartitionKey(context, builder.Configuration),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0
        }));
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(exceptionHandlerApp =>
        exceptionHandlerApp.Run(context => Results.Problem().ExecuteAsync(context)));
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseRateLimiter();
app.UseMiddleware<DiagnosticsMiddleware>();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.Equals(
            "/downloads/ClearlySaid-Android-Test.apk",
            StringComparison.OrdinalIgnoreCase))
    {
        await next(context);
        return;
    }

    var database = context.RequestServices.GetRequiredService<ClearlySaidDatabase>();
    var user = await AccountEndpoints.AuthenticateAsync(
        context.Request,
        database,
        context.RequestAborted);
    if (user is null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    if (user.Role != AccountRoles.Admin)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    var packagePath = Path.Combine(
        app.Environment.WebRootPath,
        "downloads",
        "ClearlySaid-Android-Test.apk");
    if (!File.Exists(packagePath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = "application/vnd.android.package-archive";
    context.Response.Headers.ContentDisposition =
        "attachment; filename=ClearlySaid-Android-Test.apk";
    await context.Response.SendFileAsync(packagePath, context.RequestAborted);
});

await app.Services.GetRequiredService<ClearlySaidDatabase>().InitializeAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "healthy", application = "ClearlySaid" }));

app.MapClearlySaidAccountEndpoints();
app.MapClearlySaidAdminEndpoints();
app.MapClearlySaidBillingEndpoints();

app.MapPost("/api/messages/refine", async (
    RefineMessageRequest request,
    HttpRequest httpRequest,
    Api01MessageRefinementService refinementService,
    ActiveRefinementRequests activeRequests,
    ClearlySaidDatabase database,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var user = await AccountEndpoints.AuthenticateAsync(httpRequest, database, cancellationToken);
    if (user is null)
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

    if (style is not null)
    {
        var account = await database.GetAccountAsync(user.Id, cancellationToken);
        if (!account.IsUnlimited && account.Plan == SubscriptionPlans.Free)
        {
            return Results.Problem(
                "Message style controls require a paid plan.",
                statusCode: StatusCodes.Status403Forbidden);
        }
    }

    if (!activeRequests.TryEnter(user.Id, out var activeRequestLease))
    {
        return Results.Problem(
            "Another message is already being improved for this account.",
            statusCode: StatusCodes.Status409Conflict);
    }

    using (activeRequestLease)
    {
        var requestId = request.RequestId is { } suppliedRequestId && suppliedRequestId != Guid.Empty
            ? suppliedRequestId
            : Guid.NewGuid();
        var reservation = await database.TryReserveUsageAsync(
            user.Id,
            requestId,
            request.Message.Length,
            cancellationToken);
        if (reservation.Duplicate)
        {
            return Results.Problem(
                "This request ID has already been used.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (reservation.UsageId is null)
        {
            return Results.Problem(
                "You have reached your monthly message allowance.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        try
        {
            var result = await refinementService.RefineWithMetadataAsync(
                request.Message,
                requestId,
                user.Id,
                style,
                cancellationToken);
            await database.CompleteUsageAsync(reservation.UsageId.Value, result, cancellationToken);
            return Results.Ok(result);
        }
        catch (Api01ConfigurationException exception)
        {
            await database.ReleaseUsageAsync(reservation.UsageId.Value, exception.Message, cancellationToken);
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Api01ServiceException exception)
        {
            await database.ReleaseUsageAsync(reservation.UsageId.Value, exception.Message, cancellationToken);
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
        catch
        {
            await database.ReleaseUsageAsync(
                reservation.UsageId.Value,
                "Unhandled Web01 refinement error.",
                CancellationToken.None);
            throw;
        }
    }
}).RequireRateLimiting("refine");

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(ClearlySaid.Shared.Components.Routes).Assembly);

static string GetClientPartitionKey(HttpContext context, IConfiguration configuration)
{
    if (configuration.GetValue<bool>("Cloudflare:TrustConnectingIpHeader") &&
        context.Request.Headers.TryGetValue("CF-Connecting-IP", out var values) &&
        IPAddress.TryParse(values.ToString(), out var cloudflareClientIp))
    {
        return cloudflareClientIp.ToString();
    }

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

app.Run();

public partial class Program;
