using System.Collections.Concurrent;

namespace ClearlySaid.Web.Services;

public sealed class ActiveRefinementRequests
{
    private readonly ConcurrentDictionary<Guid, byte> activeUsers = new();

    public bool TryEnter(Guid userId, out IDisposable? lease)
    {
        if (!activeUsers.TryAdd(userId, 0))
        {
            lease = null;
            return false;
        }

        lease = new Lease(activeUsers, userId);
        return true;
    }

    private sealed class Lease(ConcurrentDictionary<Guid, byte> activeUsers, Guid userId) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                activeUsers.TryRemove(userId, out _);
            }
        }
    }
}
