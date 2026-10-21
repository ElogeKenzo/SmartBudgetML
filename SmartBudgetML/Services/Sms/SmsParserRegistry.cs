using System.Globalization;
using System.Text.RegularExpressions;
using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Sms;

/// <summary>
/// Parses XAF / FCFA amounts formatted with varied thousand separators (dots, spaces, non-breaking spaces, commas)
/// such as "1.500", "1 500", "1,500", "1500 FCFA", or "150.000 XAF".
/// </summary>
public static class MobileMoneyAmountParser
{
    public static bool TryParseAmountXaf(string? rawAmount, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(rawAmount))
        {
            return false;
        }

        // Strip currency labels and whitespace
        var cleaned = Regex.Replace(rawAmount, @"(?i)\b(fcfa|cfa|xaf|f)\b", string.Empty).Trim();

        // Remove thousand separators used in XAF SMS ('.', ',', spaces, narrow no-break spaces)
        // When followed by exactly 3 digits (e.g. 1.500 or 150,000 or 25 000)
        cleaned = Regex.Replace(cleaned, @"[\.\,\s\u00A0\u202F](?=\d{3}(\D|$))", string.Empty);

        // Remove any remaining spaces
        cleaned = Regex.Replace(cleaned, @"[\s\u00A0\u202F]+", string.Empty);

        // Normalize decimal comma if any remains
        cleaned = cleaned.Replace(',', '.');

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount >= 0m;
    }
}

/// <summary>
/// Registry of Mobile Money SMS parsers (Orange Money, MTN MoMo, etc.) with batch parsing
/// and strict deduplication by Reference (TxID) or (Date ±2 minutes, Amount, Operator).
/// </summary>
public class SmsParserRegistry : ISmsParserRegistry
{
    private readonly List<IMobileMoneySmsParser> _parsers = new();

    public SmsParserRegistry()
    {
        RegisterParser(new OrangeMoneyCameroonParser());
        RegisterParser(new MtnMomoCameroonParser());
    }

    public void RegisterParser(IMobileMoneySmsParser parser)
    {
        if (parser == null) throw new ArgumentNullException(nameof(parser));
        _parsers.RemoveAll(p => string.Equals(p.OperatorName, parser.OperatorName, StringComparison.OrdinalIgnoreCase));
        _parsers.Add(parser);
    }

    public ParsedMobileMoneyTransaction? Parse(string smsBody)
    {
        if (string.IsNullOrWhiteSpace(smsBody))
        {
            return null;
        }

        var trimmed = smsBody.Trim();
        foreach (var parser in _parsers)
        {
            try
            {
                if (parser.CanParse(trimmed) && parser.TryParse(trimmed, out var tx) && tx != null)
                {
                    return tx;
                }
            }
            catch
            {
                // Never throw on malformed SMS input
            }
        }

        return null;
    }

    public IReadOnlyList<ParsedMobileMoneyTransaction> ParseBatch(string batchText)
    {
        if (string.IsNullOrWhiteSpace(batchText))
        {
            return Array.Empty<ParsedMobileMoneyTransaction>();
        }

        var blocks = Regex.Split(batchText.Trim(), @"(\r?\n){2,}|\r?\n")
            .Select(b => b.Trim())
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .ToList();

        var results = new List<ParsedMobileMoneyTransaction>();
        foreach (var block in blocks)
        {
            var parsed = Parse(block);
            if (parsed != null && !IsDuplicate(parsed, results))
            {
                results.Add(parsed);
            }
        }

        return results;
    }

    public bool IsDuplicate(ParsedMobileMoneyTransaction candidate, IEnumerable<ParsedMobileMoneyTransaction> existingTransactions)
    {
        if (candidate == null) return false;

        foreach (var existing in existingTransactions)
        {
            if (!string.IsNullOrWhiteSpace(candidate.Reference) &&
                !string.IsNullOrWhiteSpace(existing.Reference) &&
                string.Equals(candidate.Reference.Trim(), existing.Reference.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(candidate.Operator, existing.Operator, StringComparison.OrdinalIgnoreCase) &&
                candidate.Amount == existing.Amount &&
                Math.Abs((candidate.Date - existing.Date).TotalMinutes) <= 2.0)
            {
                return true;
            }
        }

        return false;
    }
}
