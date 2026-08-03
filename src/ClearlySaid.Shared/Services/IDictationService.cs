namespace ClearlySaid.Shared.Services;

public interface IDictationService
{
    bool IsSupported { get; }
    bool IsListening { get; }

    event EventHandler<string>? TranscriptChanged;
    event EventHandler? ListeningStopped;
    event EventHandler<string>? Error;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
