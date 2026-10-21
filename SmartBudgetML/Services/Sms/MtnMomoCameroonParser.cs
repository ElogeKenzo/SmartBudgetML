using System.Globalization;
using System.Text.RegularExpressions;
using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Sms;

/// <summary>
/// Parses real MTN Mobile Money (MoMo) Cameroon SMS messages, including messages with promotional
/// headers/footers ("1Go Njoh!...", "Emprunte jusqu'à 100 000 FCFA...", "C'est la Rentrée...MoMo Te Gère !"),
/// covering incoming transfers, bundle/airtime purchases, merchant payments, and cash withdrawals.
/// </summary>
public class MtnMomoCameroonParser : IMobileMoneySmsParser
{
    public string OperatorName => "MTN MoMo";

    private static readonly Regex TxIdRegex = new(
        @"(?i)(?:Identifiant\s+de\s+transaction\s+financiere|Financial\s+Transaction\s+Id|Transaction\s+ID)\s*:\s*(\d{8,15})",
        RegexOptions.Compiled);

    private static readonly Regex TimestampRegex = new(
        @"(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})",
        RegexOptions.Compiled);

    private static readonly Regex BalanceRegex = new(
        @"(?i)Votre\s+nouveau\s+solde(?:\s+est\s+de)?\s*:\s*([0-9]+(?:[\.\,\s][0-9]+)*)\s*(?:XAF|FCFA|CFA)",
        RegexOptions.Compiled);

    private static readonly Regex BalanceNoColonRegex = new(
        @"(?i)Votre\s+nouveau\s+solde\s+est\s+de\s+([0-9]+(?:[\.\,\s][0-9]+)*)\s*(?:XAF|FCFA|CFA)",
        RegexOptions.Compiled);

    private static readonly Regex FeeStandardRegex = new(
        @"(?i)(?:Les\s+frais\s+s'elevaient\s+a|Frais\s*:)\s*(?:FCFA\s*|XAF\s*)?([0-9]+(?:[\.\,][0-9]+)?)\s*(?:XAF|FCFA|CFA)?",
        RegexOptions.Compiled);

    private static readonly Regex ReceivedRegex = new(
        @"(?i)Vous\s+avez\s+recu\s+(?<amount>[0-9]+(?:[\.\,\s][0-9]+)*)\s*(?:XAF|FCFA)\s+de\s+(?<sender>.+?)\s+sur\s+votre\s+compte\s+mobile\s+money",
        RegexOptions.Compiled);

    private static readonly Regex BundleDebitRegex = new(
        @"(?i)Une\s+transaction\s+de\s+(?<amount>[0-9]+(?:[\.\,\s][0-9]+)*)\s*(?:XAF|FCFA)\s+effectuee\s+par\s+(?<merchant>.+?)\s+sur\s+votre\s+compte\s+d'argent\s+mobile\s+s'est\s+terminee\s+avec\s+succes",
        RegexOptions.Compiled);

    private static readonly Regex MerchantPaymentRegex = new(
        @"(?i)Votre\s+paiement\s+de\s+(?<amount>[0-9]+(?:[\.\,\s][0-9]+)*)\s*(?:XAF|FCFA)\s+a\s+(?<merchant>.+?)\s+a\s+ete\s+finalise",
        RegexOptions.Compiled);

    private static readonly Regex WithdrawalRegex = new(
        @"(?i)Vous\s+avez\s+effectue\s+avec\s+succes\s+le\s+retrait\s+de\s+(?<amount>[0-9]+(?:[\.\,\s][0-9]+)*)\s*(?:XAF|FCFA)\s+de\s+votre\s+compte\s+mobile\s+money\s+chez\s+(?<agent>.+?)\s+le\s+\d{4}-\d{2}-\d{2}",
        RegexOptions.Compiled);

    private static readonly Regex TransferSentRegex = new(
        @"(?i)(?:Vous\s+avez\s+transfere|Transfert\s+de)\s+(?<amount>[0-9]+(?:[\.\,\s][0-9]+)*)\s*(?:XAF|FCFA)\s+(?:effectue\s+avec\s+succes\s+)?(?:a|vers)\s+(?<recipient>.+?)\s+(?:le|a)\s+\d{4}-\d{2}-\d{2}",
        RegexOptions.Compiled);

