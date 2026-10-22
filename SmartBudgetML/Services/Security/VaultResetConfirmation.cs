namespace SmartBudgetML.Services.Security;

public enum VaultResetStep
{
    Idle,
    FirstConfirmation,
    FinalConfirmation
}

/// <summary>
/// Double confirmation for the destructive vault reset on /vault-error:
/// tap 1 asks, tap 2 asks again, tap 3 allows the reset.
/// </summary>
public sealed class VaultResetConfirmation
{
    public VaultResetStep Step { get; private set; } = VaultResetStep.Idle;

    /// <summary>Moves to the next step; returns true only when the reset may run now.</summary>
    public bool Advance()
    {
        switch (Step)
        {
            case VaultResetStep.Idle:
                Step = VaultResetStep.FirstConfirmation;
                return false;
            case VaultResetStep.FirstConfirmation:
                Step = VaultResetStep.FinalConfirmation;
                return false;
            default:
                Step = VaultResetStep.Idle;
                return true;
        }
    }

    public void Cancel() => Step = VaultResetStep.Idle;
}
