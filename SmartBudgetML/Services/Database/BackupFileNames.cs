using System.Globalization;

namespace SmartBudgetML.Services.Database;

public static class BackupFileNames
{
    public const string Extension = ".sbbackup";

    /// <summary>smartbudget-YYYYMMDD.sbbackup</summary>
    public static string ForDate(DateTime date) =>
        $"smartbudget-{date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}{Extension}";
}
