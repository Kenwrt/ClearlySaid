using ClearlySaid.Shared.Services;
using Microsoft.JSInterop;

namespace ClearlySaid.Web.Services;

public sealed class BrowserDictationService(IJSRuntime jsRuntime) : IDictationService, IAsyncDisposable
{
    private IJSObjectReference? module;
    private DotNetObjectReference<BrowserDictationService>? reference;

    public bool IsSupported { get; private set; } = true;
    public bool IsListening { get; private set; }

    public event EventHandler<string>? TranscriptChanged;
    public event EventHandler? ListeningStopped;
    public event EventHandler<string>? Error;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        module ??= await jsRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            "./_content/ClearlySaid.Shared/clearlysaid-speech.js");

        IsSupported = await module.InvokeAsync<bool>("isSupported", cancellationToken);
        if (!IsSupported)
        {
            throw new NotSupportedException("Speech recognition isn't supported by this browser.");
        }

        reference ??= DotNetObjectReference.Create(this);
        await module.InvokeVoidAsync("start", cancellationToken, reference);
        IsListening = true;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (module is not null && IsListening)
        {
            await module.InvokeVoidAsync("stop", cancellationToken);
        }

        IsListening = false;
        ListeningStopped?.Invoke(this, EventArgs.Empty);
    }

    [JSInvokable]
    public void ReceiveTranscript(string transcript) => TranscriptChanged?.Invoke(this, transcript);

    [JSInvokable]
    public void ReceiveStopped()
    {
        IsListening = false;
        ListeningStopped?.Invoke(this, EventArgs.Empty);
    }

    [JSInvokable]
    public void ReceiveError(string error)
    {
        IsListening = false;
        var message = error switch
        {
            "not-allowed" or "service-not-allowed" =>
                "Microphone access is blocked. Select the lock icon beside the web address, allow Microphone access, and try again.",
            "audio-capture" =>
                "No working microphone was found. Check your device's microphone and try again.",
            "no-speech" =>
                "No speech was detected. Try again and speak after Listening appears.",
            "network" =>
                "Speech recognition couldn't reach the browser's speech service. Check your connection and try again.",
            _ => $"Speech recognition failed: {error}"
        };
        Error?.Invoke(this, message);
    }

    public async ValueTask DisposeAsync()
    {
        reference?.Dispose();
        if (module is not null)
        {
            await module.DisposeAsync();
        }
    }
}
