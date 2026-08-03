using Microsoft.JSInterop;
using ClearlySaid.Shared.Services;

namespace ClearlySaid.Web.Services;

public sealed class BrowserAccessTokenStore(IJSRuntime jsRuntime) : IAccessTokenStore
{
    private const string Key = "clearlysaid.accessToken";

    public Task<string?> GetAsync() =>
        jsRuntime.InvokeAsync<string?>("localStorage.getItem", Key).AsTask();

    public Task SetAsync(string token) =>
        jsRuntime.InvokeVoidAsync("localStorage.setItem", Key, token).AsTask();

    public Task RemoveAsync() =>
        jsRuntime.InvokeVoidAsync("localStorage.removeItem", Key).AsTask();
}
