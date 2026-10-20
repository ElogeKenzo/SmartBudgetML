using Microsoft.ML;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Implements on-device ML expense categorization using ML.NET
/// and the SDCA Maximum Entropy (Stochastic Dual Coordinate Ascent) algorithm.
/// Persists user category feedback in SQLite across app restarts.
/// </summary>
public class ExpenseCategorizerService : IExpenseCategorizerService
{
    private readonly MLContext _mlContext;
    private readonly IDatabaseService? _databaseService;
    private ITransformer? _model;
    private PredictionEngine<ExpenseTransactionData, CategoryPrediction>? _predictionEngine;
    private readonly List<ExpenseTransactionData> _userTrainingBuffer = new();
    private readonly object _syncLock = new();
    private readonly SemaphoreSlim _trainLock = new(1, 1);

    public bool IsTrained { get; private set; } = false;
    public int LastTrainingSampleCount { get; private set; } = 0;

    public ExpenseCategorizerService(int seed = 42)
        : this(null, seed)
    {
    }

    public ExpenseCategorizerService(IDatabaseService? databaseService, int seed = 42)
    {
        _databaseService = databaseService;
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
            var dataset = SeedTrainingData.GetDefaultDataset();

            if (_databaseService != null)
            {
                await _databaseService.InitializeAsync();
                var persistedFeedback = await _databaseService.CategoryFeedbacks.GetAllAsync();
                foreach (var fb in persistedFeedback)
                {
                    int weight = Math.Clamp(fb.Count, 1, 20);
                    for (int i = 0; i < weight; i++)
                    {
                        dataset.Add(new ExpenseTransactionData
                        {
                            Description = fb.RawDescription,
                            CategoryName = fb.CategoryName
                        });
                    }
                }
            }
            else
            {
                lock (_syncLock)
                {
                    dataset.AddRange(_userTrainingBuffer);
                }
            }

            if (additionalData != null)
            {
                dataset.AddRange(additionalData);
            }

            LastTrainingSampleCount = dataset.Count;

            var trainingDataView = _mlContext.Data.LoadFromEnumerable(dataset);

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

        var rawDesc = description.Trim();
        var normDesc = rawDesc.ToLowerInvariant();
        var catName = confirmedCategory.Trim();

        if (_databaseService != null)
        {
            await _databaseService.InitializeAsync();
            var existing = await _databaseService.CategoryFeedbacks.FirstOrDefaultAsync(
                f => f.NormalizedDescription == normDesc && f.CategoryName == catName);

            if (existing != null)
            {
                existing.Count += 1;
                existing.RawDescription = rawDesc;
                existing.UpdatedAt = DateTime.UtcNow;
                await _databaseService.CategoryFeedbacks.UpdateAsync(existing);
            }
            else
            {
                await _databaseService.CategoryFeedbacks.InsertAsync(new CategoryFeedback
                {
                    NormalizedDescription = normDesc,
                    RawDescription = rawDesc,
                    CategoryName = catName,
                    Count = 1,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }
        else
        {
            lock (_syncLock)
            {
                _userTrainingBuffer.Add(new ExpenseTransactionData
                {
                    Description = rawDesc,
                    CategoryName = catName
                });
            }
        }

        await TrainAsync();
    }
}
