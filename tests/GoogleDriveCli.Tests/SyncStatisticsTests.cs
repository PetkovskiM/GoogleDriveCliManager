using GoogleDriveCli.Models;

namespace GoogleDriveCli.Tests;

/// <summary>
/// Stress tests that exercise <see cref="SyncStatistics"/> from many concurrent
/// workers and assert exact counts. The whole point of the Interlocked-based
/// counters in <c>SyncStatistics</c> is that these assertions hold — if the
/// counters were plain <c>long</c> fields the totals would be lower than
/// expected under contention because of lost updates.
/// </summary>
public class SyncStatisticsTests
{
    [Fact]
    public async Task RecordSuccess_FromManyThreadsConcurrently_TotalsAreExact()
    {
        // ARRANGE
        var stats = new SyncStatistics();
        const int callsPerWorker = 10_000;
        const int workers = 16;
        const long bytesPerCall = 1024;

        // ACT — 16 worker tasks each invoke RecordSuccess 10 000 times,
        // hammering Interlocked.Increment and Interlocked.Add on shared fields.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, workers),
            new ParallelOptions { MaxDegreeOfParallelism = workers },
            async (_, _) =>
            {
                for (var i = 0; i < callsPerWorker; i++)
                    stats.RecordSuccess(bytesPerCall);
                await Task.Yield();
            });

        // ASSERT — exact equality only holds because every increment is atomic.
        Assert.Equal(callsPerWorker * workers, stats.Success);
        Assert.Equal((long)callsPerWorker * workers * bytesPerCall, stats.TotalBytes);
    }

    [Fact]
    public async Task RecordFailure_FromManyThreadsConcurrently_AllFailuresCapturedAndCounted()
    {
        // ARRANGE
        var stats = new SyncStatistics();
        const int totalFailures = 5_000;

        // ACT — every iteration registers a distinct file id so we can verify
        // that no failure record was dropped by the ConcurrentBag.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalFailures),
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            async (i, _) =>
            {
                stats.RecordFailure($"id-{i}", $"file-{i}", "test");
                await Task.Yield();
            });

        // ASSERT — count, bag size, and distinctness all agree.
        Assert.Equal(totalFailures, stats.Failure);
        Assert.Equal(totalFailures, stats.Failures.Count);
        Assert.Equal(totalFailures, stats.Failures.Select(f => f.FileId).Distinct().Count());
    }
}
