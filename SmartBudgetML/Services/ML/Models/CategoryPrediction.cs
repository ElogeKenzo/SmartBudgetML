using Microsoft.ML.Data;

namespace SmartBudgetML.Services.ML.Models;

/// <summary>
/// Output prediction schema for the 3-stage hybrid categorization pipeline
/// (Stage 1: UserMemory, Stage 2: Keyword Rules, Stage 3: ML.NET SDCA).
/// </summary>
public class CategoryPrediction
{
    [ColumnName("PredictedCategory")]
    public string PredictedCategory { get; set; } = string.Empty;

    [ColumnName("Score")]
    public float[] Score { get; set; } = Array.Empty<float>();

    /// <summary>
    /// Alias matching the PredictionResult specification (CategoryName, Confidence, Source).
    /// </summary>
    [NoColumn]
    public string CategoryName
    {
        get => PredictedCategory;
        set => PredictedCategory = value;
    }

    /// <summary>
    /// Optional explicit confidence score set by Stage 1 (UserMemory = 1.0) or Stage 2 (Rules).
    /// When null, Confidence is computed from the ML.NET Score vector.
    /// </summary>
    [NoColumn]
    public float? ExplicitConfidence { get; set; }

    /// <summary>
    /// Pipeline stage that produced this prediction.
    /// </summary>
    [NoColumn]
    public PredictionSource Source { get; set; } = PredictionSource.Model;

    /// <summary>
    /// When Source == PredictionSource.Rules, holds the matched keyword or rule token
    /// (e.g. "moto", "eneo", "njangi") for transparent UI explanation.
    /// </summary>
    [NoColumn]
    public string? MatchedRuleKeyword { get; set; }

    /// <summary>
    /// Highest normalized confidence score among all evaluated categories (0.0 to 1.0).
    /// </summary>
    [NoColumn]
    public float Confidence
    {
        get
        {
            if (ExplicitConfidence.HasValue)
            {
                return Math.Clamp(ExplicitConfidence.Value, 0f, 1f);
            }

            if (Score == null || Score.Length == 0) return 0f;

            // Compute softmax if scores are unnormalized logits, or take max
            var maxScore = Score.Max();
            if (maxScore > 1.0f || Score.Any(s => s < 0))
            {
                // Convert logits to softmax probabilities
                var expScores = Score.Select(s => MathF.Exp(s - maxScore)).ToArray();
                var sumExp = expScores.Sum();
                return sumExp > 0 ? expScores.Max() / sumExp : 0f;
            }

            return Math.Clamp(maxScore, 0f, 1f);
        }
    }
}
