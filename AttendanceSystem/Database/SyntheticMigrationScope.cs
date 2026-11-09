using System.Text.RegularExpressions;

namespace AttendanceSystem.Database;

/// <summary>
/// Test-harness-only boundary for disposable old SQLite fixtures.
/// NOT an authorization system, proof of anonymization or production migration
/// workflow: an environment variable and directory name are forgeable.
/// </summary>
internal static class SyntheticMigrationScope
{
    private static readonly Regex FolderPattern = new(
        @"^attendance-(?:db-smoke|legacy-fixture)-[0-9a-f]{32}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase);

    internal static void Demand(string path)
    {
        // Require the opt-in flag before examining a file. Never permit a
        // caller to turn the public bool into a real-student upgrade.
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ATTENDANCE_SYNTHETIC_FIXTURES"),
                "true", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Synthetic-only fixture upgrade is disabled.");

        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                "Synthetic-only fixture path is invalid.");

        // Require an exact immediate child of OS TEMP, never a path chosen
        // by an arbitrary external caller. No user paths enter any messages.
        string fullPath;
        string tempPath;
        try
        {
            fullPath = Path.GetFullPath(path);
            tempPath = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException(
                "Synthetic-only fixture path is invalid.");
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var folder = Path.GetDirectoryName(fullPath);
        var parent = folder is null ? null : Path.GetDirectoryName(folder);
        if (folder is null ||
            !string.Equals(parent, tempPath, comparison) ||
            !FolderPattern.IsMatch(Path.GetFileName(folder)) ||
            !Directory.Exists(folder) ||
            !File.Exists(fullPath))
            throw new InvalidOperationException(
                "Synthetic-only fixture must already exist directly in OS temporary storage.");

        // Reject links/junctions that can move a synthetically named path
        // outside its disposable directory. This is a defense-in-depth test
        // guard, not a race-free sandbox against an adversarial local admin.
        try
        {
            var directoryInfo = new DirectoryInfo(folder);
            var fileInfo = new FileInfo(fullPath);
            if (directoryInfo.LinkTarget is not null ||
                directoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                fileInfo.LinkTarget is not null ||
                fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException(
                    "Links and junctions are not valid synthetic fixtures.");
        }
        catch (IOException)
        {
            throw new InvalidOperationException(
                "Synthetic-only fixture metadata is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Synthetic-only fixture metadata is inaccessible.");
        }
    }
}
