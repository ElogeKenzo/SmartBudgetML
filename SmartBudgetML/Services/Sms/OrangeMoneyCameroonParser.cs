using System.Globalization;
using System.Text.RegularExpressions;
using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Sms;

/// <summary>
/// Parses real Orange Money Cameroon SMS messages (French & English formats)
/// covering transfers (PP...), deposits (CI...), merchant/bundle payments (MP...), and withdrawals (CO...).
/// </summary>
public class OrangeMoneyCameroonParser : IMobileMoneySmsParser
{
    public string OperatorName => "Orange Money";

    private static readonly Regex TxIdRegex = new(
        @"(?i)(?:ID\s+transaction|Transaction\s+ID)\s*:\s*([A-Z]{2}\d{4,6}\.\d{4}\.[A-Z0-9]+)",
        RegexOptions.Compiled);

    private static readonly Regex AmountRegex = new(
        @"(?i)(?:Montant(?:\s+de)?\s+Transaction|Transaction\s+amount|Montant|Amount)\s*:\s*([0-9]+(?:[\.\,\s][0-9]+)*)\s*(?:FCFA|XAF|CFA)?",
        RegexOptions.Compiled);

    private static readonly Regex FeeRegex = new(
        @"(?i)(?:Frais|Charges)\s*:\s*([0-9]+(?:[\.\,][0-9]+)?)\s*(?:FCFA|XAF|CFA)?",
        RegexOptions.Compiled);

    private static readonly Regex BalanceRegex = new(
        @"(?i)(?:Nouveau\s+Solde|New\s+balance|Solde)\s*:\s*([0-9]+(?:[\.\,\s][0-9]+)*)\s*(?:FCFA|XAF|CFA)?",
        RegexOptions.Compiled);

    private static readonly Regex FrTransferRegex = new(
        @"(?i)^Transfert\s+de\s+(?<from>.+?)\s+vers\s+(?<to>.+?)\s+reussi\b",
        RegexOptions.Compiled);

    private static readonly Regex EnTransferRegex = new(
        @"(?i)^Successful\s+transfer\s+from\s+(?<from>.+?)\s+to\s+(?<to>.+?)\.",
        RegexOptions.Compiled);

    private static readonly Regex DepositRegex = new(
        @"(?i)^Depot\s+effectue\s+par\s+(?<from>.+?)\s+(?:to|vers)\s+(?<to>.+?)\.",
        RegexOptions.Compiled);

    private static readonly Regex FrPaymentRegex = new(
        @"(?i)^Paiement\s+de\s+(?<merchant>.+?)\s+reussi\s+par\s+(?<payer>.+?)\.",
        RegexOptions.Compiled);

    private static readonly Regex EnPaymentRegex = new(
        @"(?i)^Successful\s+Payment\s+of\s+(?<merchant>.+?)\s+from\s+(?<payer>.+?)\.",
        RegexOptions.Compiled);

    private static readonly Regex WithdrawalRegex = new(
        @"(?i)^Retrait\s+effectue|Cash\s*out",
        RegexOptions.Compiled);

    public bool CanParse(string smsBody)
    {
        if (string.IsNullOrWhiteSpace(smsBody)) return false;
        return TxIdRegex.IsMatch(smsBody);
    }

