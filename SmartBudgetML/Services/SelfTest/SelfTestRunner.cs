#if SB_SELFTEST
using System.Diagnostics;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Auth;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.ML;
using SmartBudgetML.Services.Security;

namespace SmartBudgetML.Services.SelfTest;

/// <summary>
/// Embedded self-test, compiled only when the SB_SELFTEST constant is defined
/// (CI emulator build and unit tests, never the Google Play build).
/// Each step writes "SB_SELFTEST|step|OK|duration_ms" (or FAIL with the error type),
/// then "SB_SELFTEST|DONE|n_ok/n_total". It works on its own database file and never
/// purges the master key, so it cannot touch the user's real data.
/// </summary>
public sealed class SelfTestRunner
{
    public const string Tag = "SB_SELFTEST";

    // Test-only PIN for the throwaway self-test profile (6 digits, non-trivial).
    private const string SelfTestPin = "482917";

    private readonly ISecureKeyStore _keyStore;
    private readonly IBiometricService _biometricService;
    private readonly string _workDirectory;
    private readonly Action<string> _log;

    private DatabaseService? _database;
    private LocalAuthService? _auth;
    private ExpenseCategorizerService? _categorizer;
    private Category? _transportCategory;

    public SelfTestRunner(
        ISecureKeyStore keyStore,
        IBiometricService biometricService,
        string workDirectory,
        Action<string> log)
    {
        _keyStore = keyStore;
        _biometricService = biometricService;
        _workDirectory = workDirectory;
        _log = log;
    }

    public int Passed { get; private set; }
    public int Total { get; private set; }

    public string DatabasePath => Path.Combine(_workDirectory, "selftest.db3");
    public string ModelPath => Path.Combine(_workDirectory, "selftest_model.zip");

    public async Task<bool> RunAsync()
    {
        Directory.CreateDirectory(_workDirectory);
        Passed = 0;
        Total = 0;

        await StepAsync("open_encrypted_db", OpenEncryptedDatabaseAsync);
        await StepAsync("create_profile_and_pin", CreateProfileAndPinAsync);
        await StepAsync("add_expense", AddExpenseAsync);
        await StepAsync("ml_train", TrainCategorizerAsync);
        await StepAsync("categorize_transport", () => ExpectCategoryAsync("Moto bendskin Akwa", "transport"));
        await StepAsync("categorize_utilities", () => ExpectCategoryAsync("Facture ENEO", "utilities"));
        await StepAsync("pin_hash_verify", VerifyPinAsync);
        await StepAsync("forecast", ForecastAsync);
        await StepAsync("cleanup", CleanupAsync);

        _log($"{Tag}|DONE|{Passed}/{Total}");
        return Passed == Total;
    }

    private async Task StepAsync(string name, Func<Task> step)
    {
        Total++;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await step();
            stopwatch.Stop();
            Passed++;
            _log($"{Tag}|{name}|OK|{stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            // Logged to logcat for the CI job only; never shown in the UI.
            stopwatch.Stop();
            var reason = ex.Message.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
            _log($"{Tag}|{name}|FAIL|{stopwatch.ElapsedMilliseconds}|{ex.GetType().Name}: {reason}");
        }
    }

    private async Task OpenEncryptedDatabaseAsync()
    {
        DeleteDatabaseFiles();
        _database = new DatabaseService(DatabasePath, _keyStore);
        await _database.InitializeAsync();
        Require(_database.IsEncrypted, "database is not encrypted");

        var categories = await _database.Categories.GetAllAsync();
        Require(categories.Count == CategoryCatalog.GetCameroonianDefaultCategories().Count,
            $"unexpected seeded category count {categories.Count}");
        _transportCategory = categories.Single(c => c.Key == "transport");
    }

    private async Task CreateProfileAndPinAsync()
    {
        var database = RequireDatabase();
        _auth = new LocalAuthService(database, _biometricService);
        await _auth.CompleteOnboardingAsync(
            fullName: "Self Test",
            currencySymbol: "FCFA",
            monthlyIncome: 250_000m,
            pin: SelfTestPin,
            enableBiometrics: false);
        Require(await _auth.IsPinSetAsync(), "PIN was not stored");
    }

    private async Task AddExpenseAsync()
    {
        var database = RequireDatabase();
        var category = _transportCategory ?? throw new InvalidOperationException("transport category missing");
        var expense = new Expense
        {
            Description = "Moto bendskin Akwa",
            Amount = 500m,
            CategoryId = category.Id,
            Date = DateTime.Today
        };
        await database.Expenses.InsertAsync(expense);

        var stored = await database.Expenses.GetByIdAsync(expense.Id);
        Require(stored is not null && stored.Amount == 500m, "expense was not persisted");
    }

    private async Task TrainCategorizerAsync()
    {
        var database = RequireDatabase();
        _categorizer = new ExpenseCategorizerService(database, new KeywordRuleCategorizer(), new FileModelStore(ModelPath));
        await _categorizer.EnsureTrainedAsync();
        Require(_categorizer.IsTrained, "categorizer is not trained");
    }

    private Task ExpectCategoryAsync(string description, string expectedKey)
    {
        var categorizer = _categorizer ?? throw new InvalidOperationException("categorizer not trained");
        var prediction = categorizer.Predict(description);
        var key = CategoryCatalog.ResolveCategoryKey(prediction.PredictedCategory);
        Require(key == expectedKey, $"'{description}' gave '{key}' instead of '{expectedKey}'");
        return Task.CompletedTask;
    }

    private async Task VerifyPinAsync()
    {
        var auth = _auth ?? throw new InvalidOperationException("auth not initialized");
        var result = await auth.VerifyPinWithStatusAsync(SelfTestPin);
        Require(result.IsValid, "PIN verification failed");
    }

    private async Task ForecastAsync()
    {
        var database = RequireDatabase();
        var expenses = await database.Expenses.GetAllAsync();
        var forecast = new BudgetForecastingService().ForecastPeriod(expenses, 200_000m);
        Require(forecast.CurrentTotalSpend == 500m, $"forecast spend {forecast.CurrentTotalSpend} instead of 500");
    }

    private async Task CleanupAsync()
    {
        if (_database is not null)
        {
            var connection = await _database.GetConnectionAsync();
            await connection.CloseAsync();
            _database = null;
        }

        DeleteDatabaseFiles();
        if (File.Exists(ModelPath))
        {
            File.Delete(ModelPath);
        }

        Require(!File.Exists(DatabasePath), "self-test database was not deleted");
    }

    private void DeleteDatabaseFiles()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var target = DatabasePath + suffix;
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
    }

    private DatabaseService RequireDatabase() =>
        _database ?? throw new InvalidOperationException("database not opened");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
#endif
