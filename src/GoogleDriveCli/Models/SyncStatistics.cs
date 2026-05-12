using System.Collections.Concurrent;
using System.Diagnostics;

namespace GoogleDriveCli.Models;

/// <summary>
/// Thread-safe accumulator of sync statistics. Designed to be mutated concurrently
/// from many worker tasks during a parallel download, without any locks.
/// <para>
/// Counter fields are 64-bit integers mutated only via <see cref="Interlocked"/>
/// primitives — atomic increment and add that are correct under contention from
/// any number of threads. The failure list uses <see cref="ConcurrentBag{T}"/>,
/// which is optimized for "many writers, one reader at the end" — exactly the
/// access pattern of a parallel sync.
/// </para>
/// <para>
/// Reads use <see cref="Interlocked.Read(ref long)"/> so the values remain
/// torn-read-safe on 32-bit platforms (a regular <c>long</c> read is not
/// atomic on 32-bit even though it is on 64-bit).
/// </para>
/// </summary>
public sealed class SyncStatistics
{
    private long _success;
    private long _skipped;
    private long _failure;
    private long _totalBytes;
    private readonly ConcurrentBag<FailedFile> _failures = new();
    private readonly Stopwatch _stopwatch = new();

    public void Start() => _stopwatch.Start();

    public void Stop() => _stopwatch.Stop();

    public void RecordSuccess(long bytes)
    {
        Interlocked.Increment(ref _success);
        Interlocked.Add(ref _totalBytes, bytes);
    }

    public void RecordSkipped() => Interlocked.Increment(ref _skipped);

    public void RecordFailure(string fileId, string fileName, string reason)
    {
        Interlocked.Increment(ref _failure);
        _failures.Add(new FailedFile(fileId, fileName, reason));
    }

    public long Success => Interlocked.Read(ref _success);
    public long Skipped => Interlocked.Read(ref _skipped);
    public long Failure => Interlocked.Read(ref _failure);
    public long TotalBytes => Interlocked.Read(ref _totalBytes);
    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public IReadOnlyCollection<FailedFile> Failures => _failures.ToArray();
}

public sealed record FailedFile(string FileId, string FileName, string Reason);
