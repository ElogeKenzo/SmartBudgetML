using System.Globalization;

namespace SmartBudgetML.Components.Shared;

/// <summary>
/// Culture-safe formatting helpers for the UI layer.
/// SVG coordinates and amounts must never use the device culture
/// (e.g. fr-FR would emit "12,5" and break SVG paths).
/// </summary>
public static class UiFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Formats an amount as "$2,450.00".</summary>
    public static string Money(decimal amount, string symbol = "$", int decimals = 2)
    {
        var sign = amount < 0 ? "-" : "";
        return sign + symbol + Math.Abs(amount).ToString("N" + decimals, Inv);
    }

    /// <summary>Formats a number for SVG attributes (always dot decimal separator).</summary>
    public static string Num(double value) => value.ToString("0.##", Inv);

    public static string Percent(double value, int decimals = 0) =>
        value.ToString("N" + decimals, Inv) + "%";

    /// <summary>Two-letter initials for avatars ("Abel Kenzo" -> "AK").</summary>
    public static string Initials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "?";
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1
            ? parts[0][..1].ToUpperInvariant()
            : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    /// <summary>Maps the seeded Category.Icon keys to a glyph.</summary>
    public static string CategoryGlyph(string? iconKey) => iconKey switch
    {
        "utensils" => "🍽️",
        "car" => "🚗",
        "home" => "🏠",
        "zap" => "⚡",
        "shopping-cart" => "🛒",
        "heart" => "💊",
        "film" => "🎬",
        "shopping-bag" => "🛍️",
        "wallet" => "💼",
        "trending-up" => "📈",
        _ => "🏷️"
    };

    /// <summary>Translucent tile background derived from a category hex color.</summary>
    public static string TileBackground(string? hex) =>
        string.IsNullOrWhiteSpace(hex) ? "rgba(99,102,241,0.15)" : hex + "26"; // ~15% alpha
}
