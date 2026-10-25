namespace SmartBudgetML.Services.Database;

public enum BackupValidationStatus
{
    Valid,

    /// <summary>Wrong password or damaged file: SQLCipher cannot tell the two apart.</summary>
    Unreadable,

    /// <summary>Readable, but not a SmartBudget backup (missing tables or failed integrity check).</summary>
    NotABackup,

    /// <summary>Made by a newer version of the app (user_version above the current schema).</summary>
    TooNew
}

/// <summary>Thrown when a restore is refused; the current database is left untouched.</summary>
public sealed class BackupRestoreException : Exception
{
    public BackupRestoreException(BackupValidationStatus status)
        : base($"Backup cannot be restored: {status}.")
    {
        Status = status;
    }

    public BackupRestoreException(BackupValidationStatus status, Exception innerException)
        : base($"Backup cannot be restored: {status}.", innerException)
    {
        Status = status;
    }

    public BackupValidationStatus Status { get; }
}