    public bool TryParse(string smsBody, out ParsedMobileMoneyTransaction? transaction)
    {
        transaction = null;
        if (!CanParse(smsBody)) return false;

        var trimmed = smsBody.Trim();
        var idMatch = TxIdRegex.Match(trimmed);
        if (!idMatch.Success) return false;

        var txId = idMatch.Groups[1].Value.Trim();

        var amountMatch = AmountRegex.Match(trimmed);
        if (!amountMatch.Success || !MobileMoneyAmountParser.TryParseAmountXaf(amountMatch.Groups[1].Value, out var amount))
        {
            return false;
        }

        decimal fee = 0m;
        var feeMatch = FeeRegex.Match(trimmed);
        if (feeMatch.Success)
        {
            MobileMoneyAmountParser.TryParseAmountXaf(feeMatch.Groups[1].Value, out fee);
        }

        decimal? balanceAfter = null;
        var balMatch = BalanceRegex.Match(trimmed);
        if (balMatch.Success && MobileMoneyAmountParser.TryParseAmountXaf(balMatch.Groups[1].Value, out var parsedBal))
        {
            balanceAfter = parsedBal;
        }

        var date = TryExtractDateFromOrangeTxId(txId) ?? DateTime.UtcNow;

        var type = MobileMoneyTransactionType.Unknown;
        string? counterparty = null;

        var depMatch = DepositRegex.Match(trimmed);
        if (depMatch.Success)
        {
            type = MobileMoneyTransactionType.Deposit;
            counterparty = depMatch.Groups["from"].Value.Trim();
        }
        else if (FrPaymentRegex.Match(trimmed) is { Success: true } frPay)
        {
            var merchant = frPay.Groups["merchant"].Value.Trim();
            counterparty = merchant;
            type = IsAirtimeOrDataBundle(merchant)
                ? MobileMoneyTransactionType.AirtimeData
                : MobileMoneyTransactionType.MerchantPayment;
        }
        else if (EnPaymentRegex.Match(trimmed) is { Success: true } enPay)
        {
            var merchant = enPay.Groups["merchant"].Value.Trim();
            counterparty = merchant;
            type = IsAirtimeOrDataBundle(merchant)
                ? MobileMoneyTransactionType.AirtimeData
                : MobileMoneyTransactionType.MerchantPayment;
        }
        else if (FrTransferRegex.Match(trimmed) is { Success: true } frTr)
        {
            // In Orange Money CM, sender transfers include "Frais: >0" or are initiated by user;
            // if "Details:" is present with "Frais: 0 FCFA" on an incoming transfer, still classify TransferSent/TransferReceived
            bool hasDetailsPrefix = trimmed.Contains("Details:", StringComparison.OrdinalIgnoreCase);
            if (fee > 0m || !hasDetailsPrefix)
            {
                type = MobileMoneyTransactionType.TransferSent;
                counterparty = frTr.Groups["to"].Value.Trim();
            }
            else
            {
                type = MobileMoneyTransactionType.TransferReceived;
                counterparty = frTr.Groups["from"].Value.Trim();
            }
        }
        else if (EnTransferRegex.Match(trimmed) is { Success: true } enTr)
        {
            bool hasDetailsPrefix = trimmed.Contains("Details:", StringComparison.OrdinalIgnoreCase);
            if (fee > 0m || !hasDetailsPrefix)
            {
                type = MobileMoneyTransactionType.TransferSent;
                counterparty = enTr.Groups["to"].Value.Trim();
            }
            else
            {
                type = MobileMoneyTransactionType.TransferReceived;
                counterparty = enTr.Groups["from"].Value.Trim();
            }
        }
        else if (WithdrawalRegex.IsMatch(trimmed) || txId.StartsWith("CO", StringComparison.OrdinalIgnoreCase))
        {
            type = MobileMoneyTransactionType.Withdrawal;
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

    private static bool IsAirtimeOrDataBundle(string merchant)
    {
        return Regex.IsMatch(merchant, @"(?i)\b(forfait|maxit|welcomepack|bundle|data|credit|pass)\b");
    }

    private static DateTime? TryExtractDateFromOrangeTxId(string txId)
    {
        // Format: PP261019.2025.B00101 -> YYMMDD.HHmm (e.g. 26-10-19 20:25)
        var m = Regex.Match(txId, @"^[A-Z]{2}(\d{2})(\d{2})(\d{2})\.(\d{2})(\d{2})\.");
        if (m.Success &&
            int.TryParse(m.Groups[1].Value, out int yy) &&
            int.TryParse(m.Groups[2].Value, out int mm) &&
            int.TryParse(m.Groups[3].Value, out int dd) &&
            int.TryParse(m.Groups[4].Value, out int hh) &&
            int.TryParse(m.Groups[5].Value, out int min))
        {
            if (mm is >= 1 and <= 12 && dd is >= 1 and <= 31 && hh is >= 0 and <= 23 && min is >= 0 and <= 59)
            {
                try
                {
                    return new DateTime(2000 + yy, mm, dd, hh, min, 0, DateTimeKind.Utc);
                }
                catch
                {
                    return null;
                }
            }
        }
        return null;
    }
}
