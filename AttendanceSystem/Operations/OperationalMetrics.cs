using System.Diagnostics.Metrics;

namespace AttendanceSystem.Operations;

/// <summary>
/// Low-cardinality in-process counters. Never use event, user, device, phone,
/// IP address, token, or attendance payload as a metric label. Resets on restart.
/// </summary>
public static class OperationalMetrics
{
    private static readonly Meter Meter = new("AttendanceSystem.SafeOperations", "0.9.0");
    private static readonly Counter<long> InsertedCounter =
        Meter.CreateCounter<long>("attendance.event.inserted");
    private static readonly Counter<long> ReplayCounter =
        Meter.CreateCounter<long>("attendance.event.replayed");
    private static readonly Counter<long> RejectCounter =
        Meter.CreateCounter<long>("attendance.event.rejected");
    private static readonly Counter<long> DbErrorCounter =
        Meter.CreateCounter<long>("attendance.db.write_errors");
    private static readonly Histogram<double> PersistLatency =
        Meter.CreateHistogram<double>("attendance.persist.duration", "ms");

    private static long _inserted;
    private static long _replayed;
    private static long _rejected;
    private static long _dbErrors;
    private static readonly object LatencyLock = new();
    private static readonly double[] LatencyMs = new double[512];
    private static int _latencySamples;
    private static int _latencyIndex;

    public static void RecordInserted(TimeSpan elapsed)
    {
        Interlocked.Increment(ref _inserted);
        InsertedCounter.Add(1);
        Persist(elapsed);
    }

    public static void RecordReplay(TimeSpan elapsed)
    {
        Interlocked.Increment(ref _replayed);
        ReplayCounter.Add(1);
        Persist(elapsed);
    }

    public static void RecordRejected(int statusCode)
    {
        Interlocked.Increment(ref _rejected);
        // Bounded status-code tag only; NEVER any event/device/user identifiers.
        var status = statusCode switch
        {
            401 => "unauthorized",
            403 => "disabled",
            409 => "conflict",
            413 => "oversize",
            422 => "invalid",
            429 => "limited",
            _ => "other"
        };
        RejectCounter.Add(1, new KeyValuePair<string, object?>("category", status));
    }

    public static void RecordDatabaseError()
    {
        Interlocked.Increment(ref _dbErrors);
        DbErrorCounter.Add(1);
    }

    public static OperationalMetricSnapshot Snapshot()
    {
        double p95;
        int sampleCount;
        lock (LatencyLock)
        {
            sampleCount = _latencySamples;
            if (sampleCount == 0) p95 = 0;
            else
            {
                var copy = LatencyMs.Take(sampleCount).ToArray();
                Array.Sort(copy);
                p95 = copy[Math.Max(0, (int)Math.Ceiling(copy.Length * 0.95) - 1)];
            }
        }
        return new OperationalMetricSnapshot(
            Interlocked.Read(ref _inserted),
            Interlocked.Read(ref _replayed),
            Interlocked.Read(ref _rejected),
            Interlocked.Read(ref _dbErrors),
            sampleCount == 0 ? null : Math.Round(p95, 3),
            sampleCount, "in_process_since_start");
    }

    private static void Persist(TimeSpan elapsed)
    {
        var milliseconds = Math.Max(0, elapsed.TotalMilliseconds);
        PersistLatency.Record(milliseconds);
        lock (LatencyLock)
        {
            LatencyMs[_latencyIndex] = milliseconds;
            _latencyIndex = (_latencyIndex + 1) % LatencyMs.Length;
            _latencySamples = Math.Min(LatencyMs.Length, _latencySamples + 1);
        }
    }
}

public sealed record OperationalMetricSnapshot(
    long InsertedEvents, long DuplicateReplays, long RejectedRequests,
    long DatabaseWriteErrors, double? PersistP95Milliseconds,
    int PersistSampleCount, string Scope);
