using ClearlySaid.Shared.Models;

namespace ClearlySaid.Shared.Services;

public interface IMessageRefinementService
{
    Task<string> RefineAsync(
        string message,
        MessageStyleOptions? style = null,
        CancellationToken cancellationToken = default);
}
