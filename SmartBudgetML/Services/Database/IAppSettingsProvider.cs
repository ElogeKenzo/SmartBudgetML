using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Database;

public interface IAppSettingsProvider
{
    Task<AppSettings> GetOrCreateSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
    Task<bool> ShouldAutoSelectCategoryAsync(float predictionConfidence);
}
