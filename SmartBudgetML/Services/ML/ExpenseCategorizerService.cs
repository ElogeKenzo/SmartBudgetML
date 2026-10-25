using System.Collections.Concurrent;
using Microsoft.ML;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Implements the 3-stage hybrid on-device expense categorizer:
/// Stage 1: UserMemory (exact or normalized CategoryFeedback match, confidence = 1.0)
/// Stage 2: Deterministic Keyword Rules (IKeywordRuleCategorizer)
/// Stage 3: On-device ML.NET SDCA Maximum Entropy classifier with confidence threshold.
/// </summary>
public class ExpenseCategorizerService : IExpenseCategorizerService
{
    private readonly MLContext _mlContext;
    private readonly IDatabaseService? _databaseService;
    private readonly IKeywordRuleCategorizer _ruleCategorizer;
    private readonly IModelStore? _modelStore;

    private ITransformer? _model;
    private PredictionEngine<ExpenseTransactionData, CategoryPrediction>? _predictionEngine;
    private readonly List<ExpenseTransactionData> _userTrainingBuffer = new();
    private readonly ConcurrentDictionary<string, string> _userMemory = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _syncLock = new();
    private readonly SemaphoreSlim _trainLock = new(1, 1);

    public bool IsTrained { get; private set; } = false;
    public int LastTrainingSampleCount { get; private set; } = 0;
    public int PendingFeedbackCount { get; private set; } = 0;
    public int RetrainBatchThreshold { get; set; } = 15;

    public ExpenseCategorizerService(int seed = 42)
        : this(null, null, null, seed)
    {
    }

    public ExpenseCategorizerService(IDatabaseService? databaseService, int seed = 42)
        : this(databaseService, null, null, seed)
    {
    }

    public ExpenseCategorizerService(
        IDatabaseService? databaseService,
        IKeywordRuleCategorizer? ruleCategorizer,
        IModelStore? modelStore,
        int seed = 42)
    {
        _databaseService = databaseService;
        _ruleCategorizer = ruleCategorizer ?? new KeywordRuleCategorizer();
        _modelStore = modelStore;
        _mlContext = new MLContext(seed: seed);
    }

    public async Task EnsureTrainedAsync(CancellationToken cancellationToken = default)
    {
        if (IsTrained && _predictionEngine != null) return;

        await LoadUserMemoryFromDatabaseAsync();

        if (_modelStore != null && PendingFeedbackCount == 0 && _modelStore.Exists())
        {
            await _trainLock.WaitAsync(cancellationToken);
            try
            {
                if (IsTrained && _predictionEngine != null) return;

                var loadedModel = _modelStore.TryLoad(_mlContext, out _);
                if (loadedModel != null)
                {
                    lock (_syncLock)
                    {
                        _model = loadedModel;
                        _predictionEngine = _mlContext.Model.CreatePredictionEngine<ExpenseTransactionData, CategoryPrediction>(_model);
                        IsTrained = true;
                        if (LastTrainingSampleCount == 0)
                        {
                            LastTrainingSampleCount = SeedTrainingData.GetDefaultDataset().Count;
                        }
                    }
                    return;
                }
            }
            finally
            {
                _trainLock.Release();
            }
        }

        await TrainAsync(cancellationToken: cancellationToken);
    }

    public async Task TrainAsync(
        IEnumerable<ExpenseTransactionData>? additionalData = null,
        CancellationToken cancellationToken = default)
    {
        await _trainLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dataset = SeedTrainingData.GetDefaultDataset();

            if (_databaseService != null)
            {
                await _databaseService.InitializeAsync();
                var persistedFeedback = await _databaseService.CategoryFeedbacks.GetAllAsync();
                foreach (var fb in persistedFeedback)
                {
                    CacheUserMemoryEntry(fb.RawDescription, fb.CategoryName);
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

            await FitModelDatasetAsync(dataset, cancellationToken);
        }
        finally
        {
            _trainLock.Release();
        }
    }

    public async Task TrainOnExactDatasetAsync(
        IEnumerable<ExpenseTransactionData> exactDataset,
        CancellationToken cancellationToken = default)
    {
        await _trainLock.WaitAsync(cancellationToken);
        try
        {
            var dataset = (exactDataset ?? Enumerable.Empty<ExpenseTransactionData>()).ToList();
            LastTrainingSampleCount = dataset.Count;
            await FitModelDatasetAsync(dataset, cancellationToken);
        }
        finally
        {
            _trainLock.Release();
        }
    }

    private async Task FitModelDatasetAsync(
        List<ExpenseTransactionData> dataset,
        CancellationToken cancellationToken)
    {
        var trainingDataView = _mlContext.Data.LoadFromEnumerable(dataset);

        var dataProcessPipeline = _mlContext.Transforms.Conversion.MapValueToKey(
                inputColumnName: nameof(ExpenseTransactionData.CategoryName),
                outputColumnName: "Label")
            .Append(_mlContext.Transforms.Text.FeaturizeText(
                inputColumnName: nameof(ExpenseTransactionData.Description),
                outputColumnName: "Features"))
            .AppendCacheCheckpoint(_mlContext);

        var trainer = _mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
            new Microsoft.ML.Trainers.SdcaMaximumEntropyMulticlassTrainer.Options
            {
                LabelColumnName = "Label",
                FeatureColumnName = "Features",
                NumberOfThreads = 1
            });

        var trainingPipeline = dataProcessPipeline
            .Append(trainer)
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue(
                inputColumnName: "PredictedLabel",
                outputColumnName: nameof(CategoryPrediction.PredictedCategory)));

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var newModel = trainingPipeline.Fit(trainingDataView);
            var newEngine = _mlContext.Model.CreatePredictionEngine<ExpenseTransactionData, CategoryPrediction>(newModel);

