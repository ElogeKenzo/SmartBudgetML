using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using SmartBudgetML.Resources.Strings;

namespace SmartBudgetML.Services.Localization;

/// <summary>
/// Reads localized UI strings from embedded or file-system .resx XML files (`AppStrings.fr.resx` and `AppStrings.en.resx`),
/// defaulting to French ("fr") per Cameroonian localization requirements.
/// </summary>
public class ResxStringLocalizer<T> : IStringLocalizer<T>
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> AllResources = new(LoadAllResources);

    public string CurrentLanguage { get; private set; } = "fr";

    public event Action? OnLanguageChanged;

    public ResxStringLocalizer()
    {
        CurrentLanguage = "fr";
    }

    public void SetLanguage(string languageCode)
    {
        var normalized = NormalizeLanguage(languageCode);
        if (!string.Equals(CurrentLanguage, normalized, StringComparison.OrdinalIgnoreCase))
        {
            CurrentLanguage = normalized;
            OnLanguageChanged?.Invoke();
        }
    }

    public LocalizedString this[string name]
    {
        get
        {
            if (string.IsNullOrEmpty(name))
            {
                return new LocalizedString(string.Empty, string.Empty, ResourceNotFound: true);
            }

            var dict = GetDictionary(CurrentLanguage);
            if (dict.TryGetValue(name, out var val))
            {
                return new LocalizedString(name, val, ResourceNotFound: false);
            }

            return new LocalizedString(name, name, ResourceNotFound: true);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var baseStr = this[name];
            if (baseStr.ResourceNotFound || arguments.Length == 0)
            {
                return baseStr;
            }

            var formatted = string.Format(CultureInfo.InvariantCulture, baseStr.Value, arguments);
            return new LocalizedString(name, formatted, ResourceNotFound: false);
        }
    }

    public IReadOnlyDictionary<string, string> GetAllStrings(string languageCode)
    {
        var normalized = NormalizeLanguage(languageCode);
        return GetDictionary(normalized);
    }

    private static string NormalizeLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "fr";
        }

        return languageCode.Trim().StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? "en"
            : "fr";
    }

    private static Dictionary<string, string> GetDictionary(string lang)
    {
        var map = AllResources.Value;
        if (map.TryGetValue(lang, out var dict))
        {
            return dict;
        }

        return map.TryGetValue("fr", out var frDict)
            ? frDict
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static Dictionary<string, Dictionary<string, string>> LoadAllResources()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["fr"] = LoadLanguageResx("fr"),
            ["en"] = LoadLanguageResx("en")
        };
        return result;
    }

    private static Dictionary<string, string> LoadLanguageResx(string lang)
    {
        var assembly = typeof(AppStrings).Assembly;
        var resourceSuffix = $"AppStrings.{lang}.resx";
        var manifestName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));

        if (manifestName != null)
        {
            using var stream = assembly.GetManifestResourceStream(manifestName);
            if (stream != null)
            {
                return ParseResxStream(stream);
            }
        }

        // Fallback: locate on disk relative to AppContext.BaseDirectory (for test runners if not embedded)
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "SmartBudgetML", "Resources", "Strings", resourceSuffix);
            if (File.Exists(candidate))
            {
                using var fs = File.OpenRead(candidate);
                return ParseResxStream(fs);
            }

            var directCandidate = Path.Combine(dir.FullName, "Resources", "Strings", resourceSuffix);
            if (File.Exists(directCandidate))
            {
                using var fs = File.OpenRead(directCandidate);
                return ParseResxStream(fs);
            }

            dir = dir.Parent;
        }

        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ParseResxStream(Stream stream)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var doc = XDocument.Load(stream);
        foreach (var dataElem in doc.Root?.Elements("data") ?? Enumerable.Empty<XElement>())
        {
            var key = dataElem.Attribute("name")?.Value;
            var value = dataElem.Element("value")?.Value;
            if (!string.IsNullOrWhiteSpace(key) && value != null)
            {
                dict[key] = value;
            }
        }
        return dict;
    }
}
