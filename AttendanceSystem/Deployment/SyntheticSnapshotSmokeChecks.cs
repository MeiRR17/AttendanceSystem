using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AttendanceSystem.Database;

namespace AttendanceSystem.Deployment;

/// <summary>
/// Disposable, synthetic-only staging backup/restore regression.
/// Never uses an existing user DB or a real institution passphrase.
/// </summary>
internal static class SyntheticSnapshotSmokeChecks
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "attendance-snapshot-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var passphrase = "SYNTHETIC-ONLY-TEST-PASSPHRASE-DO-NOT-REUSE".ToCharArray();
        try
        {
            var source = Path.Combine(root, "source.db");
            var encryptedPath = Path.Combine(root, "synthetic.attsnap");
            var restored = Path.Combine(root, "restored.db");
            var wrongPath = Path.Combine(root, "wrong-password.db");
            var changedPath = Path.Combine(root, "tampered.db");

            DatabaseSetup.InitializeDatabaseAtPath(source);
            using (var db = DatabaseSetup.Open(source))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO Devices (DeviceID,Enabled,RegisteredAtUtc)
                    VALUES('staging-device',1,'2026-11-01T00:00:00Z');
                    INSERT INTO AttendanceEvents
                        (DeviceID,EventKey,StudentID,OccurredAtUtc,ReceivedAtUtc)
                    VALUES
                        ('staging-device','immutable-test-event','SYNTHETIC-PRIVATE-MARKER',
                         '2026-11-01T08:01:00.0000000+00:00',
                         '2026-11-01T08:02:00.0000000+00:00');
                    INSERT INTO AttendanceDecisions
                        (EventID,RuleVersion,Status,EvaluatedAtUtc)
                    VALUES (1,'synthetic-v1','OnTime','2026-11-01T08:03:00Z');
                    """;
                cmd.ExecuteNonQuery();
            }

            var receipt = SyntheticSnapshotService.Backup(source, encryptedPath, passphrase);
            Check(File.Exists(encryptedPath), "ciphertext committed");
            Check(receipt.FormatVersion == 1 &&
                  receipt.SchemaVersion == DatabaseSetup.CurrentSchemaVersion &&
                  receipt.DatabaseBytes > 0, "versioned immutable backup receipt");
            var ciphertext = File.ReadAllBytes(encryptedPath);
            Check(!Encoding.UTF8.GetString(ciphertext).Contains(
                      "SYNTHETIC-PRIVATE-MARKER", StringComparison.Ordinal),
                  "ciphertext does not expose a synthetic student identifier");
            Check(receipt.EncryptedSha256 ==
                  Convert.ToHexString(SHA256.HashData(ciphertext)),
                  "ciphertext checksum matches receipt");

            TryReject(() => SyntheticSnapshotService.Backup(
                source, encryptedPath, passphrase), "cannot overwrite a snapshot");
            TryReject(() => SyntheticSnapshotService.RestoreToNewDatabase(
                encryptedPath, wrongPath, "incorrect-secret-of-sufficient-length".AsSpan()),
                "wrong key must fail authentication");
            Check(!File.Exists(wrongPath), "wrong key created no database");

            var tamperedFile = Path.Combine(root, "tampered.attsnap");
            var tamperedBytes = (byte[])ciphertext.Clone();
            tamperedBytes[^1] ^= 0x80;
            File.WriteAllBytes(tamperedFile, tamperedBytes);
            TryReject(() => SyntheticSnapshotService.RestoreToNewDatabase(
                tamperedFile, changedPath, passphrase),
                "authenticated header/payload must reject tamper");
            Check(!File.Exists(changedPath), "tamper created no database");

            SyntheticSnapshotService.RestoreToNewDatabase(
                encryptedPath, restored, passphrase);
            using (var db = DatabaseSetup.Open(restored))
            {
                Check(DatabaseSetup.ReadVersion(db) == DatabaseSetup.CurrentSchemaVersion,
                      "restored schema exact");
                using var cmd = db.CreateCommand();
                cmd.CommandText = """
                    SELECT COUNT(*) FROM AttendanceEvents
                    WHERE EventKey='immutable-test-event'
                      AND StudentID='SYNTHETIC-PRIVATE-MARKER';
                    """;
                Check(Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 1,
                      "attendance event restored without loss");
                cmd.CommandText = """
                    SELECT COUNT(*) FROM AttendanceDecisions
                    WHERE RuleVersion='synthetic-v1' AND Status='OnTime';
                    """;
                Check(Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 1,
                      "derived decision preserved");
            }
            DatabaseSetup.InitializeDatabaseAtPath(restored); // no migration on current schema
            TryReject(() => SyntheticSnapshotService.RestoreToNewDatabase(
                encryptedPath, restored, passphrase),
                "restore must refuse to overwrite an existing DB");

            var damagedSource = Path.Combine(root, "damaged.db");
            File.WriteAllBytes(damagedSource, RandomNumberGenerator.GetBytes(256));
            TryReject(() => SyntheticSnapshotService.Backup(
                damagedSource, Path.Combine(root, "bad.attsnap"), passphrase),
                "corrupt source cannot produce an approved snapshot");

            Check(Directory.GetFiles(root, "*.partial-*").Length == 0 &&
                  Directory.GetFiles(root, "*.restore-*").Length == 0,
                  "no leftover partial encrypted or plaintext restore files");
            Console.WriteLine(
                "Synthetic AES-256-GCM SQLite snapshot checks passed: WAL-safe " +
                "online backup, integrity+FK, authenticated restore, wrong-key/tamper " +
                "rejection, non-overwrite and retained event+decision.");
        }
        finally
        {
            Array.Clear(passphrase);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Check(bool valid, string description)
    {
        if (!valid)
            throw new InvalidOperationException("Snapshot test failed: " + description);
    }

    private static void TryReject(Action action, string description)
    {
        try { action(); }
        catch (CryptographicException) { return; }
        catch (SQLiteException) { return; }
        catch (IOException) { return; }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected safe rejection: " + description);
    }
}