            lock (_syncLock)
            {
                _model = newModel;
                _predictionEngine = newEngine;
                IsTrained = true;
                PendingFeedbackCount = 0;
            }

            if (_modelStore != null)
            {
                _modelStore.Save(_mlContext, newModel, trainingDataView.Schema);
            }
        }, cancellationToken);
    }

    public CategoryPrediction Predict(string? description)
    {
        return Predict(
            description,
            confidenceThreshold: 0.25f,
            useRules: true,
            useUserMemory: true,
            useMlModel: true);
    }

    public CategoryPrediction Predict(
        string? description,
        float confidenceThreshold,
        bool useRules = true,
        bool useUserMemory = true,
        bool useMlModel = true)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return new CategoryPrediction
            {
                PredictedCategory = "Other",
                ExplicitConfidence = 0f,
                Source = PredictionSource.Fallback
            };
        }

        var trimmed = description.Trim();
        if (trimmed.Length > 1000)
        {
            trimmed = trimmed[..1000];
        }

        // Stage 1: User Memory (highest priority, confidence = 1.0)
        if (useUserMemory)
        {
            var rawKey = trimmed.ToLowerInvariant();
            if (_userMemory.TryGetValue(rawKey, out var rememberedCategory))
            {
                return new CategoryPrediction
                {
                    PredictedCategory = rememberedCategory,
                    ExplicitConfidence = 1.0f,
                    Source = PredictionSource.UserMemory
                };
            }

            var normalizedKey = _ruleCategorizer.NormalizeText(trimmed);
            if (!string.IsNullOrEmpty(normalizedKey) &&
                _userMemory.TryGetValue(normalizedKey, out var normRememberedCategory))
            {
                return new CategoryPrediction
                {
                    PredictedCategory = normRememberedCategory,
                    ExplicitConfidence = 1.0f,
                    Source = PredictionSource.UserMemory
                };
            }
        }

        // Stage 2: Deterministic Keyword Rules
        if (useRules)
        {
            var ruleMatch = _ruleCategorizer.Match(trimmed);
            if (ruleMatch != null && ruleMatch.Confidence >= confidenceThreshold)
            {
                return ruleMatch;
            }
        }

        // Stage 3: On-Device ML.NET SDCA Classifier (NEVER triggers blocking training!)
        if (useMlModel && IsTrained && _predictionEngine != null)
        {
            CategoryPrediction mlPrediction;
            lock (_syncLock)
            {
                var sample = new ExpenseTransactionData { Description = trimmed };
                mlPrediction = _predictionEngine.Predict(sample);
            }

            if (mlPrediction != null &&
                !string.IsNullOrWhiteSpace(mlPrediction.PredictedCategory) &&
                mlPrediction.Confidence >= confidenceThreshold)
            {
                mlPrediction.Source = PredictionSource.Model;
                return mlPrediction;
            }

            return new CategoryPrediction
            {
                PredictedCategory = "Other",
                ExplicitConfidence = mlPrediction?.Confidence ?? 0f,
                Source = PredictionSource.Fallback
            };
        }

        return new CategoryPrediction
        {
            PredictedCategory = "Other",
            ExplicitConfidence = 0f,
            Source = PredictionSource.Fallback
        };
    }

    public async Task RecordFeedbackAsync(
        string description,
        string confirmedCategory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(confirmedCategory))
        {
            return;
        }

        await PersistFeedbackCoreAsync(description, confirmedCategory);

        int pending;
        lock (_syncLock)
        {
            PendingFeedbackCount += 1;
            pending = PendingFeedbackCount;
        }

        if (pending >= Math.Max(1, RetrainBatchThreshold))
        {
            await TrainAsync(cancellationToken: cancellationToken);
        }
    }

    public async Task RetrainWithFeedbackAsync(string description, string confirmedCategory)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(confirmedCategory))
        {
            return;
        }

        await PersistFeedbackCoreAsync(description, confirmedCategory);
        await TrainAsync();
    }

    private async Task PersistFeedbackCoreAsync(string description, string confirmedCategory)
    {
        var rawDesc = description.Trim();
        var normDesc = rawDesc.ToLowerInvariant();
        var catName = confirmedCategory.Trim();

        CacheUserMemoryEntry(rawDesc, catName);

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
    }

    private async Task LoadUserMemoryFromDatabaseAsync()
    {
        if (_databaseService == null) return;

        await _databaseService.InitializeAsync();
        var feedbacks = await _databaseService.CategoryFeedbacks.GetAllAsync();
        foreach (var fb in feedbacks.OrderBy(f => f.UpdatedAt))
        {
            CacheUserMemoryEntry(fb.RawDescription, fb.CategoryName);
        }
    }

    private void CacheUserMemoryEntry(string rawDescription, string categoryName)
    {
        if (string.IsNullOrWhiteSpace(rawDescription) || string.IsNullOrWhiteSpace(categoryName))
            return;

        var rawKey = rawDescription.Trim().ToLowerInvariant();
        _userMemory[rawKey] = categoryName.Trim();

        var normalizedKey = _ruleCategorizer.NormalizeText(rawDescription);
        if (!string.IsNullOrWhiteSpace(normalizedKey))
        {
            _userMemory[normalizedKey] = categoryName.Trim();
        }
    }
}
