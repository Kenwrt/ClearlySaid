namespace ClearlySaid.Api.Services;

public sealed class OllamaAvailabilityCircuit(IConfiguration configuration, TimeProvider timeProvider)
{
    private long openUntilUtcTicks;

    public bool IsOpen =>
        Interlocked.Read(ref openUntilUtcTicks) > timeProvider.GetUtcNow().UtcTicks;

    public void Open()
    {
        var duration = TimeSpan.FromSeconds(
            Math.Max(1, configuration.GetValue("Ollama:CircuitBreakSeconds", 30)));
        Interlocked.Exchange(
            ref openUntilUtcTicks,
            timeProvider.GetUtcNow().Add(duration).UtcTicks);
    }

    public void Close() => Interlocked.Exchange(ref openUntilUtcTicks, 0);
}
