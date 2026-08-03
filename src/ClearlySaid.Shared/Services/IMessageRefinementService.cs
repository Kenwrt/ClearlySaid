namespace ClearlySaid.Shared.Services;

public interface IMessageRefinementService
{
    Task<string> RefineAsync(string message, CancellationToken cancellationToken = default);
}
