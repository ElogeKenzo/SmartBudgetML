using System.Text.RegularExpressions;
using Xunit;

namespace SmartBudgetML.Tests.UI;

/// <summary>
/// Lot 8E (N1): a page nobody can open is not a feature. Every @page route must be the target
/// of a link (href) or a NavigateTo call somewhere else in the app.
/// </summary>
public class PageReachabilityTests
{
    // Reached by the lock flow, first launch or a vault failure, never from a link.
    private static readonly HashSet<string> EntryRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "unlock", "onboarding", "vault-error"
    };

    private static readonly Regex PageDirective = new(@"^@page\s+""(?<route>[^""]*)""", RegexOptions.Multiline);
    private static readonly Regex LinkTarget = new(
        @"(?:href\s*=\s*""|NavigateTo\(\s*\$?"")/?(?<target>[^""?#]*)",
        RegexOptions.Compiled);

    [Fact]
    public void AllPagesAreReachable()
    {
        var appDir = FindAppDirectory();
        var pages = Directory.GetFiles(Path.Combine(appDir, "Components"), "*.razor", SearchOption.AllDirectories);
        var sources = pages
            .Concat(Directory.GetFiles(Path.Combine(appDir, "Components"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(appDir, "Services"), "*.cs", SearchOption.AllDirectories))
            .ToList();

        var orphans = new List<string>();
        foreach (var page in pages)
        {
            foreach (Match directive in PageDirective.Matches(File.ReadAllText(page)))
            {
                var route = directive.Groups["route"].Value.Trim('/');
                if (EntryRoutes.Contains(route))
                {
                    continue;
                }

                // "tontines/{TontineId:int}" is reached by any link that starts with "tontines/".
                var parameter = route.IndexOf('{');
                var prefix = parameter >= 0 ? route[..parameter] : route;
                bool exactOnly = parameter < 0;

                bool reached = sources
                    .Where(source => source != page)
                    .SelectMany(source => LinkTarget.Matches(File.ReadAllText(source)))
                    .Select(m => m.Groups["target"].Value.Trim())
                    .Any(target => exactOnly
                        ? string.Equals(target.TrimEnd('/'), route, StringComparison.OrdinalIgnoreCase)
                        : target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                if (!reached)
                {
                    orphans.Add($"{Path.GetFileName(page)} -> /{route}");
                }
            }
        }

        Assert.True(orphans.Count == 0, "Pages without any link: " + string.Join(", ", orphans));
    }

    [Fact]
    public void Scanner_FindsTheKnownRoutes()
    {
        var pages = Directory.GetFiles(Path.Combine(FindAppDirectory(), "Components"), "*.razor", SearchOption.AllDirectories);
        var routes = pages
            .SelectMany(p => PageDirective.Matches(File.ReadAllText(p)))
            .Select(m => m.Groups["route"].Value)
            .ToList();

        Assert.Contains("/settings", routes);
        Assert.Contains("/tontines", routes);
    }

    private static string FindAppDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "SmartBudgetML", "Components");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "SmartBudgetML");
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("SmartBudgetML/Components not found above " + AppContext.BaseDirectory);
    }
}
