using ClearlySaid.Web.Data;

namespace ClearlySaid.Web.Services;

public sealed class DiagnosticsMiddleware(
    RequestDelegate next,
    ILogger<DiagnosticsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ClearlySaidDatabase database)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled request failure on {Method} {Path}.", context.Request.Method, context.Request.Path);
            try
            {
                await database.RecordDiagnosticAsync(
                    "Error",
                    "Web01",
                    "UnhandledRequestException",
                    $"{exception.GetType().Name}: {exception.Message} on {context.Request.Method} {context.Request.Path}",
                    null,
                    null,
                    CancellationToken.None);
            }
            catch (Exception loggingException)
            {
                logger.LogError(loggingException, "Could not persist the diagnostic event.");
            }

            throw;
        }
    }
}
