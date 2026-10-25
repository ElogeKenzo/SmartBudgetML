using System.Globalization;
using SmartBudgetML.Services.Formatting;

namespace SmartBudgetML.Components.Shared;

/// <summary>
/// Culture-safe formatting helpers for the UI layer.
/// SVG coordinates must never use the device culture (e.g. fr-FR would emit "12,5" and break SVG paths).
/// Currency amounts use CurrencyFormatter (defaulting to XAF / FCFA with 0 decimals and space thousands separator).
/// </summary>
public static class UiFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly CurrencyFormatter DefaultFormatter = new();

    /// <summary>
    /// Formats a monetary amount according to the currency symbol or code.
    /// When symbol is "FCFA" or "XAF" (or omitted), formats as "25 000 FCFA" with 0 decimals.
    /// Otherwise formats according to CurrencyFormatter ("$2,450.00", "2 450,00 €", etc.).
    /// </summary>
    public static string Money(decimal amount, string symbol = "FCFA", int decimals = -1)
    {
        var code = symbol?.Trim().ToUpperInvariant() switch
        {
            "$" or "USD" => "USD",
            "€" or "EUR" => "EUR",
            "£" or "GBP" => "GBP",
            "₦" or "NGN" => "NGN",
            "FCFA" or "XAF" or "" or null => "XAF",
            _ => symbol!.Trim()
        };

        if (symbol == "FCFA" || symbol == "XAF" || string.IsNullOrWhiteSpace(symbol))
        {
            return DefaultFormatter.Format(amount, "XAF");
        }

        if (decimals >= 0)
        {
            var sign = amount < 0 ? "-" : "";
            return sign + symbol + Math.Abs(amount).ToString("N" + decimals, Inv);
        }

        return DefaultFormatter.Format(amount, code);
    }

    /// <summary>Formats a number for SVG attributes (always dot decimal separator).</summary>
    public static string Num(double value) => value.ToString("0.##", Inv);

    public static string Percent(double value, int decimals = 0) =>
        value.ToString("N" + decimals, Inv) + "%";

    /// <summary>Day/month/year, the order used in Cameroon in both French and English ("05/11/2026").</summary>
    public static string Date(DateTime date) => date.ToString("dd/MM/yyyy", Inv);

    /// <summary>Two-letter initials for avatars ("Abel Kenzo" -> "AK").</summary>
    public static string Initials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "?";
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1
            ? parts[0][..1].ToUpperInvariant()
            : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    /// <summary>Maps the seeded Category.Icon keys to a glyph (including Cameroonian category icons).</summary>
    public static string CategoryGlyph(string? iconKey) => iconKey switch
    {
        "utensils" => "🍽️",
        "car" => "🚗",
        "home" => "🏠",
        "zap" => "⚡",
        "phone" => "📱",
        "shopping-cart" => "🛒",
        "heart" => "💊",
        "book" => "📚",
        "users" => "🤝",
        "heart-handshake" => "🤲",
        "calendar" => "🎉",
        "gift" => "🙏",
        "film" => "🎬",
        "shopping-bag" => "🛍️",
        "wallet" => "💼",
        "briefcase" => "🏪",
        "arrow-down-circle" => "📲",
        "trending-up" => "📈",
        "more-horizontal" => "📦",
        _ => "🏷️"
    };

    /// <summary>Translucent tile background derived from a category hex color.</summary>
    public static string TileBackground(string? hex) =>
        string.IsNullOrWhiteSpace(hex) ? "rgba(99,102,241,0.15)" : hex + "26"; // ~15% alpha
}
