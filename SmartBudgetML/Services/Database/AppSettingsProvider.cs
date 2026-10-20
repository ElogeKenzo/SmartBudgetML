using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Database;

public class AppSettingsProvider : IAppSettingsProvider
{
    private readonly IDatabaseService _db;

    public AppSettingsProvider(IDatabaseService db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<AppSettings> GetOrCreateSettingsAsync()
    {
        await _db.InitializeAsync();
        var all = await _db.Settings.GetAllAsync();
        if (all.Count > 0)
        {
            var existing = all[0];
            if (existing.MlConfidenceThreshold <= 0f || existing.MlConfidenceThreshold > 1.0f)
            {
                existing.MlConfidenceThreshold = 0.70f;
                await _db.Settings.UpdateAsync(existing);
            }
            return existing;
        }

        var created = new AppSettings
        {
            IsBiometricEnabled = false,
            IsPinAuthEnabled = false,
            DarkMode = true,
            CurrencyCode = "XAF",
            AutoLockTimeoutSeconds = 300,
            MlConfidenceThreshold = 0.70f,
            AutoCategorizeEnabled = true,
            OverBudgetAlertsEnabled = true,
            DailyRemindersEnabled = false,
            UpdatedAt = DateTime.UtcNow
        };
        await _db.Settings.InsertAsync(created);
        return created;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _db.InitializeAsync();

        settings.MlConfidenceThreshold = Math.Clamp(settings.MlConfidenceThreshold, 0.05f, 0.99f);
        settings.UpdatedAt = DateTime.UtcNow;

        if (settings.Id > 0)
        {
            await _db.Settings.UpdateAsync(settings);
        }
        else
        {
            await _db.Settings.InsertAsync(settings);
        }
    }

    public async Task<bool> ShouldAutoSelectCategoryAsync(float predictionConfidence)
    {
        var settings = await GetOrCreateSettingsAsync();
        if (!settings.AutoCategorizeEnabled)
        {
            return false;
        }

        return predictionConfidence >= settings.MlConfidenceThreshold;
    }
}
