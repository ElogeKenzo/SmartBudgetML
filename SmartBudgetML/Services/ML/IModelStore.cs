using Microsoft.ML;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Persists and reloads the trained ML.NET model transformer along with schema version metadata.
/// </summary>
public interface IModelStore
{
    string ModelFilePath { get; }
    int SchemaVersion { get; }
    bool Exists();
    long GetModelFileSizeBytes();
    void Save(MLContext mlContext, ITransformer model, DataViewSchema inputSchema);
    ITransformer? TryLoad(MLContext mlContext, out DataViewSchema? inputSchema);
    void Delete();
}
