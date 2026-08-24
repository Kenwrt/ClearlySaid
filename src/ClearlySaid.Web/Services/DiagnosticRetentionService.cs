using ClearlySaid.Web.Data;

namespace ClearlySaid.Web.Services;

public sealed class DiagnosticRetentionService(
    ClearlySaidDatabase database,
    ILogger<DiagnosticRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupAsync(stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.DeleteExpiredDiagnosticsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Diagnostic retention cleanup failed.");
        }
    }
}
