using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// On-device 3-stage hybrid expense categorization service:
/// Stage 1: UserMemory (CategoryFeedback)
/// Stage 2: Deterministic Keyword Rules (IKeywordRuleCategorizer)
/// Stage 3: On-device ML.NET SDCA Maximum Entropy classifier with confidence threshold.
/// </summary>
public interface IExpenseCategorizerService
{
    bool IsTrained { get; }
    int LastTrainingSampleCount { get; }
    int PendingFeedbackCount { get; }
    int RetrainBatchThreshold { get; set; }

    Task EnsureTrainedAsync(CancellationToken cancellationToken = default);
    Task TrainAsync(IEnumerable<ExpenseTransactionData>? additionalData = null, CancellationToken cancellationToken = default);

    CategoryPrediction Predict(string? description);
    CategoryPrediction Predict(
        string? description,
        float confidenceThreshold,
        bool useRules = true,
        bool useUserMemory = true,
        bool useMlModel = true);

    Task RecordFeedbackAsync(
        string description,
        string confirmedCategory,
        CancellationToken cancellationToken = default);

    Task RetrainWithFeedbackAsync(string description, string confirmedCategory);
}
