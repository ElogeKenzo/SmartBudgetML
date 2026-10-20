using System.Diagnostics;
using Microsoft.ML;
using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

public class ModelEvaluator : IModelEvaluator
{
    private readonly int _seed;
    private ModelEvaluationMetrics _currentMetrics;
    private readonly object _lock = new();

    public ModelEvaluator(int seed = 42)
    {
        _seed = seed;
        var defaultCount = SeedTrainingData.GetDefaultDataset().Count;
        _currentMetrics = ModelEvaluationMetrics.Unevaluated(defaultCount);
    }

    public ModelEvaluationMetrics GetCurrentMetrics()
    {
        lock (_lock)
        {
            return _currentMetrics;
        }
    }

    public async Task<ModelEvaluationMetrics> EvaluateAsync(
        IEnumerable<ExpenseTransactionData>? trainingSamples = null,
        IEnumerable<ExpenseTransactionData>? validationSamples = null)
    {
        return await Task.Run(() =>
        {
            var mlContext = new MLContext(seed: _seed);

            var trainList = (trainingSamples ?? SeedTrainingData.GetDefaultDataset()).ToList();
            var valList = (validationSamples ?? GetDefaultValidationDataset()).ToList();

            if (trainList.Count == 0 || valList.Count == 0)
            {
                var uneval = ModelEvaluationMetrics.Unevaluated(trainList.Count);
                lock (_lock)
                {
                    _currentMetrics = uneval;
                }
                return uneval;
            }

            var trainDataView = mlContext.Data.LoadFromEnumerable(trainList);
            var valDataView = mlContext.Data.LoadFromEnumerable(valList);

            var pipeline = mlContext.Transforms.Conversion.MapValueToKey(
                    inputColumnName: nameof(ExpenseTransactionData.CategoryName),
                    outputColumnName: "Label")
                .Append(mlContext.Transforms.Text.FeaturizeText(
                    inputColumnName: nameof(ExpenseTransactionData.Description),
                    outputColumnName: "Features"))
                .AppendCacheCheckpoint(mlContext)
                .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                    labelColumnName: "Label",
                    featureColumnName: "Features"))
                .Append(mlContext.Transforms.Conversion.MapKeyToValue(
                    inputColumnName: "PredictedLabel",
                    outputColumnName: nameof(CategoryPrediction.PredictedCategory)));

            var model = pipeline.Fit(trainDataView);
            var predictions = model.Transform(valDataView);
            var metrics = mlContext.MulticlassClassification.Evaluate(predictions, labelColumnName: "Label");

            double macroF1 = ComputeMacroF1(metrics);
            double microAcc = double.IsNaN(metrics.MicroAccuracy) ? 0.0 : Math.Clamp(metrics.MicroAccuracy, 0.0, 1.0);
            double macroAcc = double.IsNaN(metrics.MacroAccuracy) ? 0.0 : Math.Clamp(metrics.MacroAccuracy, 0.0, 1.0);

            var result = new ModelEvaluationMetrics(
                IsEvaluated: true,
                MicroAccuracy: microAcc,
                MacroAccuracy: macroAcc,
                MacroF1: macroF1,
                TrainingSampleCount: trainList.Count,
                ValidationSampleCount: valList.Count,
                EvaluatedAtUtc: DateTime.UtcNow,
                StatusDisplay: $"{microAcc * 100.0:0.0}% (F1: {macroF1 * 100.0:0.0}%)");

            lock (_lock)
            {
                _currentMetrics = result;
            }

            return result;
        });
    }

    private static double ComputeMacroF1(Microsoft.ML.Data.MulticlassClassificationMetrics metrics)
    {
        try
        {
            var cm = metrics.ConfusionMatrix;
            if (cm == null || cm.NumberOfClasses == 0)
            {
                return double.IsNaN(metrics.MacroAccuracy) ? 0.0 : Math.Clamp(metrics.MacroAccuracy, 0.0, 1.0);
            }

            int k = cm.NumberOfClasses;
            double sumF1 = 0.0;
            int validClasses = 0;

            for (int i = 0; i < k; i++)
            {
                double precision = cm.PerClassPrecision.Count > i ? cm.PerClassPrecision[i] : 0.0;
                double recall = cm.PerClassRecall.Count > i ? cm.PerClassRecall[i] : 0.0;

                if (double.IsNaN(precision)) precision = 0.0;
                if (double.IsNaN(recall)) recall = 0.0;

                double f1 = (precision + recall) > 1e-9
                    ? (2.0 * precision * recall) / (precision + recall)
                    : 0.0;

                sumF1 += f1;
                validClasses++;
            }

            return validClasses > 0 ? Math.Clamp(sumF1 / validClasses, 0.0, 1.0) : 0.0;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[ModelEvaluator] Fallback to MacroAccuracy for F1 calculation: {ex.Message}");
            return double.IsNaN(metrics.MacroAccuracy) ? 0.0 : Math.Clamp(metrics.MacroAccuracy, 0.0, 1.0);
        }
    }

    public static List<ExpenseTransactionData> GetDefaultValidationDataset() => new()
    {
        new() { Description = "Starbucks coffee and croissant", CategoryName = "Food & Dining" },
        new() { Description = "Domino's Pizza delivery dinner", CategoryName = "Food & Dining" },
        new() { Description = "Uber trip to airport terminal", CategoryName = "Transportation" },
        new() { Description = "Shell gas station fuel refill", CategoryName = "Transportation" },
        new() { Description = "Monthly apartment rent payment", CategoryName = "Housing & Rent" },
        new() { Description = "Home mortgage bank transfer", CategoryName = "Housing & Rent" },
        new() { Description = "Electric power bill payment", CategoryName = "Utilities" },
        new() { Description = "Comcast fiber internet bill", CategoryName = "Utilities" },
        new() { Description = "Whole Foods organic supermarket", CategoryName = "Groceries" },
        new() { Description = "Trader Joe's weekly grocery shopping", CategoryName = "Groceries" },
        new() { Description = "CVS Pharmacy prescription medicine", CategoryName = "Healthcare" },
        new() { Description = "Dental clinic checkup visit", CategoryName = "Healthcare" },
        new() { Description = "Netflix monthly streaming subscription", CategoryName = "Entertainment" },
        new() { Description = "Spotify premium music family plan", CategoryName = "Entertainment" },
        new() { Description = "Amazon prime electronics order", CategoryName = "Shopping" },
        new() { Description = "Nike running shoes store purchase", CategoryName = "Shopping" },
        new() { Description = "Monthly company salary direct deposit", CategoryName = "Salary / Income" },
        new() { Description = "Freelance consulting invoice payment", CategoryName = "Salary / Income" },
        new() { Description = "Vanguard S&P 500 index fund purchase", CategoryName = "Investments" },
        new() { Description = "Fidelity brokerage ETF contribution", CategoryName = "Investments" }
    };
}
