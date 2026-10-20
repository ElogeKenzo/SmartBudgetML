using System.Text;

namespace SmartBudgetML.Services.Data;

/// <summary>
/// Platform-agnostic file export service that writes exported content to disk
/// and verifies the output file exists.
/// </summary>
public interface IFileExportService
{
    Task<string> WriteExportFileAsync(string fileName, string content, Encoding? encoding = null);
}
