using System.Text;

namespace SmartBudgetML.Services.Data;

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

    public async Task<string> WriteExportFileAsync(string fileName, string content, Encoding? encoding = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name must not be empty.", nameof(fileName));
        }

        if (!Directory.Exists(_exportDirectory))
        {
            Directory.CreateDirectory(_exportDirectory);
        }

        var safeFileName = Path.GetFileName(fileName);
        var fullPath = Path.Combine(_exportDirectory, safeFileName);
        var targetEncoding = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        await File.WriteAllTextAsync(fullPath, content ?? string.Empty, targetEncoding);

        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            throw new IOException($"Export verification failed: file '{fullPath}' was not written to disk.");
        }

        return fullPath;
    }
}
