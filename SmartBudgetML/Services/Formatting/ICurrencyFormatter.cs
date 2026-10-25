namespace SmartBudgetML.Services.Formatting;

/// <summary>
/// Formats monetary amounts deterministically regardless of OS/thread culture.
/// Supports XAF/FCFA (0 decimals, non-breaking space group separator) as well as EUR, USD, GBP, MAD.
/// </summary>
public interface ICurrencyFormatter
{
    string Format(decimal amount, string? currencyCode = "XAF");
}
