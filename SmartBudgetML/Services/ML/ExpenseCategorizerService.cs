using Microsoft.ML;
using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Implements on-device ML expense categorization using ML.NET
/// and the SDCA Maximum Entropy (Stochastic Dual Coordinate Ascent) algorithm.
/// 100% Local-First Edge Computing.
/// </summary>
public class ExpenseCategorizerService : IExpenseCategorizerService
{
    private readonly MLContext _mlContext;
    private ITransformer? _model;
    private PredictionEngine<ExpenseTransactionData, CategoryPrediction>? _predictionEngine;
    private readonly List<ExpenseTransactionData> _userTrainingBuffer = new();
    private readonly object _syncLock = new();
    private readonly SemaphoreSlim _trainLock = new(1, 1);

    public bool IsTrained { get; private set; } = false;

    public ExpenseCategorizerService(int seed = 42)
    {
        _mlContext = new MLContext(seed: seed);
    }

    public async Task EnsureTrainedAsync()
    {
        if (IsTrained && _predictionEngine != null) return;
        await TrainAsync();
    }

    public async Task TrainAsync(IEnumerable<ExpenseTransactionData>? additionalData = null)
    {
        await _trainLock.WaitAsync();
        try
        {
            // 1. Gather baseline training samples + user feedback buffer + optional additional data
            var dataset = SeedTrainingData.GetDefaultDataset();

            lock (_syncLock)
            {
                dataset.AddRange(_userTrainingBuffer);
            }

            if (additionalData != null)
            {
                dataset.AddRange(additionalData);
            }

            // 2. Load into IDataView
            var trainingDataView = _mlContext.Data.LoadFromEnumerable(dataset);

            // 3. Define ML.NET NLP Pipeline
            // - Map string CategoryName to uint key "Label"
            // - Featurize raw Description text into numeric n-gram features
            // - Apply SDCA Maximum Entropy multiclass classification trainer
            // - Map predicted key back to string CategoryName
            var dataProcessPipeline = _mlContext.Transforms.Conversion.MapValueToKey(
                    inputColumnName: nameof(ExpenseTransactionData.CategoryName),
                    outputColumnName: "Label")
                .Append(_mlContext.Transforms.Text.FeaturizeText(
                    inputColumnName: nameof(ExpenseTransactionData.Description),
                    outputColumnName: "Features"))
                .AppendCacheCheckpoint(_mlContext);

            var trainer = _mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                labelColumnName: "Label",
                featureColumnName: "Features");

            var trainingPipeline = dataProcessPipeline
                .Append(trainer)
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue(
                    inputColumnName: "PredictedLabel",
                    outputColumnName: nameof(CategoryPrediction.PredictedCategory)));

            // 4. Train the model asynchronously
            await Task.Run(() =>
            {
                _model = trainingPipeline.Fit(trainingDataView);

                lock (_syncLock)
                {
                    _predictionEngine = _mlContext.Model.CreatePredictionEngine<ExpenseTransactionData, CategoryPrediction>(_model);
                    IsTrained = true;
                }
            });
        }
        finally
        {
            _trainLock.Release();
        }
    }

    public CategoryPrediction Predict(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return new CategoryPrediction { PredictedCategory = "Other" };
        }

        if (!IsTrained || _predictionEngine == null)
        {
            // Auto-train on demand if called before explicit initialization
            TrainAsync().GetAwaiter().GetResult();
        }

        lock (_syncLock)
        {
            var sample = new ExpenseTransactionData { Description = description.Trim() };
            return _predictionEngine!.Predict(sample);
        }
    }

    public async Task RetrainWithFeedbackAsync(string description, string confirmedCategory)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(confirmedCategory))
        {
            return;
        }

        lock (_syncLock)
        {
            // Add user feedback sample to local buffer
            _userTrainingBuffer.Add(new ExpenseTransactionData
            {
                Description = description.Trim(),
                CategoryName = confirmedCategory.Trim()
            });
        }

        // Trigger incremental retrain
        await TrainAsync();
    }
}
