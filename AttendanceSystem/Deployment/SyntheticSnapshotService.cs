using System.Buffers.Binary;
using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AttendanceSystem.Database;

namespace AttendanceSystem.Deployment;

/// <summary>
/// Synthetic-staging backup foundation only. Copies a consistent SQLite snapshot
/// with the SQLite online backup API, encrypts it with AES-256-GCM, and verifies
/// authenticated restore before a NEW database is published. Does not overwrite.
/// Production use is blocked pending filesystem/key management and privacy approval.
/// </summary>
internal static class SyntheticSnapshotService
{
    private const int MaxSnapshotBytes = 64 * 1024 * 1024;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 8 + 1 + SaltSize + NonceSize + sizeof(long);
    private const int Pbkdf2Iterations = 310_000;
    private const string Magic = "ATTSNP01";

    internal static SyntheticSnapshotReceipt Backup(
        string sourcePath, string encryptedPath, ReadOnlySpan<char> passphrase)
    {
        ValidatePaths(sourcePath, encryptedPath, passphrase);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Approved synthetic source database is missing.");
        if (File.Exists(encryptedPath))
            throw new IOException("Refusing to replace an existing encrypted snapshot.");
        RequireExistingDirectory(encryptedPath);

        var plainTemp = Path.Combine(Path.GetTempPath(),
            "attendance-synthetic-backup-" + Guid.NewGuid().ToString("N") + ".db");
        var encryptedTemp = encryptedPath + ".partial-" + Guid.NewGuid().ToString("N");
        byte[]? plain = null;
        byte[]? key = null;
        try
        {
            using (var source = DatabaseSetup.Open(sourcePath))
            {
                ValidateDatabase(source);
                // Existing WAL state is copied consistently by SQLite's own online
                // backup API; never File.Copy() an open SQLite WAL database.
                using (var destination = DatabaseSetup.Open(plainTemp, failIfMissing: false))
                    source.BackupDatabase(destination, "main", "main", -1, null, 0);
            }
            RestrictUnixFile(plainTemp);
            using (var check = DatabaseSetup.Open(plainTemp))
                ValidateDatabase(check);

            var info = new FileInfo(plainTemp);
            if (info.Length <= 0 || info.Length > MaxSnapshotBytes)
                throw new InvalidOperationException("Snapshot exceeds bounded synthetic staging size.");
            plain = File.ReadAllBytes(plainTemp);

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var header = new byte[HeaderSize];
            Encoding.ASCII.GetBytes(Magic).CopyTo(header, 0);
            header[8] = 1;
            salt.CopyTo(header, 9);
            nonce.CopyTo(header, 9 + SaltSize);
            BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(HeaderSize - 8), plain.LongLength);

            key = Rfc2898DeriveBytes.Pbkdf2(
                passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
            var encrypted = new byte[plain.Length];
            var tag = new byte[TagSize];
            using (var aes = new AesGcm(key, TagSize))
                aes.Encrypt(nonce, plain, encrypted, tag, header);

            // Write into the destination folder and atomically rename on success.
            using (var output = new FileStream(
                encryptedTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                RestrictUnixFile(encryptedTemp);
                output.Write(header);
                output.Write(tag);
                output.Write(encrypted);
                output.Flush(flushToDisk: true);
            }
            File.Move(encryptedTemp, encryptedPath); // fails when destination exists
            return new SyntheticSnapshotReceipt(
                1, DatabaseSetup.CurrentSchemaVersion, plain.LongLength,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(encryptedPath))),
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            DeleteIfExists(plainTemp);
            DeleteIfExists(encryptedTemp);
        }
    }

