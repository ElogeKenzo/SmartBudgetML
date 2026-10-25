namespace SmartBudgetML.Services.Database;

public enum BackupPasswordCheck
{
    Ok,
    TooShort,
    Mismatch
}

/// <summary>
/// Rules for the password that encrypts a backup (lot 8D). The password is never stored:
/// losing it makes the backup unreadable.
/// </summary>
public static class BackupPasswordPolicy
{
    public const int MinLength = 8;

    public static BackupPasswordCheck Validate(string? password, string? confirmation)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return BackupPasswordCheck.TooShort;
        }

        return string.Equals(password, confirmation, StringComparison.Ordinal)
            ? BackupPasswordCheck.Ok
            : BackupPasswordCheck.Mismatch;
    }
}
