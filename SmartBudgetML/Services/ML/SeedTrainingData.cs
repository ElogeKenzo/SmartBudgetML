using System.Reflection;
using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Loads synthetic Cameroonian training (seed_fr.csv) and holdout validation (holdout_fr.csv) datasets
/// from versioned CSV resource files for 100% on-device cold-start training and evaluation.
/// Marked SYNTHÉTIQUE — contains zero personal data or real phone numbers.
/// </summary>
public static class SeedTrainingData
{
    public const string SeedFileName = "seed_fr.csv";
    public const string HoldoutFileName = "holdout_fr.csv";

    public static List<ExpenseTransactionData> GetDefaultDataset()
    {
        var loaded = LoadCsvDataset(SeedFileName);
        return loaded.Count > 0 ? loaded : GetFallbackDataset();
    }

    public static List<ExpenseTransactionData> GetHoldoutDataset()
    {
        return LoadCsvDataset(HoldoutFileName);
    }

    public static List<ExpenseTransactionData> LoadCsvDataset(string fileName)
    {
        foreach (var candidatePath in GetCandidatePaths(fileName))
        {
            if (File.Exists(candidatePath))
            {
                var lines = File.ReadAllLines(candidatePath);
                var parsed = ParseCsvLines(lines);
                if (parsed.Count > 0)
                {
                    return parsed;
                }
            }
        }

        return new List<ExpenseTransactionData>();
    }

    public static List<ExpenseTransactionData> ParseCsvLines(IEnumerable<string> lines)
    {
        var list = new List<ExpenseTransactionData>();
        if (lines == null) return list;

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            var line = rawLine.Trim();
            if (line.StartsWith('#')) continue;
            if (line.StartsWith("Description,CategoryName", StringComparison.OrdinalIgnoreCase)) continue;

            int lastComma = line.LastIndexOf(',');
            if (lastComma <= 0 || lastComma >= line.Length - 1) continue;

            var desc = line[..lastComma].Trim().Trim('"');
            var cat = line[(lastComma + 1)..].Trim().Trim('"');

            if (!string.IsNullOrWhiteSpace(desc) && !string.IsNullOrWhiteSpace(cat))
            {
                list.Add(new ExpenseTransactionData
                {
                    Description = desc,
                    CategoryName = cat
                });
            }
        }

        return list;
    }

    private static IEnumerable<string> GetCandidatePaths(string fileName)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        yield return Path.Combine(baseDir, "Resources", "Raw", fileName);
        yield return Path.Combine(baseDir, fileName);

        // Walk up parent directories to locate SmartBudgetML/Resources/Raw/<fileName> during test runs
        var dir = new DirectoryInfo(baseDir);
        for (int depth = 0; depth < 6 && dir != null; depth++)
        {
            yield return Path.Combine(dir.FullName, "SmartBudgetML", "Resources", "Raw", fileName);
            yield return Path.Combine(dir.FullName, "Resources", "Raw", fileName);
            dir = dir.Parent;
        }
    }

    private static List<ExpenseTransactionData> GetFallbackDataset() => new()
    {
        new() { Description = "Starbucks Caramel Frappuccino Coffee", CategoryName = "Food & Dining" },
        new() { Description = "McDonalds Big Mac Meal Fries", CategoryName = "Food & Dining" },
        new() { Description = "Poisson braisé bar et plantain mûr Bonapriso", CategoryName = "Food & Dining" },
        new() { Description = "Whole Foods organic milk eggs apples", CategoryName = "Groceries" },
        new() { Description = "Marché Mokolo achat plantain huile rouge et macabo", CategoryName = "Groceries" },
        new() { Description = "Uber ride downtown city center office", CategoryName = "Transportation" },
        new() { Description = "Moto bendskin déplacement rapide carrefour Ndokoti", CategoryName = "Transportation" },
        new() { Description = "Monthly apartment rent payment landlord", CategoryName = "Housing & Rent" },
        new() { Description = "Loyer mensuel appartement 2 chambres virement bailleur", CategoryName = "Housing & Rent" },
        new() { Description = "Electric utility power company electric bill", CategoryName = "Utilities" },
        new() { Description = "Facture ENEO électricité compteur postpayé mois", CategoryName = "Utilities" },
        new() { Description = "CVS pharmacy prescription medication pills", CategoryName = "Healthcare" },
        new() { Description = "Pharmacie du Soleil achat médicaments paludisme Coartem", CategoryName = "Healthcare" },
        new() { Description = "Netflix monthly streaming subscription", CategoryName = "Entertainment" },
        new() { Description = "Sortie cinéma CanalOlympia Bessengue billets et pop-corn", CategoryName = "Entertainment" },
        new() { Description = "Amazon prime online order household items", CategoryName = "Shopping" },
        new() { Description = "Achat tissu pagne Super Wax et couture tailleur", CategoryName = "Shopping" },
        new() { Description = "Company payroll monthly direct deposit salary", CategoryName = "Salary / Income" },
        new() { Description = "Virement salaire mensuel entreprise banque Afriland", CategoryName = "Salary / Income" },
        new() { Description = "Vanguard S&P 500 index ETF stock purchase", CategoryName = "Investments" },
        new() { Description = "Cotisation mensuelle tontine Njangi association quartier", CategoryName = "Investments" },
        new() { Description = "Transfert Orange Money aide maman au village", CategoryName = "Other" }
    };
}