    public bool CanParse(string smsBody)
    {
        if (string.IsNullOrWhiteSpace(smsBody)) return false;
        return TxIdRegex.IsMatch(smsBody) &&
               (smsBody.Contains("mobile money", StringComparison.OrdinalIgnoreCase) ||
                smsBody.Contains("d'argent mobile", StringComparison.OrdinalIgnoreCase) ||
                smsBody.Contains("Votre paiement de", StringComparison.OrdinalIgnoreCase));
    }

    public bool TryParse(string smsBody, out ParsedMobileMoneyTransaction? transaction)
    {
        transaction = null;
        if (!CanParse(smsBody)) return false;

        var trimmed = smsBody.Trim();
        var txIdMatch = TxIdRegex.Match(trimmed);
        if (!txIdMatch.Success) return false;

        var txId = txIdMatch.Groups[1].Value.Trim();

        DateTime date = DateTime.UtcNow;
        var tsMatch = TimestampRegex.Match(trimmed);
        if (tsMatch.Success &&
            DateTime.TryParseExact(tsMatch.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDate))
        {
            date = parsedDate;
        }

        decimal fee = 0m;
        var feeMatch = FeeStandardRegex.Match(trimmed);
        if (feeMatch.Success)
        {
            MobileMoneyAmountParser.TryParseAmountXaf(feeMatch.Groups[1].Value, out fee);
        }

        decimal? balanceAfter = null;
        var balMatch = BalanceRegex.Match(trimmed);
        if (!balMatch.Success)
        {
            balMatch = BalanceNoColonRegex.Match(trimmed);
        }
        if (balMatch.Success && MobileMoneyAmountParser.TryParseAmountXaf(balMatch.Groups[1].Value, out var parsedBal))
        {
            balanceAfter = parsedBal;
        }

        MobileMoneyTransactionType type;
        decimal amount;
        string? counterparty;

        if (ReceivedRegex.Match(trimmed) is { Success: true } recvMatch &&
            MobileMoneyAmountParser.TryParseAmountXaf(recvMatch.Groups["amount"].Value, out amount))
        {
            type = MobileMoneyTransactionType.TransferReceived;
            counterparty = recvMatch.Groups["sender"].Value.Trim();
        }
        else if (BundleDebitRegex.Match(trimmed) is { Success: true } bundleMatch &&
                 MobileMoneyAmountParser.TryParseAmountXaf(bundleMatch.Groups["amount"].Value, out amount))
        {
            counterparty = Regex.Replace(bundleMatch.Groups["merchant"].Value.Trim(), @"\s+", " ");
            type = counterparty.Contains("BUNDLE", StringComparison.OrdinalIgnoreCase) ||
                   counterparty.Contains("FORFAIT", StringComparison.OrdinalIgnoreCase)
                ? MobileMoneyTransactionType.AirtimeData
                : MobileMoneyTransactionType.MerchantPayment;
        }
        else if (MerchantPaymentRegex.Match(trimmed) is { Success: true } payMatch &&
                 MobileMoneyAmountParser.TryParseAmountXaf(payMatch.Groups["amount"].Value, out amount))
        {
            type = MobileMoneyTransactionType.MerchantPayment;
            counterparty = payMatch.Groups["merchant"].Value.Trim();
        }
        else if (WithdrawalRegex.Match(trimmed) is { Success: true } witMatch &&
                 MobileMoneyAmountParser.TryParseAmountXaf(witMatch.Groups["amount"].Value, out amount))
        {
            type = MobileMoneyTransactionType.Withdrawal;
            counterparty = witMatch.Groups["agent"].Value.Trim();
        }
        else if (TransferSentRegex.Match(trimmed) is { Success: true } sentMatch &&
                 MobileMoneyAmountParser.TryParseAmountXaf(sentMatch.Groups["amount"].Value, out amount))
        {
            type = MobileMoneyTransactionType.TransferSent;
            counterparty = sentMatch.Groups["recipient"].Value.Trim();
        }
        else
        {
            return false;
        }

        transaction = new ParsedMobileMoneyTransaction
        {
            Operator = OperatorName,
            Type = type,
            Amount = amount,
            Fee = fee,
            BalanceAfter = balanceAfter,
            Counterparty = counterparty,
            Reference = txId,
            Date = date,
            RawMessage = trimmed
        };

        return true;
    }
}
