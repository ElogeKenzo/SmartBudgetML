namespace SmartBudgetML.Services.ML.Models;

/// <summary>
/// Identifies which stage of the 3-stage hybrid categorization pipeline produced the prediction.
/// </summary>
public enum PredictionSource
{
    /// <summary>
    /// No prediction or below confidence threshold.
    /// </summary>
    None = 0,

    /// <summary>
    /// Stage 1: Exact or normalized match from user's historical CategoryFeedback memory.
    /// </summary>
    UserMemory = 1,

    /// <summary>
    /// Stage 2: Deterministic keyword/phrase rule match (Cameroonian FR/EN/Pidgin dictionary).
    /// </summary>
    Rules = 2,

    /// <summary>
    /// Stage 3: On-device ML.NET SDCA Maximum Entropy classifier.
    /// </summary>
    Model = 3,

    /// <summary>
    /// Default fallback category when input is empty or below threshold.
    /// </summary>
    Fallback = 4
}
