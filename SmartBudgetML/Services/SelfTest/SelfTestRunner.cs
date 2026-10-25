#if SB_SELFTEST
using System.Diagnostics;
using SmartBudgetML.Models;
using SmartBudgetML.Resources.Strings;
using SmartBudgetML.Services.Auth;
using SmartBudgetML.Services.Budget;
using SmartBudgetML.Services.Data;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.Localization;
using SmartBudgetML.Services.ML;
using SmartBudgetML.Services.ML.Models;
using SmartBudgetML.Services.Security;
using SmartBudgetML.Services.Tontines;

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

    // Test-only backup password for the throwaway self-test backup.
    private const string SelfTestBackupPassword = "selftest-sauvegarde";

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
    public string BackupPath => Path.Combine(_workDirectory, "selftest.sbbackup");
    private string ExportDirectory => Path.Combine(_workDirectory, "exports");

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
        await StepAsync("reopen_encrypted_db", ReopenEncryptedDatabaseAsync);
        await StepAsync("lock_app", LockAppAsync);
        await StepAsync("export_csv", ExportCsvAsync);
        await StepAsync("password_backup", PasswordBackupAsync);
        await StepAsync("password_restore", PasswordRestoreAsync);
        await StepAsync("tontine_contribution", TontineContributionAsync);
        await StepAsync("irregular_envelope", IrregularEnvelopeAsync);
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

    // Closes the database and opens it again with the stored key: the vault must be Ready
    // and the expense still there (lot 8C, N6).
    private async Task ReopenEncryptedDatabaseAsync()
    {
        var database = RequireDatabase();
        var connection = await database.GetConnectionAsync();
        await connection.CloseAsync();

        _database = new DatabaseService(DatabasePath, _keyStore);
        await _database.InitializeAsync();
        Require(_database.VaultState == VaultState.Ready, $"vault state {_database.VaultState} after reopen");
        Require(_database.IsEncrypted, "reopened database is not encrypted");
        Require(await _database.Expenses.CountAsync() == 1, "expense lost after reopen");
    }

    // Locking must raise LockStateChanged, which MainLayout turns into a redirect to /unlock (N3).
    private Task LockAppAsync()
    {
        var auth = _auth ?? throw new InvalidOperationException("auth not initialized");
        auth.MarkAppUnlocked();
        var raised = 0;
        void OnLocked(object? sender, EventArgs e) => raised++;
        auth.LockStateChanged += OnLocked;
        try
        {
            auth.LockApp();
        }
        finally
        {
            auth.LockStateChanged -= OnLocked;
        }

        Require(raised == 1, $"LockStateChanged raised {raised} times");
        Require(!auth.IsAppUnlocked, "app is still unlocked");
        Require(LockNavigationCoordinator.GetRedirect("", auth.IsAppUnlocked) == LockNavigationCoordinator.UnlockRoute,
            "lock does not redirect to unlock");
        return Task.CompletedTask;
    }

    // CSV export as the Settings screen does it, minus the share sheet (lot 8D).
    private async Task ExportCsvAsync()
    {
        var database = RequireDatabase();
        var exporter = new CsvExpenseExporter(new LocalFileExportService(ExportDirectory));
        var path = await exporter.ExportToFileAsync(
            await database.Expenses.GetAllAsync(),
            await database.Categories.GetAllAsync());
        var content = await File.ReadAllTextAsync(path);
        Require(content.Contains("Moto bendskin Akwa"), "exported CSV does not contain the expense");
    }

    // Backup encrypted with a password, not with the phone key (lot 8D, N5).
    private async Task PasswordBackupAsync()
    {
        var database = RequireDatabase();
        await database.CreatePasswordBackupAsync(BackupPath, SelfTestBackupPassword);
        var status = await database.ValidatePasswordBackupAsync(BackupPath, SelfTestBackupPassword);
        Require(status == BackupValidationStatus.Valid, $"backup validation gave {status}");
        var wrong = await database.ValidatePasswordBackupAsync(BackupPath, "mauvais-mot-de-passe");
        Require(wrong == BackupValidationStatus.Unreadable, $"wrong password gave {wrong}");
    }

    // An expense added after the backup must disappear once the backup is restored.
    private async Task PasswordRestoreAsync()
    {
        var database = RequireDatabase();
        var category = _transportCategory ?? throw new InvalidOperationException("transport category missing");
        await database.Expenses.InsertAsync(new Expense
        {
            Description = "Taxi apres sauvegarde",
            Amount = 1000m,
            CategoryId = category.Id,
            Date = DateTime.Today
        });

        await database.RestorePasswordBackupAsync(BackupPath, SelfTestBackupPassword);
        Require(database.IsEncrypted, "restored database is not encrypted");
        var expenses = await database.Expenses.GetAllAsync();
        Require(expenses.Count == 1 && expenses[0].Description == "Moto bendskin Akwa",
            $"restore gave {expenses.Count} expenses instead of the backed-up one");
    }

    // Tontine stored in the database; the same contribution recorded twice books one expense (lot 8E, N1).
    private async Task TontineContributionAsync()
    {
        var database = RequireDatabase();
        var service = new TontineService(database, new ResxStringLocalizer<AppStrings>());
        var draft = new TontineDraft
        {
            Name = "Njangi selftest",
            ContributionAmount = 5_000m,
            Frequency = TontineFrequency.Weekly,
            StartDate = DateTime.Today
        };
        draft.Members.Add(new TontineMemberDraft("Self Test", 1, IsCurrentUser: true));
        draft.Members.Add(new TontineMemberDraft("Membre A", 2, IsCurrentUser: false));
        var id = await service.CreateAsync(draft);

        var me = (await service.GetDetailsAsync(id, DateTime.Today))?.CurrentUser
            ?? throw new InvalidOperationException("tontine member 'me' missing");
        await service.RecordContributionAsync(id, 1, me.Id, DateTime.Today);
        var second = await service.RecordContributionAsync(id, 1, me.Id, DateTime.Today);
        Require(second == TontineRecordOutcome.AlreadyRecorded, "second contribution was recorded again");

        var linked = await database.Expenses.CountAsync(e => e.SourceType == ExpenseSources.TontineContribution);
        Require(linked == 1, $"{linked} linked expenses instead of 1");
    }

    // Irregular income profile gives a weekly envelope from IrregularIncomePlanner (lot 8E, N14).
    private async Task IrregularEnvelopeAsync()
    {
        var database = RequireDatabase();
        var profile = (await database.UserProfiles.GetAllAsync()).FirstOrDefault()
            ?? throw new InvalidOperationException("profile missing");
        profile.IncomeProfile = IncomeProfileType.Irregular;
        profile.VitalMinimumSpend = 50_000m;
        await database.UserProfiles.UpdateAsync(profile);

        var overview = await new BudgetPlanService(database, new BudgetForecastingService()).GetOverviewAsync(DateTime.Today, "XAF");
        Require(overview.PeriodMode == BudgetPeriodMode.Weekly, $"period mode {overview.PeriodMode}");
        Require(overview.CurrentWeek is not null && overview.CurrentWeek.Allocated > 0m, "no weekly envelope");
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
        foreach (var file in new[] { ModelPath, BackupPath })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        if (Directory.Exists(ExportDirectory))
        {
            Directory.Delete(ExportDirectory, recursive: true);
        }

        Require(!File.Exists(DatabasePath), "self-test database was not deleted");
    }

    private void DeleteDatabaseFiles()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", DatabaseService.PreRestoreSuffix })
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
