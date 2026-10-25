using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Moq;
using SmartBudgetML.Services.Auth;
using SmartBudgetML.Services.Database;
using Xunit;
using Xunit.Abstractions;

namespace SmartBudgetML.Tests.Auth;

/// <summary>
/// N8 (lot 8C): PBKDF2 (600 000 iterations) must not run on the UI thread.
/// A single-threaded SynchronizationContext stands in for the UI thread and records
/// how long each posted callback blocks it.
/// </summary>
public class PinHashingThreadTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pinthread_test_{Guid.NewGuid():N}.db3");
    private DatabaseService _db = null!;

    public PinHashingThreadTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        _db = new DatabaseService(_dbPath);
        return _db.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        var conn = await _db.GetConnectionAsync();
        await conn.CloseAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            File.Delete(_dbPath + suffix);
        }
        Assert.False(File.Exists(_dbPath));
    }

    [Fact]
    public async Task SetAndVerifyPin_DoNotBlockTheCallingContextForTheHashDuration()
    {
        var hashTime = MeasureOneHash();
        _output.WriteLine($"PBKDF2-SHA256 {LocalAuthService.OwaspIterations} iterations on this host: {hashTime.TotalMilliseconds:0} ms");

        var auth = new LocalAuthService(_db, Mock.Of<IBiometricService>());
        using var ui = new SingleThreadContext();

        var verified = await ui.RunAsync(async () =>
        {
            await auth.SetPinAsync("849205");
            return await auth.VerifyPinAsync("849205");
        });

        _output.WriteLine($"Longest UI callback: {ui.LongestCallback.TotalMilliseconds:0} ms");
        Assert.True(verified);
        Assert.True(
            ui.LongestCallback < hashTime / 2,
            $"UI context was blocked {ui.LongestCallback.TotalMilliseconds:0} ms; one hash takes {hashTime.TotalMilliseconds:0} ms.");
    }

    private static TimeSpan MeasureOneHash()
    {
        var sw = Stopwatch.StartNew();
        Rfc2898DeriveBytes.Pbkdf2("849205", new byte[16], LocalAuthService.OwaspIterations, HashAlgorithmName.SHA256, 32);
        return sw.Elapsed;
    }

    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;
        private long _longestTicks;

        public SingleThreadContext()
        {
            _thread = new Thread(Pump) { IsBackground = true, Name = "fake-ui" };
            _thread.Start();
        }

        public TimeSpan LongestCallback => TimeSpan.FromTicks(Interlocked.Read(ref _longestTicks));

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException();

        public Task<T> RunAsync<T>(Func<Task<T>> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async _ =>
            {
                try
                {
                    tcs.SetResult(await work());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);
            return tcs.Task;
        }

        private void Pump()
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                var sw = Stopwatch.StartNew();
                callback(state);
                var ticks = sw.Elapsed.Ticks;
                if (ticks > Interlocked.Read(ref _longestTicks))
                {
                    Interlocked.Exchange(ref _longestTicks, ticks);
                }
            }
        }

        public void Dispose() => _queue.CompleteAdding();
    }
}
