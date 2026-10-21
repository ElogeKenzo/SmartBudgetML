using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Sms;

public interface IMobileMoneySmsParser
{
    string OperatorName { get; }
    bool CanParse(string smsBody);
    bool TryParse(string smsBody, out ParsedMobileMoneyTransaction? transaction);
}

public interface ISmsParserRegistry
{
    void RegisterParser(IMobileMoneySmsParser parser);
    ParsedMobileMoneyTransaction? Parse(string smsBody);
    IReadOnlyList<ParsedMobileMoneyTransaction> ParseBatch(string batchText);
    bool IsDuplicate(ParsedMobileMoneyTransaction candidate, IEnumerable<ParsedMobileMoneyTransaction> existingTransactions);
}