    internal static void RestoreToNewDatabase(
        string encryptedPath, string destinationPath, ReadOnlySpan<char> passphrase)
    {
        ValidatePaths(encryptedPath, destinationPath, passphrase);
        RequireExistingDirectory(destinationPath);
        if (File.Exists(destinationPath))
            throw new IOException("Restore refuses to overwrite an existing database.");
        if (!File.Exists(encryptedPath))
            throw new FileNotFoundException("Encrypted snapshot missing.");

        var length = new FileInfo(encryptedPath).Length;
        if (length <= HeaderSize + TagSize ||
            length > MaxSnapshotBytes + HeaderSize + TagSize)
            throw new InvalidOperationException("Unrecognized or oversized snapshot.");

        var payload = File.ReadAllBytes(encryptedPath);
        var header = payload.AsSpan(0, HeaderSize);
        if (!header[..8].SequenceEqual(Encoding.ASCII.GetBytes(Magic)) ||
            header[8] != 1)
            throw new InvalidOperationException("Unrecognized snapshot version.");
        var declared = BinaryPrimitives.ReadInt64LittleEndian(header[(HeaderSize - 8)..]);
        if (declared <= 0 || declared > MaxSnapshotBytes ||
            declared != payload.Length - HeaderSize - TagSize)
            throw new InvalidOperationException("Invalid encrypted snapshot length.");

        var salt = header.Slice(9, SaltSize);
        var nonce = header.Slice(9 + SaltSize, NonceSize);
        var tag = payload.AsSpan(HeaderSize, TagSize);
        var ciphertext = payload.AsSpan(HeaderSize + TagSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        var plain = new byte[checked((int)declared)];
        var temporaryDatabase = destinationPath + ".restore-" + Guid.NewGuid().ToString("N");
        try
        {
            // Authenticated header and contents. Wrong passphrase or any tamper
            // throws before any destination DB is written.
            using (var aes = new AesGcm(key, TagSize))
                aes.Decrypt(nonce, ciphertext, tag, plain, header);

            using (var output = new FileStream(
                temporaryDatabase, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                RestrictUnixFile(temporaryDatabase);
                output.Write(plain);
                output.Flush(flushToDisk: true);
            }

            using (var db = DatabaseSetup.Open(temporaryDatabase))
                ValidateDatabase(db);
            File.Move(temporaryDatabase, destinationPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
            DeleteIfExists(temporaryDatabase);
        }
    }

    private static void ValidatePaths(
        string input, string output, ReadOnlySpan<char> passphrase)
    {
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Source and destination paths are required.");
        if (string.Equals(Path.GetFullPath(input), Path.GetFullPath(output),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                    StringComparison.Ordinal))
            throw new ArgumentException("Input and output must be distinct.");
        if (passphrase.Length < 20)
            throw new ArgumentException("Use a strong externally supplied 20+ character passphrase.");
    }

    private static void ValidateDatabase(SQLiteConnection db)
    {
        if (DatabaseSetup.ReadVersion(db) != DatabaseSetup.CurrentSchemaVersion)
            throw new InvalidOperationException("Synthetic backup requires exact current schema.");
        using (var check = db.CreateCommand())
        {
            check.CommandText = "PRAGMA integrity_check;";
            if (!string.Equals(
                    Convert.ToString(check.ExecuteScalar(), CultureInfo.InvariantCulture),
                    "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SQLite integrity check did not pass.");
        }
        using (var fk = db.CreateCommand())
        {
            fk.CommandText = "PRAGMA foreign_key_check;";
            using var rows = fk.ExecuteReader();
            if (rows.Read())
                throw new InvalidOperationException("Foreign-key violations in SQLite snapshot.");
        }
    }

    private static void RequireExistingDirectory(string path)
    {
        if (!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(path))))
            throw new DirectoryNotFoundException("Snapshot target directory missing.");
    }

    private static void RestrictUnixFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        // Windows staging must have a separately reviewed private NTFS ACL.
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        // Deleting a plaintext temp file is NOT cryptographic secure erasure.
    }
}

internal sealed record SyntheticSnapshotReceipt(
    int FormatVersion, int SchemaVersion, long DatabaseBytes,
    string EncryptedSha256, string CreatedAtUtc);
