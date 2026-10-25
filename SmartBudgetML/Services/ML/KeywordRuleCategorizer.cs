using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Stage 2 deterministic keyword and phrase categorizer tailored for Cameroonian French,
/// English, Pidgin, and local services (ENEO, CAMWATER, Orange Money, MTN MoMo, bendskin, Yango, tontine/njangi).
/// </summary>
public class KeywordRuleCategorizer : IKeywordRuleCategorizer
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "du", "des", "le", "la", "les", "un", "une", "et", "ou", "au", "aux",
        "vers", "pour", "par", "dans", "sur", "en", "avec", "sans", "chez", "mon", "ma", "mes",
        "ce", "cet", "cette", "ces", "a", "d", "l", "n", "s", "y",
        "fcfa", "xaf", "cfa", "francs", "usd", "eur",
        "the", "to", "at", "and", "for", "of", "in", "on", "with", "from", "by"
    };

    private sealed record RuleEntry(string Keyword, string CategoryName, float Confidence, bool IsPhrase);

    private static readonly List<RuleEntry> Rules = BuildRules();

    public string NormalizeText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var trimmed = input.Length > 2000 ? input[..2000] : input;
        var formD = trimmed.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);

        foreach (char c in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetter(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsDigit(c))
            {
                // Strip digits (amounts, quantities, phone numbers)
                sb.Append(' ');
            }
            else
            {
                sb.Append(' ');
            }
        }

        var tokens = sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 1 && !Stopwords.Contains(t));

        return string.Join(' ', tokens);
    }

    public CategoryPrediction? Match(string? description)
    {
        var normalized = NormalizeText(description);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var tokenSet = new HashSet<string>(
            normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);

        var paddedNormalized = $" {normalized} ";

        foreach (var rule in Rules)
        {
            if (rule.IsPhrase)
            {
                if (paddedNormalized.Contains($" {rule.Keyword} ", StringComparison.OrdinalIgnoreCase))
                {
                    return new CategoryPrediction
                    {
                        PredictedCategory = rule.CategoryName,
                        ExplicitConfidence = rule.Confidence,
                        Source = PredictionSource.Rules,
                        MatchedRuleKeyword = rule.Keyword
                    };
                }
            }
            else
            {
                if (tokenSet.Contains(rule.Keyword))
                {
                    return new CategoryPrediction
                    {
                        PredictedCategory = rule.CategoryName,
                        ExplicitConfidence = rule.Confidence,
                        Source = PredictionSource.Rules,
                        MatchedRuleKeyword = rule.Keyword
                    };
                }
            }
        }

        return null;
    }

    private static List<RuleEntry> BuildRules()
    {
        var entries = new List<(string Keyword, string Category, float Confidence)>
        {
            // High-specificity multi-word phrase rules first (prevent false positives across categories)
            ("tour bouffe tontine", "Salary / Income", 0.96f),
            ("bouffe tontine", "Salary / Income", 0.96f),
            ("virement salaire", "Salary / Income", 0.96f),
            ("salaire mensuel", "Salary / Income", 0.96f),
            ("salaire fonction", "Salary / Income", 0.96f),
            ("loyer percu", "Salary / Income", 0.96f),
            ("revenu location", "Salary / Income", 0.96f),
            ("paiement client", "Salary / Income", 0.95f),
            ("reglement client", "Salary / Income", 0.95f),
            ("facture client", "Salary / Income", 0.95f),
            ("depot client", "Salary / Income", 0.95f),
            ("recette journaliere", "Salary / Income", 0.95f),
            ("recette vente", "Salary / Income", 0.95f),
            ("recette salon", "Salary / Income", 0.95f),
            ("transfert diaspora", "Salary / Income", 0.95f),
            ("transfert recu", "Salary / Income", 0.95f),
            ("gains debrouille", "Salary / Income", 0.95f),
            ("remboursement frais", "Salary / Income", 0.95f),
            ("company monthly direct deposit", "Salary / Income", 0.96f),
            ("direct deposit payroll", "Salary / Income", 0.96f),

            // Solidarity / Other specific phrases
            ("om maman", "Other", 0.96f),
            ("aide maman", "Other", 0.96f),
            ("frais scolarite", "Other", 0.96f),
            ("frais scolaires", "Other", 0.96f),
            ("fournitures scolaires", "Other", 0.96f),
            ("cahiers livres scolaires", "Other", 0.96f),
            ("cotisation deuil", "Other", 0.96f),
            ("contribution deuil", "Other", 0.96f),
            ("cadeau mariage", "Other", 0.96f),
            ("offrande culte", "Other", 0.96f),
            ("offrande dimanche", "Other", 0.96f),
            ("frais retrait", "Other", 0.95f),
            ("timbre fiscal", "Other", 0.95f),
            ("timbre communal", "Other", 0.95f),
            ("carte nationale identite", "Other", 0.95f),
            ("passeport biometrique", "Other", 0.95f),
            ("reunion familiale", "Other", 0.95f),
            ("soutien medical", "Other", 0.95f),
            ("aide financiere", "Other", 0.95f),
            ("arbre noel", "Other", 0.95f),
            ("envoi colis", "Other", 0.95f),
            ("impression memoire", "Other", 0.95f),

            // Housing & Rent specific phrases
            ("loyer mensuel", "Housing & Rent", 0.96f),
            ("paiement loyer", "Housing & Rent", 0.96f),
            ("avance mois loyer", "Housing & Rent", 0.96f),
            ("avance loyer", "Housing & Rent", 0.96f),
            ("caution location", "Housing & Rent", 0.96f),
            ("fosse septique", "Housing & Rent", 0.95f),
            ("toles toiture", "Housing & Rent", 0.95f),
            ("reparation toiture", "Housing & Rent", 0.95f),
            ("agence immobiliere", "Housing & Rent", 0.95f),
            ("gardien barriere", "Housing & Rent", 0.95f),
            ("gardien nuit", "Housing & Rent", 0.95f),
            ("charges gardiennage", "Housing & Rent", 0.95f),
            ("charges copropriete", "Housing & Rent", 0.95f),
            ("seau peinture", "Housing & Rent", 0.94f),
            ("achat peinture", "Housing & Rent", 0.94f),
            ("apartment rent", "Housing & Rent", 0.96f),

            // Investments specific phrases
            ("tontine njangi", "Investments", 0.96f),
            ("cotisation tontine", "Investments", 0.96f),
            ("cotisation mensuelle tontine", "Investments", 0.96f),
            ("compte bloque", "Investments", 0.95f),
            ("epargne bancaire", "Investments", 0.95f),
            ("depot terme", "Investments", 0.95f),
            ("caisse epargne", "Investments", 0.95f),
            ("bons tresor", "Investments", 0.95f),
            ("obligations etat", "Investments", 0.95f),
            ("apport capital", "Investments", 0.95f),
            ("fonds roulement", "Investments", 0.95f),
            ("achat poussins", "Investments", 0.95f),
            ("traite mensuelle achat", "Investments", 0.95f),
            ("achat terrain", "Investments", 0.95f),
            ("mutuelle epargne", "Investments", 0.95f),
            ("mutuelle investissement", "Investments", 0.95f),

            // Utilities specific phrases & tokens
            ("facture eneo", "Utilities", 0.96f),
            ("prepaye eneo", "Utilities", 0.96f),
            ("recu eneo", "Utilities", 0.96f),
            ("facture camwater", "Utilities", 0.96f),
            ("eau camwater", "Utilities", 0.96f),
            ("bouteille gaz", "Utilities", 0.96f),
            ("forfait data", "Utilities", 0.95f),
            ("forfait internet", "Utilities", 0.95f),
            ("pass internet", "Utilities", 0.95f),
            ("camtel blue", "Utilities", 0.96f),
            ("canal bouquet", "Utilities", 0.95f),
            ("recharge credit", "Utilities", 0.95f),
            ("credit recharge", "Utilities", 0.95f),
            ("electric utility", "Utilities", 0.96f),

            // Transportation specific phrases
            ("kia taxi", "Transportation", 0.96f),
            ("taxi ramassage", "Transportation", 0.96f),
            ("taxi depot", "Transportation", 0.96f),
            ("moto bendskin", "Transportation", 0.96f),
            ("yango course", "Transportation", 0.96f),
            ("course yango", "Transportation", 0.96f),
            ("station total", "Transportation", 0.95f),
            ("station tradex", "Transportation", 0.95f),
            ("station ola", "Transportation", 0.95f),
            ("finex voyages", "Transportation", 0.96f),
            ("touristique express", "Transportation", 0.96f),
            ("general express", "Transportation", 0.96f),
            ("peage routier", "Transportation", 0.95f),
            ("vidange moteur", "Transportation", 0.95f),
            ("vidange huile", "Transportation", 0.95f),
            ("lavage vehicule", "Transportation", 0.95f),
            ("lavage auto", "Transportation", 0.95f),
            ("uber trip", "Transportation", 0.96f),
            ("uber ride", "Transportation", 0.96f),

            // Groceries specific phrases
            ("marche mokolo", "Groceries", 0.96f),
            ("marche mfoundi", "Groceries", 0.96f),
            ("marche sandaga", "Groceries", 0.96f),
            ("marche mboppi", "Groceries", 0.96f),
            ("marche nkololoun", "Groceries", 0.96f),
            ("marche ndokoti", "Groceries", 0.96f),
            ("marche new bell", "Groceries", 0.96f),
            ("marche central douala", "Groceries", 0.96f),
            ("supermarche dovv", "Groceries", 0.96f),
            ("supermarche mahima", "Groceries", 0.96f),
            ("supermarche casino", "Groceries", 0.96f),
            ("carrefour market", "Groceries", 0.96f),
            ("santa lucia", "Groceries", 0.96f),
            ("super bali", "Groceries", 0.95f),
            ("super bonapriso", "Groceries", 0.95f),
            ("boucherie moderne", "Groceries", 0.95f),
            ("boucherie locale", "Groceries", 0.95f),
            ("poissonnerie locale", "Groceries", 0.95f),
            ("poissonnerie congelee", "Groceries", 0.95f),
            ("savon menage", "Groceries", 0.95f),
            ("whole foods", "Groceries", 0.96f),

            // Food & Dining specific phrases
            ("poisson braise", "Food & Dining", 0.96f),
            ("porc braise", "Food & Dining", 0.96f),
            ("braise porc", "Food & Dining", 0.96f),
            ("poulet braise", "Food & Dining", 0.96f),
            ("poulet dg", "Food & Dining", 0.96f),
            ("ndole crevettes", "Food & Dining", 0.96f),
            ("tourne dos", "Food & Dining", 0.96f),
            ("beignets haricot", "Food & Dining", 0.96f),
            ("beignet haricot", "Food & Dining", 0.96f),
            ("eru waterfufu", "Food & Dining", 0.96f),
            ("taro sauce jaune", "Food & Dining", 0.96f),
            ("okok sale", "Food & Dining", 0.96f),
            ("okok sucre", "Food & Dining", 0.96f),
            ("koki macabo", "Food & Dining", 0.96f),
            ("koki chaud", "Food & Dining", 0.96f),
            ("mbongo poisson", "Food & Dining", 0.96f),
            ("mbongo tchobi", "Food & Dining", 0.96f),
            ("kondre boeuf", "Food & Dining", 0.96f),
            ("kondre porc", "Food & Dining", 0.96f),
            ("bouillon chevre", "Food & Dining", 0.95f),
            ("bouillon boeuf", "Food & Dining", 0.95f),
            ("spaghetti omelette", "Food & Dining", 0.95f),
            ("omelette spaghetti", "Food & Dining", 0.95f),
            ("boulangerie zepol", "Food & Dining", 0.95f),
            ("boulangerie acropole", "Food & Dining", 0.95f),
            ("starbucks caramel", "Food & Dining", 0.96f),
            ("mcdonalds double", "Food & Dining", 0.96f),

            // Single-token high-precision rules
            ("eneo", "Utilities", 0.92f),
            ("camwater", "Utilities", 0.92f),
            ("kpdp", "Utilities", 0.92f),
            ("yoomee", "Utilities", 0.92f),
            ("hysacam", "Utilities", 0.92f),
            ("startimes", "Utilities", 0.92f),

            ("bendskin", "Transportation", 0.92f),
            ("yango", "Transportation", 0.92f),
            ("vulcanisateur", "Transportation", 0.92f),
            ("camrail", "Transportation", 0.92f),
            ("uber", "Transportation", 0.92f),
            ("lyft", "Transportation", 0.92f),

            ("pharmacie", "Healthcare", 0.92f),
            ("coartem", "Healthcare", 0.92f),
            ("laquintinie", "Healthcare", 0.92f),
            ("goutte epaisse", "Healthcare", 0.95f),
            ("dentiste", "Healthcare", 0.92f),
            ("detartrage", "Healthcare", 0.92f),
            ("pediatrie", "Healthcare", 0.92f),
            ("pediatre", "Healthcare", 0.92f),
            ("opticien", "Healthcare", 0.92f),
            ("radiographie", "Healthcare", 0.92f),
            ("echographie", "Healthcare", 0.92f),
            ("maternite", "Healthcare", 0.92f),
            ("ophtalmologie", "Healthcare", 0.92f),
            ("ophtalmologue", "Healthcare", 0.92f),
            ("glucometre", "Healthcare", 0.92f),
            ("kinesitherapie", "Healthcare", 0.92f),

            ("canalolympia", "Entertainment", 0.93f),
            ("netflix", "Entertainment", 0.93f),
            ("spotify", "Entertainment", 0.93f),
            ("playstation", "Entertainment", 0.92f),
            ("karaoke", "Entertainment", 0.92f),
            ("stand up", "Entertainment", 0.93f),
            ("futsal", "Entertainment", 0.92f),

            ("njangi", "Investments", 0.92f),
            ("bvmac", "Investments", 0.92f),
            ("muffa", "Investments", 0.92f),
            ("vanguard", "Investments", 0.92f),
            ("fidelity", "Investments", 0.92f),

            ("soya", "Food & Dining", 0.91f),
            ("chawarma", "Food & Dining", 0.91f),
            ("starbucks", "Food & Dining", 0.92f),
            ("mcdonalds", "Food & Dining", 0.92f),

            ("plombier", "Housing & Rent", 0.91f),
            ("menuisier", "Housing & Rent", 0.91f),
            ("macon", "Housing & Rent", 0.91f),

            ("barbier", "Shopping", 0.91f),
            ("mocassins", "Shopping", 0.91f),
            ("parfum", "Shopping", 0.91f),
            ("valise", "Shopping", 0.91f),

            ("honoraires", "Salary / Income", 0.92f),
            ("payroll", "Salary / Income", 0.92f),

            ("orphelinat", "Other", 0.92f),
            ("dime", "Other", 0.92f)
        };

        return entries
            .Select(e => new RuleEntry(
                e.Keyword,
                e.Category,
                e.Confidence,
                e.Keyword.Contains(' ')))
            .ToList();
    }
}
