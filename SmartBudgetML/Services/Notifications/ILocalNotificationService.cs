namespace SmartBudgetML.Services.Notifications;

public sealed record ScheduledReminder(
    string Id,
    string Title,
    string Message,
    DateTime NotifyAtUtc);

public interface ILocalNotificationService
{
    Task ScheduleReminderAsync(ScheduledReminder reminder);
    Task CancelReminderAsync(string id);
    IReadOnlyList<ScheduledReminder> GetScheduledReminders();
}

public class InMemoryLocalNotificationService : ILocalNotificationService
{
    private readonly List<ScheduledReminder> _reminders = new();

    public Task ScheduleReminderAsync(ScheduledReminder reminder)
    {
        _reminders.RemoveAll(r => r.Id == reminder.Id);
        _reminders.Add(reminder);
        return Task.CompletedTask;
    }

    public Task CancelReminderAsync(string id)
    {
        _reminders.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }

    public IReadOnlyList<ScheduledReminder> GetScheduledReminders() => _reminders.AsReadOnly();
}
