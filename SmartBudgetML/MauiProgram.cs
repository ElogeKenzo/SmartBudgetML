using Microsoft.Extensions.Logging;
using SmartBudgetML.Services.Auth;
using SmartBudgetML.Services.Database;

namespace SmartBudgetML;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // Data & Infrastructure
        builder.Services.AddSingleton<IDatabaseService, DatabaseService>();

        // Security & Authentication
        builder.Services.AddSingleton<IBiometricService, BiometricService>();
        builder.Services.AddSingleton<ILocalAuthService, LocalAuthService>();

        return builder.Build();
    }
}
