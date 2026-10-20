using System.Diagnostics;
using SQLite;

namespace SmartBudgetML.Services.Database;

/// <summary>
/// Centralized database configuration and flags for local SQLite / SQLCipher.
/// Works cross-platform on Android, Windows, and in unit test environments.
/// Does not use SharedCache so SQLCipher key verification is enforced per connection.
/// </summary>
public static class DatabaseConstants
{
    public const string DatabaseFilename = "smartbudget.db3";

    public const SQLiteOpenFlags Flags =
        SQLiteOpenFlags.ReadWrite |
        SQLiteOpenFlags.Create |
        SQLiteOpenFlags.FullMutex;

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
            catch (Exception ex)
            {
                Trace.TraceWarning($"[DatabaseConstants] Fallback to BaseDirectory: {ex.Message}");
                var basePath = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(basePath, DatabaseFilename);
            }
        }
    }
}
