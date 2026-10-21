namespace SmartBudgetML.Services.Localization;

public sealed record LocalizedString(string Name, string Value, bool ResourceNotFound = false)
{
    public override string ToString() => Value;
    public static implicit operator string(LocalizedString localizedString) => localizedString.Value;
}

public interface IStringLocalizer
{
    string CurrentLanguage { get; }
    event Action? OnLanguageChanged;
    void SetLanguage(string languageCode);
    LocalizedString this[string name] { get; }
    LocalizedString this[string name, params object[] arguments] { get; }
    IReadOnlyDictionary<string, string> GetAllStrings(string languageCode);
}

public interface IStringLocalizer<out T> : IStringLocalizer
{
}
