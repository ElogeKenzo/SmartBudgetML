using SmartBudgetML.Models;
using SmartBudgetML.Services.Auth;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.ML;
using SmartBudgetML.Services.Security;
using Xunit;

namespace SmartBudgetML.Tests.Smoke;

/// <summary>
/// End-to-end host smoke test simulating the full user journey required in Phase 7:
/// 1. First launch & Onboarding account creation (XAF / FCFA, 6-digit PIN)
/// 2. Opening the 256-bit SQLCipher encrypted database & seeding 17 Cameroonian categories
/// 3. Hybrid ML categorization prediction ("Taxi Akwa Bonanjo" / "Facture ENEO")
/// 4. Adding an expense & verifying month-end linear regression forecast
/// 5. App background auto-lock & PIN unlock
/// </summary>
public class EndToEndSmokeTests
{
    [Fact]
    public async Task FullAppSmokeJourney_Onboarding_EncryptedSqlite_HybridMlPrediction_ExpenseCreation_AndAutoLock_ShouldSucceed()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sb_smoke_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "smartbudget_smoke.db3");
        var modelPath = Path.Combine(tempDir, "smoke_model.zip");

        try
        {
            var keyStore = new InMemorySecureKeyStore();
            var db = new DatabaseService(dbPath, keyStore);
            var biometric = new StubBiometricService();
            var auth = new LocalAuthService(db, biometric);

            // 1. Initialize encrypted SQLCipher database and seed 17 Cameroonian categories
            await db.InitializeAsync();
            Assert.True(db.IsEncrypted);

            var categories = await db.Categories.GetAllAsync();
            Assert.Equal(17, categories.Count);

            // 2. Onboarding: create local profile with 6-digit non-trivial PIN in FCFA
            var profile = await auth.CompleteOnboardingAsync(
                fullName: "Jean-Paul Mbarga",
                currencySymbol: "FCFA",
                monthlyIncome: 250_000m,
                pin: "482917",
                enableBiometrics: false,
                monthlySavingsGoal: 50_000m);

            Assert.True(auth.IsAppUnlocked);
            Assert.Equal("FCFA", profile.CurrencySymbol);

            // 3. Hybrid ML categorization (Rules + ML.NET SDCA)
            var rules = new KeywordRuleCategorizer();
            var modelStore = new FileModelStore(modelPath);
            var categorizer = new ExpenseCategorizerService(db, rules, modelStore);
            await categorizer.EnsureTrainedAsync();

            var pred = categorizer.Predict("Moto bendskin Akwa", confidenceThreshold: 0.70f);
            Assert.False(string.IsNullOrWhiteSpace(pred.PredictedCategory));
            var resolvedKey = CategoryCatalog.ResolveCategoryKey(pred.PredictedCategory);
            Assert.Equal("transport", resolvedKey);

            // 4. Add expense & run budget forecast
            var transportCat = categories.Single(c => c.Key == "transport");
            await db.Expenses.InsertAsync(new Expense
            {
                Description = "Moto bendskin Akwa",
                Amount = 500m,
                CategoryId = transportCat.Id,
                Date = DateTime.Today,
                IsMlPredicted = true,
                PredictionConfidence = pred.Confidence
            });

            var expenses = await db.Expenses.GetAllAsync();
            Assert.Single(expenses);

            var forecaster = new BudgetForecastingService();
            var forecast = forecaster.ForecastMonthEnd(expenses, 200_000m, DateTime.Today, "FCFA");
            Assert.Equal(500m, forecast.CurrentTotalSpend);
            Assert.Contains("FCFA", forecast.StatusMessage);

            // 5. Auto-lock on background timeout & unlock with PIN
            var t0 = DateTime.UtcNow;
            auth.RecordBackgrounded(t0);
            var locked = await auth.EvaluateAutoLockOnResumeAsync(t0.AddMinutes(10));
            Assert.True(locked);
            Assert.False(auth.IsAppUnlocked);

            var verify = await auth.VerifyPinWithStatusAsync("482917");
            Assert.True(verify.IsValid);
            Assert.True(auth.IsAppUnlocked);

            await db.PurgeDatabaseAndKeyAsync();
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private sealed class StubBiometricService : IBiometricService
    {
        public Task<bool> IsAvailableAsync() => Task.FromResult(false);
        public Task<bool> AuthenticateAsync(string title, string reason) => Task.FromResult(false);
    }
}
