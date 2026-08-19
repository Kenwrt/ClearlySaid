using ClearlySaid.Shared.Services;
using Microsoft.JSInterop;

namespace ClearlySaid.Web.Services;

public sealed class MetaPixelRegistrationSuccessTracker(
    IJSRuntime jsRuntime,
    ILogger<MetaPixelRegistrationSuccessTracker> logger) : IRegistrationSuccessTracker
{
    public async Task TrackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(
                "clearlySaidMeta.trackCompleteRegistration",
                cancellationToken);
        }
        catch (JSException exception)
        {
            logger.LogWarning(exception, "Meta registration event could not be sent.");
        }
    }
}
