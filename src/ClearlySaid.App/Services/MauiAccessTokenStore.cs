using ClearlySaid.Shared.Services;

namespace ClearlySaid.App.Services;

public sealed class MauiAccessTokenStore : IAccessTokenStore
{
    private const string Key = "clearlysaid.accessToken";

    public Task<string?> GetAsync() => SecureStorage.Default.GetAsync(Key);

    public Task SetAsync(string token) => SecureStorage.Default.SetAsync(Key, token);

    public Task RemoveAsync()
    {
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}
