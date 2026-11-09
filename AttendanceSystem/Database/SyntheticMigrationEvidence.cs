using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AttendanceSystem.Database;

/// <summary>
/// Optional GitHub Actions synthetic-only engineering receipt. It intentionally
/// exports no SQL, row values, fixture paths, personal IDs or data-derived hashes.
/// A SHA-256 checksum is corruption detection, NEVER a digital signature.
/// </summary>
internal static class SyntheticMigrationEvidence
{
    internal static void Write(IReadOnlyList<MigrationRehearsalReceipt> reports,
                               string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count != 2 ||
            reports.Select(r => r.OriginalSchema).Order().SequenceEqual(new[] { 0, 1 }) == false ||
            reports.Any(r =>
                r.Format != "attendance-synthetic-migration-rehearsal-v1" ||
                r.TargetSchema != DatabaseSetup.CurrentSchemaVersion ||
                r.StudentCount != 4 || r.AttendanceCount != 3 ||
                r.HolidayCount != 2 || !r.CloneOnly || !r.OriginalUnchanged ||
                !r.PreMigrationBackupVerified ||
                !r.RollbackToOriginalSchemaVerified ||
                r.RealDataUsed || r.ProductionMigrationAuthorized ||
                r.Decision != "NO-GO"))
            throw new InvalidOperationException(
                "Synthetic rehearsal evidence incomplete; refusing receipt.");

        var full = Path.GetFullPath(destinationDirectory);
        var temp = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(full);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(parent, temp, comparison) ||
            Path.GetFileName(full) != "attendance-rc6-migration-evidence" ||
            !Directory.Exists(full))
            throw new InvalidOperationException("Invalid synthetic evidence directory.");

        var dir = new DirectoryInfo(full);
        if (dir.LinkTarget is not null ||
            dir.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            Directory.EnumerateFileSystemEntries(full).Any())
            throw new InvalidOperationException("Evidence directory must be empty and not a link.");

        var report = new
        {
            format = "attendance-synthetic-restore-evidence-v1",
            sourceProvider = "System.Data.SQLite.Core/1.0.119",
            targetProvider = "System.Data.SQLite/2.0.4",
            nativeProvider = "SourceGear.sqlite3/3.53.4",
            generatedDataOnly = true,
            noPrivateDataExported = true,
            signed = false,
            productionMigrationAuthorized = false,
            decision = "NO-GO",
            result = reports.OrderBy(r => r.OriginalSchema).ToArray(),
            limitations = new[]
            {
                "Self-reported synthetic CI evidence is not an owner approval.",
                "Temporary plaintext test backups are not approved for private data.",
                "Original real-world student data and vendor hardware were never examined."
            }
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(report,
            new JsonSerializerOptions { WriteIndented = true });
        using (var file = new FileStream(
            Path.Combine(full, "migration-rehearsal.json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            file.Write(json);
            file.Flush(flushToDisk: true);
        }
        var digest = Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();
        using var sums = new FileStream(
            Path.Combine(full, "SHA256SUMS.txt"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        sums.Write(Encoding.ASCII.GetBytes(
            digest + "  migration-rehearsal.json" + Environment.NewLine));
        sums.Flush(flushToDisk: true);
    }
}
