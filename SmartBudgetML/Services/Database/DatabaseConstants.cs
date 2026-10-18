using SQLite;

namespace SmartBudgetML.Services.Database;

/// <summary>
/// Centralized database configuration and flags for local SQLite.
/// Works cross-platform on Android, iOS, Windows, Mac, and in unit test environments.
/// </summary>
public static class DatabaseConstants
{
    public const string DatabaseFilename = "smartbudget.db3";

    public const SQLiteOpenFlags Flags =
        SQLiteOpenFlags.ReadWrite |
        SQLiteOpenFlags.Create |
        SQLiteOpenFlags.SharedCache;

    /// <summary>
    /// Gets the platform-appropriate local application data path.
    /// Compatible with both .NET MAUI devices and headless xUnit test runners.
    /// </summary>
    public static string DatabasePath
    {
        get
        {
            try
            {
                var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(basePath))
                {
                    basePath = AppDomain.CurrentDomain.BaseDirectory;
                }
                return Path.Combine(basePath, DatabaseFilename);
            }
            catch
            {
                var basePath = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(basePath, DatabaseFilename);
            }
        }
    }
}
