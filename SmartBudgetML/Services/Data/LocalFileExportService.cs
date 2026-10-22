using System.Text;

namespace SmartBudgetML.Services.Data;

/// <summary>
/// Writes exports to a private folder. Sharing needs the platform share sheet:
/// see <c>MauiFileExportService</c> in the app.
/// </summary>
public class LocalFileExportService : IFileExportService
{
    private readonly string _exportDirectory;

    public LocalFileExportService(string? exportDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(exportDirectory))
        {
            _exportDirectory = exportDirectory;
        }
        else
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                baseDir = AppDomain.CurrentDomain.BaseDirectory;
            }
            _exportDirectory = Path.Combine(baseDir, "exports");
        }
    }

    public string ExportDirectory => _exportDirectory;

    public async Task<string> WriteExportFileAsync(string fileName, string content, Encoding? encoding = null)
    {
        var fullPath = PrepareExportPath(fileName);
        var targetEncoding = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        await File.WriteAllTextAsync(fullPath, content ?? string.Empty, targetEncoding);

        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            throw new IOException($"Export verification failed: file '{fullPath}' was not written to disk.");
        }

        return fullPath;
    }

    public string PrepareExportPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name must not be empty.", nameof(fileName));
        }

        Directory.CreateDirectory(_exportDirectory);

        // Exports hold personal data: keep only the file about to be shared.
        foreach (var previous in Directory.GetFiles(_exportDirectory))
        {
            File.Delete(previous);
        }

        return Path.Combine(_exportDirectory, Path.GetFileName(fileName));
    }

    public virtual Task ShareFileAsync(string path, string title) =>
        throw new PlatformNotSupportedException("Sharing a file needs the platform share sheet.");
}
