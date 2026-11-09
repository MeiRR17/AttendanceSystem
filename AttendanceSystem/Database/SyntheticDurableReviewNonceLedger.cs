using System.Data.SQLite;
using System.Security.Cryptography;
using System.Text;

namespace AttendanceSystem.Database;

/// <summary>
/// SQLite-backed, transactional replay ledger for **synthetic** migration
/// rehearsals. The ledger survives re-instantiation and is safe against
/// duplicate insertions from independent SQLite connections. It is NOT a
/// centrally administered, tamper-resistant institutional authorization log.
/// </summary>
internal sealed class SyntheticDurableReviewNonceLedger : ISyntheticReviewNonceLedger
{
    private readonly string ledgerPath;

    internal SyntheticDurableReviewNonceLedger(string sourceFixturePath)
    {
        // Always check this existing fake fixture FIRST. The caller cannot
        // point this ledger to an arbitrary real student database or folder.
        SyntheticMigrationScope.Demand(sourceFixturePath);
        var directory = Path.GetDirectoryName(
            Path.GetFullPath(sourceFixturePath))!;
        ledgerPath = Path.Combine(directory, "synthetic-review-nonces.db");
        CheckNotLink(ledgerPath);

        using var db = Open();
        using var create = db.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS SyntheticReviewNonces (
              NonceDigest TEXT PRIMARY KEY NOT NULL,
              UsedAtUtc TEXT NOT NULL
            );
            """;
        create.ExecuteNonQuery();
    }

    public bool TryConsume(string replayKey)
    {
        if (string.IsNullOrWhiteSpace(replayKey) ||
            replayKey.Length > 256)
            throw new InvalidOperationException(
                "Synthetic replay key rejected.");
        CheckNotLink(ledgerPath);

        // No raw source file hash or nonce is stored. The primary-key
        // constraint makes replay rejection atomic across DB connections.
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.ASCII.GetBytes(replayKey)));
        using var db = Open();
        using var insert = db.CreateCommand();
        insert.CommandText = """
            INSERT OR IGNORE INTO SyntheticReviewNonces
            (NonceDigest, UsedAtUtc)
            VALUES (@digest, @time);
            """;
        insert.Parameters.AddWithValue("@digest", digest);
        insert.Parameters.AddWithValue(
            "@time", DateTimeOffset.UtcNow.ToString("O"));
        return insert.ExecuteNonQuery() == 1;
    }

    private SQLiteConnection Open()
    {
        var db = new SQLiteConnection(new SQLiteConnectionStringBuilder
        {
            DataSource = ledgerPath,
            Version = 3,
            FailIfMissing = false
        }.ToString());
        db.Open();
        return db;
    }

    private static void CheckNotLink(string path)
    {
        if (!File.Exists(path))
            return;
        var file = new FileInfo(path);
        if (file.LinkTarget is not null ||
            file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException(
                "Linked or redirected synthetic replay ledger denied.");
    }
}
