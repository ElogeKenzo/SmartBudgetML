using Microsoft.ML.Data;

namespace SmartBudgetML.Services.ML.Models;

/// <summary>
/// ML.NET output prediction schema for multi-class NLP categorization.
/// </summary>
public class CategoryPrediction
{
    [ColumnName("PredictedCategory")]
    public string PredictedCategory { get; set; } = string.Empty;

    [ColumnName("Score")]
    public float[] Score { get; set; } = Array.Empty<float>();

    /// <summary>
    /// Highest normalized confidence score among all evaluated categories (0.0 to 1.0).
    /// </summary>
    public float Confidence
    {
        get
        {
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
