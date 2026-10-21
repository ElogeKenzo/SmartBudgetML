using System.Diagnostics;
using Microsoft.ML;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.ML;
using SmartBudgetML.Services.ML.Models;
using SmartBudgetML.Services.Security;
using Xunit;
using Xunit.Abstractions;

namespace SmartBudgetML.Tests.ML;

public class Phase4MlHybridTests
{
    private readonly ITestOutputHelper _output;

    public Phase4MlHybridTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // =========================================================================
    // 4.1 Hybrid 3-Stage Categorizer (UserMemory -> Rules -> ML.NET)
    // =========================================================================

    [Fact]
    public void KeywordRuleCategorizer_NormalizeText_ShouldStripAccentsAmountsPunctuationAndStopwords()
    {
        var rules = new KeywordRuleCategorizer();
        var normalized = rules.NormalizeText("Règlement facture d'Électricité ENEO de 15 000 FCFA à Yaoundé !!");

        Assert.DoesNotContain("é", normalized);
        Assert.DoesNotContain("15", normalized);
        Assert.DoesNotContain("000", normalized);
        Assert.DoesNotContain("fcfa", normalized);
        Assert.Contains("eneo", normalized);
        Assert.Contains("electricite", normalized);
    }

    [Theory]
    [InlineData("Facture ENEO compteur mois", "Utilities", "eneo")]
    [InlineData("Facture CAMWATER eau potable", "Utilities", "camwater")]
    [InlineData("Moto bendskin Akwa vers Deido", "Transportation", "bendskin")]
    [InlineData("Course Yango Bastos", "Transportation", "yango")]
    [InlineData("Cotisation tontine njangi quartier", "Investments", "njangi")]
    [InlineData("Pharmacie achat Coartem paludisme", "Healthcare", "pharmacie")]
    [InlineData("Sortie cinéma CanalOlympia", "Entertainment", "canalolympia")]
    [InlineData("Transfert OM maman aide village", "Other", "om maman")]
    public void KeywordRuleCategorizer_Match_ShouldRecognizeCameroonianKeywordsWithRuleSource(
        string description,
        string expectedCategory,
        string expectedKeywordFragment)
    {
        var rules = new KeywordRuleCategorizer();
        var match = rules.Match(description);

        Assert.NotNull(match);
        Assert.Equal(expectedCategory, match!.PredictedCategory);
        Assert.Equal(PredictionSource.Rules, match.Source);
        Assert.True(match.Confidence >= 0.85f);
        Assert.False(string.IsNullOrWhiteSpace(match.MatchedRuleKeyword));
        Assert.Contains(expectedKeywordFragment, match.MatchedRuleKeyword!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HybridCategorizer_Stage1UserMemory_ShouldOverrideStage2RulesAndStage3Model()
    {
        var categorizer = new ExpenseCategorizerService(seed: 42);
        await categorizer.EnsureTrainedAsync();

        // Normally "Facture ENEO" matches Stage 2 Rules -> Utilities.
        // If the user explicitly categorizes "Facture ENEO boutique" as "Housing & Rent",
        // Stage 1 UserMemory must take priority with Confidence = 1.0 and Source = UserMemory.
        await categorizer.RecordFeedbackAsync("Facture ENEO boutique", "Housing & Rent");

        var prediction = categorizer.Predict("Facture ENEO boutique");

        Assert.Equal("Housing & Rent", prediction.PredictedCategory);
        Assert.Equal(PredictionSource.UserMemory, prediction.Source);
        Assert.Equal(1.0f, prediction.Confidence);
    }

    [Fact]
    public void HybridCategorizer_PredictWhenUntrained_ShouldNeverBlockOrTriggerTraining_AndUseRulesFallback()
    {
        var categorizer = new ExpenseCategorizerService(seed: 42);
        Assert.False(categorizer.IsTrained);

        // Must execute synchronously in < 50 ms without training the ML model
        var sw = Stopwatch.StartNew();
        var rulePred = categorizer.Predict("Moto bendskin Ndokoti");
        var unknownPred = categorizer.Predict("Quelque chose de totalement inconnu xyzabc");
        sw.Stop();

        Assert.False(categorizer.IsTrained, "Predict() must NEVER trigger TrainAsync()!");
        Assert.Equal("Transportation", rulePred.PredictedCategory);
        Assert.Equal(PredictionSource.Rules, rulePred.Source);

        Assert.Equal("Other", unknownPred.PredictedCategory);
        Assert.Equal(PredictionSource.Fallback, unknownPred.Source);
        Assert.True(sw.ElapsedMilliseconds < 100, $"Untrained Predict() took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task HybridCategorizer_ConfidenceThreshold_ShouldFallbackToOther_WhenModelConfidenceBelowThreshold()
    {
        var categorizer = new ExpenseCategorizerService(seed: 42);
        await categorizer.EnsureTrainedAsync();

        // Force threshold = 0.999f with rules disabled so ML model confidence cannot reach threshold
        var pred = categorizer.Predict(
            "Dépense diverse ambiguë au quartier",
            confidenceThreshold: 0.999f,
            useRules: false,
            useUserMemory: false,
            useMlModel: true);

        Assert.Equal("Other", pred.PredictedCategory);
        Assert.Equal(PredictionSource.Fallback, pred.Source);
    }

    // =========================================================================
    // 4.2 Batched Retraining, Model Persistence (IModelStore), & Thread-Safety
    // =========================================================================

    [Fact]
    public async Task RecordFeedbackAsync_ShouldBatchRetraining_OnlyAfterThresholdReached()
    {
        var categorizer = new ExpenseCategorizerService(seed: 42)
        {
            RetrainBatchThreshold = 5
        };
        await categorizer.EnsureTrainedAsync();
        int initialSamples = categorizer.LastTrainingSampleCount;

        // Record 4 feedbacks (< threshold of 5): should update UserMemory immediately without full ML retrain
        for (int i = 1; i <= 4; i++)
        {
            await categorizer.RecordFeedbackAsync($"Achat spécial test {i}", "Shopping");
            Assert.Equal(i, categorizer.PendingFeedbackCount);
            Assert.Equal(initialSamples, categorizer.LastTrainingSampleCount);
        }

        // Immediate Stage 1 UserMemory works even before ML retrain!
        var memPred = categorizer.Predict("Achat spécial test 2");
        Assert.Equal("Shopping", memPred.PredictedCategory);
        Assert.Equal(PredictionSource.UserMemory, memPred.Source);

        // 5th feedback hits threshold -> triggers TrainAsync and resets PendingFeedbackCount to 0
        await categorizer.RecordFeedbackAsync("Achat spécial test 5", "Shopping");
        Assert.Equal(0, categorizer.PendingFeedbackCount);
        Assert.True(categorizer.LastTrainingSampleCount >= initialSamples + 5);
    }

    [Fact]
    public async Task FileModelStore_SaveAndReload_ShouldProduceIdenticalPredictions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sb_model_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var modelPath = Path.Combine(tempDir, "model_v2.zip");

        try
        {
            var store = new FileModelStore(modelPath, schemaVersion: 2);
            var categorizer1 = new ExpenseCategorizerService(null, new KeywordRuleCategorizer(), store, seed: 42);
            await categorizer1.TrainAsync();

            Assert.True(store.Exists());
            Assert.True(store.GetModelFileSizeBytes() > 1024);

            // Second categorizer loads directly from store on EnsureTrainedAsync without retraining from scratch
            var categorizer2 = new ExpenseCategorizerService(null, new KeywordRuleCategorizer(), store, seed: 42);
            await categorizer2.EnsureTrainedAsync();
            Assert.True(categorizer2.IsTrained);

            var testPhrases = new[]
            {
                "Poisson braisé sole et miondo Deido",
                "Marché Mokolo vivres frais plantain",
                "Loyer mensuel appartement 3 pièces",
                "Consultation médecin Hôpital Laquintinie",
                "Virement salaire mensuel employeur"
            };

            foreach (var phrase in testPhrases)
            {
                var p1 = categorizer1.Predict(phrase, confidenceThreshold: 0.1f, useRules: false, useUserMemory: false, useMlModel: true);
                var p2 = categorizer2.Predict(phrase, confidenceThreshold: 0.1f, useRules: false, useUserMemory: false, useMlModel: true);

                Assert.Equal(p1.PredictedCategory, p2.PredictedCategory);
                Assert.Equal(p1.Confidence, p2.Confidence, precision: 4);
            }

            // Schema version mismatch should invalidate cached model and return null on TryLoad
            var incompatibleStore = new FileModelStore(modelPath, schemaVersion: 999);
            var mlContext = new MLContext(seed: 42);
            var loaded = incompatibleStore.TryLoad(mlContext, out _);
            Assert.Null(loaded);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Predict_ConcurrentCallsFromMultipleThreads_ShouldBeThreadSafe()
    {
        var categorizer = new ExpenseCategorizerService(seed: 42);
        await categorizer.EnsureTrainedAsync();

        var phrases = SeedTrainingData.GetHoldoutDataset().Select(x => x.Description).Take(50).ToArray();
        Assert.NotEmpty(phrases);

        var tasks = Enumerable.Range(0, 200).Select(i => Task.Run(() =>
        {
            var phrase = phrases[i % phrases.Length];
            var result = categorizer.Predict(phrase);
            Assert.NotNull(result);
            Assert.False(string.IsNullOrWhiteSpace(result.PredictedCategory));
        }));

        await Task.WhenAll(tasks);
    }

    // =========================================================================
    // 4.3 & 4.4 Cameroonian Dataset Integrity, Anti-Leak, Holdout Evaluation,
    //           Performance Benchmark & Robustness
    // =========================================================================

    [Fact]
    public void CameroonianDatasets_TrainAndHoldout_ShouldMeetSizeRequirements_AndHaveZeroLeakage()
    {
        var train = SeedTrainingData.GetDefaultDataset();
        var holdout = SeedTrainingData.GetHoldoutDataset();
        var rules = new KeywordRuleCategorizer();

        Assert.True(train.Count >= 250, $"Expected >= 250 training samples, got {train.Count}");
        Assert.True(holdout.Count >= 150, $"Expected >= 150 holdout samples, got {holdout.Count}");

        var trainNormalized = new HashSet<string>(
            train.Select(x => x.Description.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        var leaked = holdout
            .Where(h => trainNormalized.Contains(h.Description.Trim().ToLowerInvariant()))
            .Select(h => h.Description)
            .ToList();

        Assert.Empty(leaked);
    }

    [Fact]
    public async Task HoldoutEvaluation_CompareRulesMlAndHybrid_ShouldMeetNonRegressionThresholds()
    {
        var evaluator = new ModelEvaluator(seed: 42);
        var train = SeedTrainingData.GetDefaultDataset();
        var holdout = SeedTrainingData.GetHoldoutDataset();

        var report = await evaluator.CompareApproachesAsync(train, holdout);

        _output.WriteLine($"Train count: {report.TrainingSampleCount}, Holdout count: {report.HoldoutSampleCount}");
        _output.WriteLine($"[Rules Only] MicroAcc={report.RulesOnly.MicroAccuracy:P2}, MacroAcc={report.RulesOnly.MacroAccuracy:P2}, MacroF1={report.RulesOnly.MacroF1:P2}");
        _output.WriteLine($"[ML Only]    MicroAcc={report.MlOnly.MicroAccuracy:P2}, MacroAcc={report.MlOnly.MacroAccuracy:P2}, MacroF1={report.MlOnly.MacroF1:P2}");
        _output.WriteLine($"[Hybrid]     MicroAcc={report.Hybrid.MicroAccuracy:P2}, MacroAcc={report.Hybrid.MacroAccuracy:P2}, MacroF1={report.Hybrid.MacroF1:P2}");

        Assert.True(report.RulesOnly.MicroAccuracy >= 0.65, $"Rules accuracy too low: {report.RulesOnly.MicroAccuracy:P2}");
        Assert.True(report.MlOnly.MicroAccuracy >= 0.75, $"ML accuracy too low: {report.MlOnly.MicroAccuracy:P2}");
        Assert.True(report.Hybrid.MicroAccuracy >= 0.85, $"Hybrid accuracy too low: {report.Hybrid.MicroAccuracy:P2}");
        Assert.True(report.Hybrid.MacroF1 >= 0.85, $"Hybrid MacroF1 too low: {report.Hybrid.MacroF1:P2}");

        // Hybrid must do at least as well as or better than Rules alone and ML alone
        Assert.True(
            report.Hybrid.MicroAccuracy >= report.RulesOnly.MicroAccuracy,
            $"Hybrid ({report.Hybrid.MicroAccuracy:P2}) should beat or equal RulesOnly ({report.RulesOnly.MicroAccuracy:P2})");
        Assert.True(
            report.Hybrid.MicroAccuracy >= report.MlOnly.MicroAccuracy,
            $"Hybrid ({report.Hybrid.MicroAccuracy:P2}) should beat or equal MlOnly ({report.MlOnly.MicroAccuracy:P2})");
    }

    [Fact]
    public async Task PerformanceBenchmark_1000Inferences_ShouldRunUnderLatencyBudget()
    {
        var categorizer = new ExpenseCategorizerService(seed: 42);

        var trainSw = Stopwatch.StartNew();
        await categorizer.TrainAsync();
        trainSw.Stop();

        var holdout = SeedTrainingData.GetHoldoutDataset();
        Assert.NotEmpty(holdout);

        var latenciesMs = new double[1000];
        for (int i = 0; i < 1000; i++)
        {
            var sample = holdout[i % holdout.Count].Description;
            var sw = Stopwatch.StartNew();
            var pred = categorizer.Predict(sample);
            sw.Stop();
            latenciesMs[i] = sw.Elapsed.TotalMilliseconds;
            Assert.NotNull(pred);
        }

        Array.Sort(latenciesMs);
        double avgMs = latenciesMs.Average();
        double p95Ms = latenciesMs[(int)(latenciesMs.Length * 0.95)];

        _output.WriteLine($"TrainTimeMs={trainSw.ElapsedMilliseconds}ms, AvgInferenceMs={avgMs:F4}ms, P95InferenceMs={p95Ms:F4}ms");

        Assert.True(avgMs < 5.0, $"Average inference latency too high: {avgMs:F3} ms");
        Assert.True(p95Ms < 15.0, $"P95 inference latency too high: {p95Ms:F3} ms");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("     ")]
    [InlineData("1234567890")]
    [InlineData("99999 FCFA")]
    [InlineData("🔥🎉💰🚀🛒")]
    [InlineData("買物 スーパー 東京 12345")]
    [InlineData("شراء خبز وحليب من السوق")]
    public async Task Predict_RobustnessInputs_ShouldNeverThrow_AndReturnValidPrediction(string? edgeInput)
    {
        var categorizer = new ExpenseCategorizerService(seed: 42);
        await categorizer.EnsureTrainedAsync();

        var pred = categorizer.Predict(edgeInput);
        Assert.NotNull(pred);
        Assert.False(string.IsNullOrWhiteSpace(pred.PredictedCategory));
        Assert.InRange(pred.Confidence, 0f, 1f);
    }

    [Fact]
    public async Task Predict_10000CharacterString_ShouldNeverThrow_AndCompleteQuickly()
    {
        var categorizer = new ExpenseCategorizerService(seed: 42);
        await categorizer.EnsureTrainedAsync();

        var hugeInput = new string('a', 5000) + " Facture ENEO " + new string('b', 5000);
        var pred = categorizer.Predict(hugeInput);

        Assert.NotNull(pred);
        Assert.False(string.IsNullOrWhiteSpace(pred.PredictedCategory));
    }

    // =========================================================================
    // 4.5 BudgetForecastingService — Fixed Expenses, Irregular Income Modes,
    //     Reliability Index, Edge Cases & Leap Year
    // =========================================================================

    [Fact]
    public void ForecastPeriod_KnownFixedExpenses_ShouldNotBeLinearlyExtrapolatedAcrossMonth()
    {
        var forecaster = new BudgetForecastingService();
        var asOf = new DateTime(2026, 10, 5); // Day 5 of 31

        // User paid monthly rent of 100,000 FCFA on Day 1 (Fixed) + 2,000 FCFA/day variable food (10,000 FCFA over 5 days)
        var expenses = new List<Expense>
        {
            new() { Amount = 100_000m, Date = new DateTime(2026, 10, 1), Description = "Loyer mensuel", IsFixedOrRecurring = true },
            new() { Amount = 2_000m, Date = new DateTime(2026, 10, 1), Description = "Marché jour 1" },
            new() { Amount = 2_000m, Date = new DateTime(2026, 10, 2), Description = "Marché jour 2" },
            new() { Amount = 2_000m, Date = new DateTime(2026, 10, 3), Description = "Marché jour 3" },
            new() { Amount = 2_000m, Date = new DateTime(2026, 10, 4), Description = "Marché jour 4" },
            new() { Amount = 2_000m, Date = new DateTime(2026, 10, 5), Description = "Marché jour 5" }
        };

        // Upcoming known fixed expense later in the month: 15,000 FCFA ENEO bill on Oct 20
        var upcomingFixed = new List<Expense>
        {
            new() { Amount = 15_000m, Date = new DateTime(2026, 10, 20), Description = "Facture ENEO prévue", IsFixedOrRecurring = true }
        };

        // Total expected: 100,000 (paid fixed) + 15,000 (upcoming fixed) + ~62,000 (variable 2,000 * 31) = ~177,000 FCFA
        // Naive linear extrapolation would have projected (110,000 / 5) * 31 = 682,000 FCFA!
        var result = forecaster.ForecastPeriod(
            expenses,
            periodBudget: 250_000m,
            periodMode: BudgetPeriodMode.Monthly,
            asOfDate: asOf,
            currencyCode: "XAF",
            upcomingFixedExpenses: upcomingFixed);

        Assert.Equal(110_000m, result.CurrentTotalSpend);
        Assert.Equal(115_000m, result.FixedCommittedSpend);
        Assert.Equal(10_000m, result.VariableSpend);
        Assert.InRange(result.ProjectedEndMonthSpend, 165_000m, 190_000m);
        Assert.Equal(BudgetHealthStatus.OnTrack, result.Status);
        Assert.Equal(ForecastReliability.Low, result.Reliability); // Day 5 < 7 -> Low reliability
        Assert.False(string.IsNullOrWhiteSpace(result.ReliabilityMessage));
    }

    [Fact]
    public void ForecastPeriod_WeeklyAndEnvelopeModes_ShouldProjectCorrectly()
    {
        var forecaster = new BudgetForecastingService();
        // Wednesday Oct 7, 2026 (Day 3 of 7-day week starting Monday Oct 5)
        var asOf = new DateTime(2026, 10, 7);
        var expenses = new List<Expense>
        {
            new() { Amount = 3_000m, Date = new DateTime(2026, 10, 5), Description = "Taxi lundi" },
            new() { Amount = 3_000m, Date = new DateTime(2026, 10, 6), Description = "Taxi mardi" },
            new() { Amount = 3_000m, Date = new DateTime(2026, 10, 7), Description = "Taxi mercredi" }
        };

        var weeklyResult = forecaster.ForecastPeriod(
            expenses,
            periodBudget: 25_000m,
            periodMode: BudgetPeriodMode.Weekly,
            asOfDate: asOf,
            currencyCode: "XAF");

        Assert.Equal(BudgetPeriodMode.Weekly, weeklyResult.PeriodMode);
        Assert.Equal(7, weeklyResult.DaysInMonth);
        Assert.Equal(3, weeklyResult.DaysElapsed);
        Assert.Equal(9_000m, weeklyResult.CurrentTotalSpend);
        Assert.InRange(weeklyResult.ProjectedEndMonthSpend, 19_000m, 23_000m);

        var envelopeResult = forecaster.ForecastPeriod(
            expenses,
            periodBudget: 20_000m,
            periodMode: BudgetPeriodMode.Envelope,
            asOfDate: asOf,
            currencyCode: "XAF");

        Assert.Equal(BudgetPeriodMode.Envelope, envelopeResult.PeriodMode);
        Assert.Equal(9_000m, envelopeResult.CurrentTotalSpend);
    }

    [Fact]
    public void ForecastPeriod_LeapYearFeb29_EndOfMonth_FutureExpense_AndZeroBudget_ShouldBeHandledCleanly()
    {
        var forecaster = new BudgetForecastingService();

        // Leap year February 2024 (29 days), on day 29, with a future expense in March that must be ignored
        var leapDate = new DateTime(2024, 2, 29);
        var expenses = new List<Expense>
        {
            new() { Amount = 10_000m, Date = new DateTime(2024, 2, 1), Description = "Début février" },
            new() { Amount = 19_000m, Date = new DateTime(2024, 2, 29), Description = "Fin février" },
            new() { Amount = 50_000m, Date = new DateTime(2024, 3, 2), Description = "Dépense future mars" }
        };

        var result = forecaster.ForecastPeriod(
            expenses,
            periodBudget: 0m, // Zero budget edge case
            periodMode: BudgetPeriodMode.Monthly,
            asOfDate: leapDate,
            currencyCode: "XAF");

        Assert.Equal(29, result.DaysInMonth);
        Assert.Equal(29, result.DaysElapsed);
        Assert.Equal(0, result.DaysRemaining);
        Assert.Equal(29_000m, result.CurrentTotalSpend);
        Assert.Equal(BudgetHealthStatus.Warning, result.Status);
        Assert.Equal(ForecastReliability.High, result.Reliability);
    }
}
