namespace SmartBudgetML.Services.Data;

/// <summary>
/// Writes exports to <see cref="FileSystem.CacheDirectory"/> and opens the system share sheet,
/// from which the user picks where to save the file (Drive, Files, WhatsApp...). No storage
/// permission is needed.
/// </summary>
public sealed class MauiFileExportService : LocalFileExportService
{
    public MauiFileExportService()
        : base(Path.Combine(FileSystem.CacheDirectory, "exports"))
    {
    }

    public override Task ShareFileAsync(string path, string title) =>
        Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File = new ShareFile(path)
        });
}
