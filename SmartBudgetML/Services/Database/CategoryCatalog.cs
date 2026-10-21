using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Database;

/// <summary>
/// Provides the Cameroonian default category catalog (14 expense categories + 3 income categories),
/// stable language-independent category keys, French/English display labels, suggested XAF budgets,
/// and legacy/ML category name resolution.
/// </summary>
public static class CategoryCatalog
{
    public sealed record CategoryDefinition(
        string Key,
        string FrenchName,
        string EnglishName,
        string Icon,
        string ColorHex,
        bool IsIncome,
        decimal SuggestedBudgetXaf);

    private static readonly IReadOnlyList<CategoryDefinition> Definitions = new List<CategoryDefinition>
    {
        // 14 Expense Categories (Cameroonian realities)
        new("food_market", "Alimentation / Marché", "Food & Dining", "utensils", "#F59E0B", false, 60_000m),
        new("transport", "Transport (Taxi / Moto / Car)", "Transportation", "car", "#3B82F6", false, 30_000m),
        new("housing_rent", "Logement / Loyer", "Housing & Rent", "home", "#8B5CF6", false, 75_000m),
        new("utilities", "Eau & Électricité (ENEO / Camwater)", "Utilities", "zap", "#EAB308", false, 20_000m),
        new("telecom_data", "Télécom & Internet (MTN / Orange / Camtel)", "Telecom & Data", "phone", "#06B6D4", false, 15_000m),
        new("healthcare", "Santé / Pharmacie", "Healthcare", "heart", "#EF4444", false, 20_000m),
        new("education", "Éducation / Scolarité", "Education & Tuition", "book", "#6366F1", false, 40_000m),
        new("tontine", "Tontine / Cotisation", "Tontine / Njangi", "users", "#10B981", false, 50_000m),
        new("family_solidarity", "Aide familiale / Solidarité", "Family Support", "heart-handshake", "#EC4899", false, 25_000m),
        new("events", "Événements (Deuil / Mariage / Fête)", "Events & Ceremonies", "calendar", "#F97316", false, 20_000m),
        new("church_donations", "Dîme / Offrandes / Dons", "Tithes & Donations", "gift", "#A855F7", false, 15_000m),
        new("entertainment", "Loisirs / Sorties", "Entertainment", "film", "#EC4899", false, 20_000m),
        new("savings", "Épargne / Investissement", "Savings & Investments", "trending-up", "#14B8A6", false, 40_000m),
        new("other", "Autres", "Other", "more-horizontal", "#6B7280", false, 15_000m),

        // 3 Income Categories
        new("salary", "Salaire / Revenu", "Salary / Income", "wallet", "#10B981", true, 0m),
        new("business_hustle", "Commerce / Débrouille", "Business / Side Hustle", "briefcase", "#059669", true, 0m),
        new("transfers_received", "Transferts reçus (MoMo / OM)", "Transfers Received", "arrow-down-circle", "#34D399", true, 0m)
    };

    private static readonly Dictionary<string, CategoryDefinition> ByKey =
        Definitions.ToDictionary(d => d.Key, d => d, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> AliasToKey = new(StringComparer.OrdinalIgnoreCase)
    {
        // Canonical keys
        ["food_market"] = "food_market",
        ["transport"] = "transport",
        ["housing_rent"] = "housing_rent",
        ["utilities"] = "utilities",
        ["telecom_data"] = "telecom_data",
        ["healthcare"] = "healthcare",
        ["education"] = "education",
        ["tontine"] = "tontine",
        ["family_solidarity"] = "family_solidarity",
        ["events"] = "events",
        ["church_donations"] = "church_donations",
        ["entertainment"] = "entertainment",
        ["savings"] = "savings",
        ["other"] = "other",
        ["salary"] = "salary",
        ["business_hustle"] = "business_hustle",
        ["transfers_received"] = "transfers_received",

        // Legacy English & ML category names -> stable keys
        ["Food & Dining"] = "food_market",
        ["Groceries"] = "food_market",
        ["Alimentation / Marché"] = "food_market",
        ["Transportation"] = "transport",
        ["Transport (Taxi / Moto / Car)"] = "transport",
        ["Housing & Rent"] = "housing_rent",
        ["Logement / Loyer"] = "housing_rent",
        ["Utilities"] = "utilities",
        ["Eau & Électricité (ENEO / Camwater)"] = "utilities",
        ["Telecom & Data"] = "telecom_data",
        ["Télécom & Internet (MTN / Orange / Camtel)"] = "telecom_data",
        ["Healthcare"] = "healthcare",
        ["Santé / Pharmacie"] = "healthcare",
        ["Education"] = "education",
        ["Education & Tuition"] = "education",
        ["Éducation / Scolarité"] = "education",
        ["Tontine"] = "tontine",
        ["Tontine / Njangi"] = "tontine",
        ["Tontine / Cotisation"] = "tontine",
        ["Family Support"] = "family_solidarity",
        ["Aide familiale / Solidarité"] = "family_solidarity",
        ["Events"] = "events",
        ["Events & Ceremonies"] = "events",
        ["Événements (Deuil / Mariage / Fête)"] = "events",
        ["Tithes & Donations"] = "church_donations",
        ["Dîme / Offrandes / Dons"] = "church_donations",
        ["Entertainment"] = "entertainment",
        ["Loisirs / Sorties"] = "entertainment",
        ["Investments"] = "savings",
        ["Savings & Investments"] = "savings",
        ["Épargne / Investissement"] = "savings",
        ["Shopping"] = "other",
        ["Other"] = "other",
        ["Autres"] = "other",
        ["Salary / Income"] = "salary",
        ["Salaire / Revenu"] = "salary",
        ["Business / Side Hustle"] = "business_hustle",
        ["Commerce / Débrouille"] = "business_hustle",
        ["Transfers Received"] = "transfers_received",
        ["Transferts reçus (MoMo / OM)"] = "transfers_received"
    };

    public static List<Category> GetCameroonianDefaultCategories()
    {
        return Definitions.Select(d => new Category
        {
            Key = d.Key,
            Name = d.FrenchName,
            Icon = d.Icon,
            ColorHex = d.ColorHex,
            BudgetLimit = 0m,
            IsDefault = true,
            IsIncome = d.IsIncome
        }).ToList();
    }

    public static string GetDisplayName(Category? category, string language = "fr")
    {
        if (category == null)
        {
            return string.Empty;
        }

        var key = !string.IsNullOrWhiteSpace(category.Key)
            ? category.Key
            : ResolveCategoryKey(category.Name);

        if (!string.IsNullOrWhiteSpace(key) && ByKey.TryGetValue(key, out var def))
        {
            return language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                ? def.EnglishName
                : def.FrenchName;
        }

        return category.Name;
    }

    public static string ResolveCategoryKey(string? categoryNameOrKey)
    {
        if (string.IsNullOrWhiteSpace(categoryNameOrKey))
        {
            return string.Empty;
        }

        return AliasToKey.TryGetValue(categoryNameOrKey.Trim(), out var key)
            ? key
            : string.Empty;
    }

    public static decimal GetSuggestedBudgetXaf(string? categoryKey)
    {
        if (string.IsNullOrWhiteSpace(categoryKey))
        {
            return 0m;
        }

        if (ByKey.TryGetValue(categoryKey.Trim(), out var def))
        {
            return def.SuggestedBudgetXaf;
        }

        var resolved = ResolveCategoryKey(categoryKey);
        return !string.IsNullOrEmpty(resolved) && ByKey.TryGetValue(resolved, out var resolvedDef)
            ? resolvedDef.SuggestedBudgetXaf
            : 0m;
    }
}
