using System.Diagnostics;
using System.Globalization;
using Microsoft.ML;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Persists and reloads the trained ML.NET model transformer in AppDataDirectory
/// with schema version validation.
/// </summary>
public class FileModelStore : IModelStore
{
    public const int CurrentSchemaVersion = 2;
    public const string DefaultModelFileName = "smartbudget_model_v2.zip";

    public string ModelFilePath { get; }
    public int SchemaVersion { get; }

    private string MetaFilePath => ModelFilePath + ".meta";

    public FileModelStore(string? customModelPath = null, int schemaVersion = CurrentSchemaVersion)
    {
        ModelFilePath = customModelPath ?? GetDefaultModelPath();
        SchemaVersion = schemaVersion;
    }

    public bool Exists()
    {
        return File.Exists(ModelFilePath) && File.Exists(MetaFilePath);
    }

    public long GetModelFileSizeBytes()
    {
        if (!File.Exists(ModelFilePath)) return 0L;
        return new FileInfo(ModelFilePath).Length;
    }

    public void Save(MLContext mlContext, ITransformer model, DataViewSchema inputSchema)
    {
        if (mlContext == null) throw new ArgumentNullException(nameof(mlContext));
        if (model == null) throw new ArgumentNullException(nameof(model));

        var dir = Path.GetDirectoryName(ModelFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = ModelFilePath + ".tmp";
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            mlContext.Model.Save(model, inputSchema, fs);
        }

        File.Move(tempPath, ModelFilePath, overwrite: true);
        File.WriteAllText(MetaFilePath, SchemaVersion.ToString(CultureInfo.InvariantCulture));
    }

    public ITransformer? TryLoad(MLContext mlContext, out DataViewSchema? inputSchema)
    {
        inputSchema = null;
        if (!Exists()) return null;

        try
        {
            var metaText = File.ReadAllText(MetaFilePath).Trim();
            if (!int.TryParse(metaText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int storedVersion) ||
                storedVersion != SchemaVersion)
            {
                return null;
            }

            using var fs = new FileStream(ModelFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var loaded = mlContext.Model.Load(fs, out var schema);
            inputSchema = schema;
            return loaded;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[FileModelStore] Could not load model from '{ModelFilePath}': {ex.Message}");
            return null;
        }
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(ModelFilePath)) File.Delete(ModelFilePath);
            if (File.Exists(MetaFilePath)) File.Delete(MetaFilePath);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[FileModelStore] Could not delete model files: {ex.Message}");
        }
    }

    private static string GetDefaultModelPath()
    {
        try
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(basePath))
            {
                basePath = AppDomain.CurrentDomain.BaseDirectory;
            }
            return Path.Combine(basePath, DefaultModelFileName);
        }
        catch
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultModelFileName);
        }
    }
}
