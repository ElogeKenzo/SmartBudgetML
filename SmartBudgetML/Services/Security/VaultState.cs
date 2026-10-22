namespace SmartBudgetML.Services.Security;

/// <summary>State of the encrypted database vault after an open attempt.</summary>
public enum VaultState
{
    /// <summary>The key is available and the database is open (or no key store is used).</summary>
    Ready,

    /// <summary>The secure storage could not be read or written. Nothing was written elsewhere.</summary>
    KeyUnavailable,

    /// <summary>An encrypted database exists but its key is missing or wrong. The file was not touched.</summary>
    KeyLost
}

/// <summary>Thrown by DatabaseService when the vault is not <see cref="VaultState.Ready"/>.</summary>
public sealed class VaultUnavailableException : Exception
{
    public VaultUnavailableException(VaultState state, Exception? innerException = null)
        : base($"Database vault is not available: {state}.", innerException)
    {
        State = state;
    }

    public VaultState State { get; }
}
