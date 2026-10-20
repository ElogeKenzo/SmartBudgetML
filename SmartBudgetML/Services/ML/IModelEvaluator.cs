using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

public record ModelEvaluationMetrics(
    bool IsEvaluated,
    double MicroAccuracy,
    double MacroAccuracy,
    double MacroF1,
    int TrainingSampleCount,
    int ValidationSampleCount,
    DateTime? EvaluatedAtUtc,
    string StatusDisplay)
{
    public static ModelEvaluationMetrics Unevaluated(int trainingSampleCount = 0) => new(
        IsEvaluated: false,
        MicroAccuracy: 0.0,
        MacroAccuracy: 0.0,
        MacroF1: 0.0,
        TrainingSampleCount: trainingSampleCount,
        ValidationSampleCount: 0,
        EvaluatedAtUtc: null,
        StatusDisplay: "Non évalué");
}

public interface IModelEvaluator
{
    ModelEvaluationMetrics GetCurrentMetrics();

    Task<ModelEvaluationMetrics> EvaluateAsync(
        IEnumerable<ExpenseTransactionData>? trainingSamples = null,
        IEnumerable<ExpenseTransactionData>? validationSamples = null);
}
