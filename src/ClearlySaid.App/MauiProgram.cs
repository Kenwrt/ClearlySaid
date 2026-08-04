using ClearlySaid.App.Services;
using ClearlySaid.Shared.Services;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Media;
using Microsoft.Extensions.Logging;

namespace ClearlySaid.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<ISpeechToText>(SpeechToText.Default);
        builder.Services.AddSingleton<IDictationService, MauiDictationService>();
        builder.Services.AddSingleton(_ => new HttpClient
        {
            BaseAddress = new Uri(AppSettings.ServerBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        });
        builder.Services.AddSingleton<IAccessTokenStore, MauiAccessTokenStore>();
        builder.Services.AddSingleton<ClearlySaidApiClient>();
        builder.Services.AddSingleton<IAccountService>(services => services.GetRequiredService<ClearlySaidApiClient>());
        builder.Services.AddSingleton<IMessageRefinementService>(services => services.GetRequiredService<ClearlySaidApiClient>());
        builder.Services.AddSingleton<IAdminService>(services => services.GetRequiredService<ClearlySaidApiClient>());
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
