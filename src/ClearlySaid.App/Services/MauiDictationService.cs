using System.Globalization;
using ClearlySaid.Shared.Services;
using CommunityToolkit.Maui.Media;

namespace ClearlySaid.App.Services;

public sealed class MauiDictationService : IDictationService
{
    private readonly ISpeechToText speechToText;
    private string partialTranscript = string.Empty;

    public MauiDictationService(ISpeechToText speechToText)
    {
        this.speechToText = speechToText;
        speechToText.RecognitionResultUpdated += OnRecognitionUpdated;
        speechToText.RecognitionResultCompleted += OnRecognitionCompleted;
    }

    public bool IsSupported => true;
    public bool IsListening { get; private set; }

    public event EventHandler<string>? TranscriptChanged;
    public event EventHandler? ListeningStopped;
    public event EventHandler<string>? Error;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var microphoneStatus = await Permissions.RequestAsync<Permissions.Microphone>();
        var speechPermissionGranted = await speechToText.RequestPermissions(cancellationToken);
        if (microphoneStatus is not PermissionStatus.Granted || !speechPermissionGranted)
        {
            throw new UnauthorizedAccessException(
                "Microphone and speech-recognition permissions are required to dictate.");
        }

        partialTranscript = string.Empty;
        IsListening = true;

        try
        {
            await speechToText.StartListenAsync(new SpeechToTextOptions
            {
                Culture = CultureInfo.CurrentCulture,
                ShouldReportPartialResults = true
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            IsListening = false;
            Error?.Invoke(this, exception.Message);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await speechToText.StopListenAsync(cancellationToken);
        IsListening = false;
        ListeningStopped?.Invoke(this, EventArgs.Empty);
    }

    private void OnRecognitionUpdated(object? sender, SpeechToTextRecognitionResultUpdatedEventArgs e)
    {
        partialTranscript += e.RecognitionResult;
        TranscriptChanged?.Invoke(this, partialTranscript);
    }

    private void OnRecognitionCompleted(object? sender, SpeechToTextRecognitionResultCompletedEventArgs e)
    {
        IsListening = false;

        if (e.RecognitionResult.IsSuccessful)
        {
            TranscriptChanged?.Invoke(this, e.RecognitionResult.Text);
        }
        else
        {
            Error?.Invoke(
                this,
                e.RecognitionResult.Exception?.Message ?? "Speech recognition failed.");
        }

        ListeningStopped?.Invoke(this, EventArgs.Empty);
    }
}
