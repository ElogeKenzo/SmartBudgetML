namespace SmartBudgetML.Models;

public enum MobileMoneyTransactionType
{
    Deposit,
    Withdrawal,
    TransferSent,
    TransferReceived,
    MerchantPayment,
    AirtimeData,
    FeesOnly,
    Unknown
}

public class ParsedMobileMoneyTransaction
{
    public string Operator { get; set; } = string.Empty;
    public MobileMoneyTransactionType Type { get; set; } = MobileMoneyTransactionType.Unknown;
    public decimal Amount { get; set; }
    public decimal Fee { get; set; } = 0m;
    public decimal? BalanceAfter { get; set; }
    public string? Counterparty { get; set; }
    public string? Reference { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string RawMessage { get; set; } = string.Empty;
    public bool IsIncome => Type == MobileMoneyTransactionType.TransferReceived || Type == MobileMoneyTransactionType.Deposit;
}
