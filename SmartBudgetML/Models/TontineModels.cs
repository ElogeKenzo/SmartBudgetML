using SQLite;

namespace SmartBudgetML.Models;

public enum TontineFrequency
{
    Weekly,
    BiWeekly,
    Monthly
}

[Table("Tontines")]
public class Tontine : IEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public decimal ContributionAmount { get; set; }

    public TontineFrequency Frequency { get; set; } = TontineFrequency.Monthly;

    public int TotalMembers { get; set; }

    public int MyTurnOrder { get; set; } = 1;

    public DateTime StartDate { get; set; } = DateTime.Today;

    public bool IsActive { get; set; } = true;
}

[Table("TontineMembers")]
public class TontineMember : IEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int TontineId { get; set; }

    [NotNull, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int TurnOrder { get; set; }

    public bool IsCurrentUser { get; set; }

    public bool IsWithdrawn { get; set; }
}

[Table("TontineRounds")]
public class TontineRound : IEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int TontineId { get; set; }

    public int RoundNumber { get; set; }

    public int BeneficiaryMemberId { get; set; }

    public DateTime ExpectedDate { get; set; }

    public decimal ExpectedAmount { get; set; }

    public bool IsPaidOut { get; set; }
}

[Table("TontineContributions")]
public class TontineContribution : IEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int TontineId { get; set; }

    public int RoundNumber { get; set; }

    public int MemberId { get; set; }

    public decimal Amount { get; set; }

    public DateTime Date { get; set; }

    public bool IsPaid { get; set; }

    public bool IsLate { get; set; }
}
