namespace GoogleDriveCli.Common;

/// <summary>
/// Formats raw byte counts as human-readable strings (B / KB / MB / GB).
/// Pulled out into a single helper so the <c>sync</c> summary and the
/// <c>search</c> table can't drift in formatting.
/// </summary>
public static class ByteFormatter
{
    public static string Format(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
