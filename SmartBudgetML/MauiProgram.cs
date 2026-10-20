using Microsoft.Extensions.Logging;
using SmartBudgetML.Services.Auth;
using SmartBudgetML.Services.Data;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.Formatting;
using SmartBudgetML.Services.ML;
using SmartBudgetML.Services.Security;

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

        // Security Key Store & SQLCipher Encrypted Database
        builder.Services.AddSingleton<ISecureKeyStore, MauiSecureKeyStore>();
        builder.Services.AddSingleton<IDatabaseService>(sp =>
            new DatabaseService(
                DatabaseConstants.DatabasePath,
                sp.GetRequiredService<ISecureKeyStore>()));

        // Data, Export & Formatting Infrastructure
        builder.Services.AddSingleton<IAppSettingsProvider, AppSettingsProvider>();
        builder.Services.AddSingleton<IFileExportService, LocalFileExportService>();
        builder.Services.AddSingleton<ICsvExpenseExporter, CsvExpenseExporter>();
        builder.Services.AddSingleton<ICurrencyFormatter, CurrencyFormatter>();

        // Security & Authentication
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IBiometricService, BiometricService>();
        builder.Services.AddSingleton<ILocalAuthService, LocalAuthService>();

        // On-Device Machine Learning (ML.NET)
        builder.Services.AddSingleton<IExpenseCategorizerService, ExpenseCategorizerService>();
        builder.Services.AddSingleton<IModelEvaluator, ModelEvaluator>();
        builder.Services.AddSingleton<IBudgetForecastingService, BudgetForecastingService>();

        return builder.Build();
    }
}
