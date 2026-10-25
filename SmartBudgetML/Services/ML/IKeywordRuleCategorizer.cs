using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Stage 2 deterministic keyword and phrase categorizer with text normalization
/// (diacritics removal, lowercase, amount/number stripping, stopword filtering).
/// </summary>
public interface IKeywordRuleCategorizer
{
    string NormalizeText(string? input);
    CategoryPrediction? Match(string? description);
}
