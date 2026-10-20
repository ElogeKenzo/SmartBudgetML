using System.Globalization;
using System.Text;
using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Data;

/// <summary>
/// RFC 4180 compliant CSV exporter with UTF-8 BOM, ISO-8601 dates,
/// resolved category names, and invariant decimal formatting.
/// </summary>
public class CsvExpenseExporter : ICsvExpenseExporter
{
    private readonly IFileExportService _fileExportService;

    public CsvExpenseExporter(IFileExportService fileExportService)
    {
        _fileExportService = fileExportService ?? throw new ArgumentNullException(nameof(fileExportService));
    }

    public string GenerateCsv(IEnumerable<Expense> expenses, IEnumerable<Category> categories)
    {
        var categoryMap = (categories ?? Enumerable.Empty<Category>())
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First().Name);

        var sb = new StringBuilder();
        sb.Append("Date,Description,Category,Amount,Type,Notes\r\n");

        foreach (var exp in expenses ?? Enumerable.Empty<Expense>())
        {
            var dateStr = exp.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var categoryName = categoryMap.TryGetValue(exp.CategoryId, out var resolvedName) && !string.IsNullOrWhiteSpace(resolvedName)
                ? resolvedName
                : (string.IsNullOrWhiteSpace(exp.CategoryName) ? "Uncategorized" : exp.CategoryName!);

            var amountStr = exp.Amount.ToString("0.00", CultureInfo.InvariantCulture);
            var typeStr = exp.IsIncome ? "Income" : "Expense";

            sb.Append(dateStr).Append(',')
              .Append(EscapeRfc4180(exp.Description)).Append(',')
              .Append(EscapeRfc4180(categoryName)).Append(',')
              .Append(amountStr).Append(',')
              .Append(typeStr).Append(',')
              .Append(EscapeRfc4180(exp.Notes ?? string.Empty))
              .Append("\r\n");
        }

        return sb.ToString();
    }

    public async Task<string> ExportToFileAsync(
        IEnumerable<Expense> expenses,
        IEnumerable<Category> categories,
        string? fileName = null)
    {
        var csv = GenerateCsv(expenses, categories);
        var targetName = string.IsNullOrWhiteSpace(fileName)
            ? $"smartbudget_expenses_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv"
            : fileName;

        var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return await _fileExportService.WriteExportFileAsync(targetName, csv, utf8WithBom);
    }

    public static string EscapeRfc4180(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        bool mustQuote = false;
        foreach (char c in field)
        {
            if (c == '"' || c == ',' || c == '\r' || c == '\n' || c == '&' || c == ' ' || c > 127)
            {
                mustQuote = true;
                break;
            }
        }

        if (!mustQuote)
        {
            return field;
        }

        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
