using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AttendanceSystem.Database;

/// <summary>
/// Offline-root signed policy for *synthetic test reviewers only*.
/// The actual trust anchor comes from the separate caller-supplied pinned key,
/// never from this manifest. No production trust store is configured.
/// </summary>
internal sealed record SyntheticRootSignedPolicy(
    string Format,
    string Purpose,
    long Epoch,
    long IssuedAtUnixSeconds,
    long ExpiresAtUnixSeconds,
    IReadOnlyList<SyntheticReviewerKey> Reviewers,
    byte[] Signature);

internal static class SyntheticOfflineRootTrust
{
    internal const string Format = "attendance-synthetic-offline-root-trust-v1";
    internal const string Purpose = "offline-fixture-recovery-rehearsal-only";

    internal static SyntheticReviewerTrustPolicy VerifyAndPin(
        SyntheticRootSignedPolicy? manifest,
        byte[]? pinnedRootSpki,
        SyntheticTrustEpochLedger? epochLedger,
        DateTimeOffset now)
    {
        if (manifest is null || pinnedRootSpki is null || epochLedger is null ||
            manifest.Format != Format || manifest.Purpose != Purpose ||
            manifest.Epoch < 1 || manifest.Reviewers is null ||
            manifest.Reviewers.Count != 2 || manifest.Signature is null ||
            manifest.Signature.Length != 64 || pinnedRootSpki.Length is < 64 or > 200)
            Deny();

        var nowSeconds = now.ToUnixTimeSeconds();
        if (manifest!.IssuedAtUnixSeconds > nowSeconds + 60 ||
            manifest.IssuedAtUnixSeconds < nowSeconds - 3600 ||
            manifest.ExpiresAtUnixSeconds <= nowSeconds ||
            manifest.ExpiresAtUnixSeconds - manifest.IssuedAtUnixSeconds is <= 0 or > 3600)
            Deny();

        // Construct the structurally validated policy BEFORE signing bytes:
        // a duplicate reviewer, invalid public key or an incorrect fingerprint
        // cannot be smuggled into the canonical payload.
        SyntheticReviewerTrustPolicy policy;
        try
        {
            policy = new SyntheticReviewerTrustPolicy(manifest.Reviewers);
        }
        catch (InvalidOperationException) { Deny(); }

        var canonical = CanonicalMessage(manifest!);
        try
        {
            using var root = ECDsa.Create();
            root.ImportSubjectPublicKeyInfo(pinnedRootSpki!, out var consumed);
            if (consumed != pinnedRootSpki.Length || root.KeySize != 256 ||
                !root.VerifyData(canonical, manifest.Signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                Deny();
        }
        catch (CryptographicException) { Deny(); }
        catch (ArgumentException) { Deny(); }

        // Monotonic acceptance is stored in a *separate* local synthetic SQLite
        // file; accept the same signed manifest again, but never an older epoch
        // or a different payload using an already-accepted epoch.
        var digest = Convert.ToHexString(SHA256.HashData(canonical));
        if (!epochLedger!.TryPin(manifest.Epoch, digest))
            Deny();
        return policy!;
    }

    internal static byte[] CanonicalMessage(SyntheticRootSignedPolicy manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Reviewers is null || manifest.Reviewers.Count != 2)
            Deny();

        // Fixed fields, exact SPKI bytes (base64), sorted roles, and explicit
        // line separators eliminate culture, order and delimiter ambiguity.
        var lines = new List<string>
        {
            "ATTENDANCE_SYNTHETIC_OFFLINE_ROOT_V1",
            manifest.Format,
            manifest.Purpose,
            manifest.Epoch.ToString(CultureInfo.InvariantCulture),
            manifest.IssuedAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
            manifest.ExpiresAtUnixSeconds.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var reviewer in manifest.Reviewers.OrderBy(k => k.Role, StringComparer.Ordinal))
        {
            if (reviewer is null || reviewer.SubjectPublicKeyInfo is null)
                Deny();
            lines.Add(reviewer.Role);
            lines.Add(reviewer.KeyId);
            lines.Add(Convert.ToBase64String(reviewer.SubjectPublicKeyInfo));
            lines.Add(reviewer.NotBeforeUnixSeconds.ToString(CultureInfo.InvariantCulture));
            lines.Add(reviewer.NotAfterUnixSeconds.ToString(CultureInfo.InvariantCulture));
            lines.Add(reviewer.Revoked ? "1" : "0");
        }
        return Encoding.UTF8.GetBytes(string.Join("\n", lines));
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Deny() =>
        throw new InvalidOperationException(
            "Synthetic offline trust policy rejected; no migration permitted.");
}

/// <summary>
/// Local durable epoch/fingerprint acceptance within disposable synthetic TEMP.
/// It detects downgrade/rewrite on an intact SQLite file, NOT deletion,
/// restore-from-backup, privileged tampering, distributed rollback or split brain.
/// </summary>
internal sealed class SyntheticTrustEpochLedger
{
    private readonly string path;

    internal SyntheticTrustEpochLedger(string fixturePath, bool negativeControls = false)
    {
        SyntheticMigrationScope.Demand(fixturePath);
        path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(fixturePath))!,
            negativeControls
                ? "synthetic-root-epoch-negative.db"
                : "synthetic-root-epoch.db");
        using var db = Open();
        using var setup = db.CreateCommand();
        setup.CommandText = """
            CREATE TABLE IF NOT EXISTS SyntheticTrustEpoch (
              Slot INTEGER PRIMARY KEY CHECK(Slot=1),
              Epoch INTEGER NOT NULL CHECK(Epoch > 0),
              ManifestDigest TEXT NOT NULL
            );
            """;
        setup.ExecuteNonQuery();
    }

    internal bool TryPin(long epoch, string manifestDigest)
    {
        if (epoch <= 0 || manifestDigest.Length != 64 ||
            !manifestDigest.All(Uri.IsHexDigit))
            return false;

        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO SyntheticTrustEpoch(Slot, Epoch, ManifestDigest)
            VALUES(1, @epoch, @digest)
            ON CONFLICT(Slot) DO UPDATE SET
                Epoch=excluded.Epoch,
                ManifestDigest=excluded.ManifestDigest
            WHERE excluded.Epoch > SyntheticTrustEpoch.Epoch
               OR (excluded.Epoch = SyntheticTrustEpoch.Epoch
                   AND excluded.ManifestDigest = SyntheticTrustEpoch.ManifestDigest);
            """;
        command.Parameters.AddWithValue("@epoch", epoch);
        command.Parameters.AddWithValue("@digest", manifestDigest);
        return command.ExecuteNonQuery() == 1;
    }

    private SQLiteConnection Open()
    {
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null ||
                info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException(
                    "Linked synthetic root policy ledger refused.");
        }
        var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
        {
            DataSource = path,
            Version = 3,
            FailIfMissing = false
        }.ToString());
        connection.Open();
        return connection;
    }
}
