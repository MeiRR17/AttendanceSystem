using System.Data.SQLite;
using System.Security.Cryptography;

namespace AttendanceSystem.Database;

/// <summary>
/// Negative controls use disposable SQLite-online-backup copies. The signed
/// checkpoint stays outside the audit database and is independently pinned by
/// the calling test harness; no real student data is used.
/// </summary>
internal static class SyntheticAuditCheckpointSmokeChecks
{
    internal static void Run(
        string fixturePath,
        SyntheticMigrationAuditChain chain,
        SyntheticSignedAuditCheckpoint checkpoint,
        byte[] externallyPinnedTestPublicKey)
    {
        SyntheticMigrationScope.Demand(fixturePath);
        var now = DateTimeOffset.UtcNow;

        void Reject(SyntheticSignedAuditCheckpoint? candidate,
                    string? source = null, byte[]? pinned = null)
        {
            var rejected = false;
            try
            {
                SyntheticAuditCheckpointVerifier.Verify(
                    candidate, pinned ?? externallyPinnedTestPublicKey,
                    source ?? chain.DatabasePath, now);
            }
            catch (InvalidOperationException) { rejected = true; }
            catch (SQLiteException) { rejected = true; }
            if (!rejected)
                throw new InvalidOperationException(
                    "Tampered synthetic audit/checkpoint unexpectedly accepted.");
        }

        SyntheticAuditCheckpointVerifier.Verify(
            checkpoint, externallyPinnedTestPublicKey, chain.DatabasePath, now);
        Reject(null);
        Reject(checkpoint with { HeadHash = new string('A', 64) });
        Reject(checkpoint with { Entries = 1 });
        Reject(checkpoint with { Decision = "GO" });
        Reject(checkpoint with { ProductionAuthorized = true });
        Reject(checkpoint with { Purpose = "real-student-attestation" });
        Reject(checkpoint with { SourceSchemaVersion = 99 });

        var badSig = Convert.FromBase64String(checkpoint.SignatureBase64);
        badSig[0] ^= 0x01;
        Reject(checkpoint with {
            SignatureBase64 = Convert.ToBase64String(badSig)
        });

        using (var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            Reject(checkpoint, pinned: stranger.ExportSubjectPublicKeyInfo());

        var folder = Path.GetDirectoryName(Path.GetFullPath(fixturePath))!;
        var rollback = Path.Combine(folder,
            "synthetic-audit-rollback-schema" +
            checkpoint.SourceSchemaVersion + ".db");
        var mutated = Path.Combine(folder,
            "synthetic-audit-tampered-schema" +
            checkpoint.SourceSchemaVersion + ".db");
        var missing = Path.Combine(folder,
            "synthetic-audit-missing-schema" +
            checkpoint.SourceSchemaVersion + ".db");
        try
        {
            // A missing audit file must NOT be silently recreated just because
            // a detached signed checkpoint exists. This catches deletion.
            Reject(checkpoint, source: missing);
            if (File.Exists(missing))
                throw new InvalidOperationException(
                    "Missing synthetic audit was unexpectedly created.");

            // Clone the complete real SQLite journal twice (WAL-safe).
            foreach (var clone in new[] { rollback, mutated })
            {
                using var source = SyntheticMigrationAuditChain.Open(
                    chain.DatabasePath, requireExisting: true);
                using var target = SyntheticMigrationAuditChain.Open(
                    clone, requireExisting: false);
                source.BackupDatabase(target, "main", "main", -1, null, 0);
            }

            using (var older = SyntheticMigrationAuditChain.Open(
                       rollback, requireExisting: true))
            using (var cmd = older.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM SyntheticAuditEntries WHERE Seq=2;";
                if (cmd.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "Unable to prepare truncated synthetic audit clone.");
            }
            // A valid prefix with only the first event must NOT match the
            // independent signed two-event head.
            Reject(checkpoint, source: rollback);

            using (var altered = SyntheticMigrationAuditChain.Open(
                       mutated, requireExisting: true))
            using (var cmd = altered.CreateCommand())
            {
                cmd.CommandText = """
                    UPDATE SyntheticAuditEntries SET Kind='tampered'
                    WHERE Seq=1;
                    """;
                if (cmd.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "Unable to prepare synthetic tamper control.");
            }
            // This catches content rewriting even if row count is unchanged.
            Reject(checkpoint, source: mutated);

            SyntheticAuditCheckpointVerifier.Verify(
                checkpoint, externallyPinnedTestPublicKey,
                chain.DatabasePath, now);
            if (chain.ReadHead().Entries != 2)
                throw new InvalidOperationException(
                    "Negative controls unexpectedly modified original audit.");
        }
        finally
        {
            foreach (var file in new[] { rollback, mutated })
            {
                foreach (var path in new[] { file, file + "-wal", file + "-shm" })
                    if (File.Exists(path))
                        File.Delete(path);
            }
        }

        Console.WriteLine(
            "Synthetic detached audit checkpoint PASS: ECDSA-P256 signature, " +
            "independently pinned TEST key, hash-linked events, deleted/rolled-back " +
            "and tampered SQLite audit rejected; checkpoint still NO-GO.");
    }
}
