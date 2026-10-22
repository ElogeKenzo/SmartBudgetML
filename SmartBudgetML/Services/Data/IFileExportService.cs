using System.Text;

namespace SmartBudgetML.Services.Data;

/// <summary>
/// Platform-agnostic file export service: writes exported content to a private export folder,
/// verifies the output file exists, and hands it to the system share sheet so the user can save it.
/// </summary>
public interface IFileExportService
{
    /// <summary>Folder holding the file being shared. Only the latest export is kept there.</summary>
    string ExportDirectory { get; }

    Task<string> WriteExportFileAsync(string fileName, string content, Encoding? encoding = null);

    /// <summary>Returns a path in <see cref="ExportDirectory"/> after removing the previous export.</summary>
    string PrepareExportPath(string fileName);

    /// <summary>
    /// Opens the share sheet. It does not report whether the user saved the file, so the
    /// screen must never claim the file was exported.
    /// </summary>
    Task ShareFileAsync(string path, string title);
}
