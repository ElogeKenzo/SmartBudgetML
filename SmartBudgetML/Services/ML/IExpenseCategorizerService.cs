using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// On-device Natural Language Processing (NLP) service for expense classification.
/// Trains and evaluates a multi-class SDCA Maximum Entropy classifier locally.
/// </summary>
public interface IExpenseCategorizerService
{
    bool IsTrained { get; }
    int LastTrainingSampleCount { get; }
    Task EnsureTrainedAsync();
    Task TrainAsync(IEnumerable<ExpenseTransactionData>? additionalData = null);
    CategoryPrediction Predict(string description);
    Task RetrainWithFeedbackAsync(string description, string confirmedCategory);
}
