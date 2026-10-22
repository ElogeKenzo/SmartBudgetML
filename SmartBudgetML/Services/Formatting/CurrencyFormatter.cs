using System.Globalization;

namespace SmartBudgetML.Services.Formatting;

/// <summary>
/// Culture-invariant monetary formatter supporting XAF/FCFA, EUR, USD, GBP, MAD.
/// Never relies on Thread.CurrentThread.CurrentCulture.
/// </summary>
public class CurrencyFormatter : ICurrencyFormatter
{
    private static readonly NumberFormatInfo SpaceCommaFormat = new()
    {
        NumberGroupSeparator = "\u00A0",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = new[] { 3 }
    };

    private static readonly NumberFormatInfo CommaDotFormat = new()
    {
        NumberGroupSeparator = ",",
        NumberDecimalSeparator = ".",
        NumberGroupSizes = new[] { 3 }
    };

    public string Format(decimal amount, string? currencyCode = "XAF")
    {
        var code = (currencyCode ?? "XAF").Trim().ToUpperInvariant();
        var sign = amount < 0 ? "-" : string.Empty;
        var abs = Math.Abs(amount);

        switch (code)
        {
            case "XAF":
            case "FCFA":
            case "CFA":
                {
                    var rounded = Math.Round(abs, 0, MidpointRounding.AwayFromZero);
                    return $"{sign}{rounded.ToString("N0", SpaceCommaFormat)} FCFA";
                }
            case "EUR":
            case "€":
                {
                    return $"{sign}{abs.ToString("N2", SpaceCommaFormat)} €";
                }
            case "MAD":
                {
                    return $"{sign}{abs.ToString("N2", SpaceCommaFormat)} MAD";
                }
            case "GBP":
            case "£":
                {
                    return $"{sign}£{abs.ToString("N2", CommaDotFormat)}";
                }
            case "USD":
            case "$":
            default:
                {
                    return $"{sign}${abs.ToString("N2", CommaDotFormat)}";
                }
        }
    }
}
