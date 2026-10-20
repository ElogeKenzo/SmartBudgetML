using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Data;

public interface ICsvExpenseExporter
{
    string GenerateCsv(IEnumerable<Expense> expenses, IEnumerable<Category> categories);

    Task<string> ExportToFileAsync(
        IEnumerable<Expense> expenses,
        IEnumerable<Category> categories,
        string? fileName = null);
}
