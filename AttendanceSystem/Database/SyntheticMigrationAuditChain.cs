using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AttendanceSystem.Database;

/// <summary>
/// Hash-linked, fixed-event audit of **synthetic-only** SQLite recoveries.
/// Its local database can be replaced by a privileged actor; the independent
/// signed checkpoint is needed to detect a complete file rollback/deletion.
/// </summary>
internal sealed class SyntheticMigrationAuditChain
{
    internal const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";
    private readonly string path;

    internal string DatabasePath => path;

    internal SyntheticMigrationAuditChain(string fixturePath)
    {
        SyntheticMigrationScope.Demand(fixturePath);
        var name = Path.GetFileName(fixturePath);
        var suffix = name switch
        {
            "legacy-unversioned.db" => "schema0",
            "legacy-schema-v1.db" => "schema1",
            _ => throw new InvalidOperationException("Unknown synthetic audit fixture.")
        };
        path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(fixturePath))!,
            "synthetic-migration-audit-" + suffix + ".db");

        using var db = Open(path, requireExisting: false);
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS SyntheticAuditEntries (
                Seq INTEGER PRIMARY KEY NOT NULL CHECK(Seq > 0),
                Kind TEXT NOT NULL,
                OccurredAtUnixSeconds INTEGER NOT NULL,
                PrevHash TEXT NOT NULL,
                EntryHash TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    internal SyntheticAuditHead Append(string kind)
    {
        if (kind is not ("synthetic-review-accepted" or "synthetic-rollback-verified"))
            throw new InvalidOperationException("Unrecognized synthetic audit event.");

        using var db = Open(path, requireExisting: true);
        using var tx = db.BeginTransaction(IsolationLevel.Serializable);
        // Verify the complete chain before appending, not just its final row.
        var current = ReadHead(db, tx);
        var seq = current.Entries + 1;
        var when = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var digest = HashEntry(seq, kind, when, current.LastHash);
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO SyntheticAuditEntries
            (Seq,Kind,OccurredAtUnixSeconds,PrevHash,EntryHash)
            VALUES (@seq,@kind,@time,@previous,@current);
            """;
        cmd.Parameters.AddWithValue("@seq", seq);
        cmd.Parameters.AddWithValue("@kind", kind);
        cmd.Parameters.AddWithValue("@time", when);
        cmd.Parameters.AddWithValue("@previous", current.LastHash);
        cmd.Parameters.AddWithValue("@current", digest);
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Synthetic audit append rejected.");
        tx.Commit();
        return new SyntheticAuditHead(seq, digest);
    }

    internal SyntheticAuditHead ReadHead() =>
        ReadHeadFromFile(path);

    internal static SyntheticAuditHead ReadHeadFromFile(string databasePath)
    {
        using var db = Open(databasePath, requireExisting: true);
        return ReadHead(db);
    }

    private static SyntheticAuditHead ReadHead(
        SQLiteConnection db, SQLiteTransaction? transaction = null)
    {
        using var integrity = db.CreateCommand();
        integrity.Transaction = transaction;
        integrity.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(integrity.ExecuteScalar()?.ToString(),
                "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Synthetic audit SQLite integrity failed.");

        using var cmd = db.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT Seq,Kind,OccurredAtUnixSeconds,PrevHash,EntryHash
            FROM SyntheticAuditEntries ORDER BY Seq;
            """;
        using var rows = cmd.ExecuteReader();
        var sequence = 0;
        var expectedPrev = Genesis;
        while (rows.Read())
        {
            var seq = rows.GetInt32(0);
            var kind = rows.GetString(1);
            var when = rows.GetInt64(2);
            var previous = rows.GetString(3);
            var storedHash = rows.GetString(4);
            if (seq != sequence + 1 ||
                kind is not ("synthetic-review-accepted" or "synthetic-rollback-verified") ||
                previous != expectedPrev ||
                storedHash != HashEntry(seq, kind, when, previous))
                throw new InvalidOperationException(
                    "Synthetic audit hash chain invalid.");
            sequence = seq;
            expectedPrev = storedHash;
        }
        return new SyntheticAuditHead(sequence, expectedPrev);
    }

    internal static string HashEntry(
        int sequence, string kind, long unixSeconds, string priorHash)
    {
        var payload = string.Join("\n",
            "ATTENDANCE_SYNTHETIC_AUDIT_ENTRY_V1",
            sequence.ToString(CultureInfo.InvariantCulture),
            kind,
            unixSeconds.ToString(CultureInfo.InvariantCulture),
            priorHash);
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(payload)));
    }

    internal static SQLiteConnection Open(string file, bool requireExisting)
    {
        if (requireExisting && !File.Exists(file))
            throw new InvalidOperationException(
                "Synthetic audit file missing: checkpoint verification blocked.");
        if (File.Exists(file))
        {
            var info = new FileInfo(file);
            if (info.LinkTarget is not null ||
                info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException(
                    "Synthetic audit symlink/junction refused.");
        }
        var conn = new SQLiteConnection(new SQLiteConnectionStringBuilder
        {
            DataSource = file, Version = 3, FailIfMissing = requireExisting
        }.ToString());
        conn.Open();
        return conn;
    }
}

internal sealed record SyntheticAuditHead(int Entries, string LastHash);
