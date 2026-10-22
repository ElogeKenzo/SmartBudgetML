using SmartBudgetML.Services.Auth;
using SmartBudgetML.Services.Security;
using SmartBudgetML.Services.SelfTest;
using Xunit;

namespace SmartBudgetML.Tests.SelfTest;

public class SelfTestRunnerTests : IDisposable
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), $"sb_selftest_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
        {
            Directory.Delete(_workDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_AllStepsPass_WritesOkLinesAndDone()
    {
        var lines = new List<string>();
        var runner = new SelfTestRunner(new InMemorySecureKeyStore(), new NoBiometrics(), _workDir, lines.Add);

        var success = await runner.RunAsync();

        Assert.True(success, string.Join(Environment.NewLine, lines));
        Assert.Equal(runner.Total, runner.Passed);
        Assert.Equal($"SB_SELFTEST|DONE|{runner.Total}/{runner.Total}", lines[^1]);

        var stepLines = lines.Take(lines.Count - 1).ToList();
        Assert.Equal(runner.Total, stepLines.Count);
        Assert.All(stepLines, line =>
        {
            var parts = line.Split('|');
            Assert.Equal(4, parts.Length);
            Assert.Equal("SB_SELFTEST", parts[0]);
            Assert.Equal("OK", parts[2]);
            Assert.True(long.TryParse(parts[3], out var ms) && ms >= 0, line);
        });
        Assert.Contains(stepLines, l => l.StartsWith("SB_SELFTEST|categorize_transport|OK|"));
        Assert.Contains(stepLines, l => l.StartsWith("SB_SELFTEST|categorize_utilities|OK|"));
        Assert.False(File.Exists(runner.DatabasePath), "self-test database must be removed by cleanup");
    }

    [Fact]
    public async Task RunAsync_WhenKeyStoreFails_ReportsFailAndIncompleteDone()
    {
        var lines = new List<string>();
        var runner = new SelfTestRunner(new FailingKeyStore(), new NoBiometrics(), _workDir, lines.Add);

        var success = await runner.RunAsync();

        Assert.False(success);
        Assert.StartsWith("SB_SELFTEST|open_encrypted_db|FAIL|", lines[0]);
        Assert.Contains("InvalidOperationException", lines[0]);
        Assert.True(runner.Passed < runner.Total);
        Assert.Equal($"SB_SELFTEST|DONE|{runner.Passed}/{runner.Total}", lines[^1]);
    }

    private sealed class NoBiometrics : IBiometricService
    {
        public Task<bool> IsAvailableAsync() => Task.FromResult(false);
        public Task<bool> AuthenticateAsync(string title, string reason) => Task.FromResult(false);
    }

    private sealed class FailingKeyStore : ISecureKeyStore
    {
        public Task<string> GetOrCreateMasterKeyAsync() =>
            throw new InvalidOperationException("secure storage unavailable");
        public Task<bool> HasMasterKeyAsync() => Task.FromResult(false);
        public Task PurgeMasterKeyAsync() => Task.CompletedTask;
    }
}
