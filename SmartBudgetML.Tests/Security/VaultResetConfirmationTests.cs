using SmartBudgetML.Services.Security;
using Xunit;

namespace SmartBudgetML.Tests.Security;

/// <summary>Vault reset erases all data: it needs two explicit confirmations after the first tap.</summary>
public class VaultResetConfirmationTests
{
    [Fact]
    public void Reset_IsAllowedOnlyOnTheThirdTap()
    {
        var c = new VaultResetConfirmation();
        Assert.Equal(VaultResetStep.Idle, c.Step);

        Assert.False(c.Advance());
        Assert.Equal(VaultResetStep.FirstConfirmation, c.Step);

        Assert.False(c.Advance());
        Assert.Equal(VaultResetStep.FinalConfirmation, c.Step);

        Assert.True(c.Advance());
    }

    [Fact]
    public void Cancel_ReturnsToIdleFromAnyStep()
    {
        var c = new VaultResetConfirmation();
        c.Advance();
        c.Advance();

        c.Cancel();

        Assert.Equal(VaultResetStep.Idle, c.Step);
        Assert.False(c.Advance());
    }
}
