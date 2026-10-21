using Microsoft.Extensions.Logging;
using SmartBudgetML.Services.Auth;
using SmartBudgetML.Services.Data;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.Formatting;
using SmartBudgetML.Services.Localization;
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

        // Data, Export, Security, Formatting & Localization Infrastructure
        builder.Services.AddSingleton<ISecureKeyStore, MauiSecureKeyStore>();
        builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
        builder.Services.AddSingleton<IAppSettingsProvider, AppSettingsProvider>();
        builder.Services.AddSingleton<IFileExportService, LocalFileExportService>();
        builder.Services.AddSingleton<ICsvExpenseExporter, CsvExpenseExporter>();
        builder.Services.AddSingleton<ICurrencyFormatter, CurrencyFormatter>();
        builder.Services.AddSingleton(typeof(IStringLocalizer<>), typeof(ResxStringLocalizer<>));

        // Security & Authentication
        builder.Services.AddSingleton<IBiometricService, BiometricService>();
        builder.Services.AddSingleton<ILocalAuthService, LocalAuthService>();

        // On-Device Hybrid Machine Learning (3-Stage: UserMemory -> Rules -> ML.NET)
        builder.Services.AddSingleton<IKeywordRuleCategorizer, KeywordRuleCategorizer>();
        builder.Services.AddSingleton<IModelStore, FileModelStore>();
        builder.Services.AddSingleton<IExpenseCategorizerService, ExpenseCategorizerService>();
        builder.Services.AddSingleton<IModelEvaluator, ModelEvaluator>();
        builder.Services.AddSingleton<IBudgetForecastingService, BudgetForecastingService>();

        return builder.Build();
    }
}
