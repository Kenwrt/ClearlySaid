namespace ClearlySaid.Shared.Services;

public interface IRegistrationSuccessTracker
{
    Task TrackAsync(CancellationToken cancellationToken = default);
}
