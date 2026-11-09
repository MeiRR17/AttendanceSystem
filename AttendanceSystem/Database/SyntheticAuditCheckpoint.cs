using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AttendanceSystem.Database;

/// <summary>
/// Detached synthetic checkpoint. Public key is included for inspection only;
/// a verifier MUST pin the root outside this checkpoint to establish trust.
/// A self-contained signature does NOT provide institutional attestation.
/// </summary>
internal sealed record SyntheticSignedAuditCheckpoint(
    string Format,
    string Purpose,
    int SourceSchemaVersion,
    int Entries,
    string HeadHash,
    long IssuedAtUnixSeconds,
    string TestKeyFingerprint,
    string TestPublicKeySpkiBase64,
    string SignatureBase64,
    bool SyntheticOnly,
    bool ProductionAuthorized,
    string Decision);

internal sealed class SyntheticAuditCheckpointTestSigner : IDisposable
{
    private readonly ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    internal byte[] PinnedTestPublicKey => signer.ExportSubjectPublicKeyInfo();

    internal SyntheticSignedAuditCheckpoint Sign(int schema, SyntheticAuditHead head)
    {
        if (schema is not (0 or 1) || head.Entries != 2 ||
            head.LastHash.Length != 64)
            throw new InvalidOperationException("Unsupported synthetic audit snapshot.");
        var publicKey = PinnedTestPublicKey;
        var unsigned = new SyntheticSignedAuditCheckpoint(
            SyntheticAuditCheckpointVerifier.Format,
            SyntheticAuditCheckpointVerifier.Purpose,
            schema, head.Entries, head.LastHash,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Convert.ToHexString(SHA256.HashData(publicKey)),
            Convert.ToBase64String(publicKey),
            "",
            SyntheticOnly: true,
            ProductionAuthorized: false,
            Decision: "NO-GO");
        return unsigned with
        {
            SignatureBase64 = Convert.ToBase64String(signer.SignData(
                SyntheticAuditCheckpointVerifier.Canonical(unsigned),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        };
    }

    public void Dispose() => signer.Dispose();
}

internal static class SyntheticAuditCheckpointVerifier
{
    internal const string Format = "attendance-synthetic-detached-audit-v1";
    internal const string Purpose = "synthetic-recovery-audit-checkpoint-only";

    internal static void Verify(
        SyntheticSignedAuditCheckpoint? checkpoint,
        byte[]? independentlyPinnedPublicKey,
        string auditDatabasePath,
        DateTimeOffset now)
    {
        if (checkpoint is null || independentlyPinnedPublicKey is null ||
            checkpoint.Format != Format || checkpoint.Purpose != Purpose ||
            checkpoint.SourceSchemaVersion is not (0 or 1) ||
            checkpoint.Entries != 2 || !IsHex(checkpoint.HeadHash, 64) ||
            checkpoint.HeadHash == SyntheticMigrationAuditChain.Genesis ||
            !IsHex(checkpoint.TestKeyFingerprint, 64) ||
            checkpoint.SignatureBase64 is null ||
            checkpoint.TestPublicKeySpkiBase64 is null ||
            !checkpoint.SyntheticOnly || checkpoint.ProductionAuthorized ||
            checkpoint.Decision != "NO-GO" ||
            Math.Abs(now.ToUnixTimeSeconds() - checkpoint.IssuedAtUnixSeconds) > 3600)
            Deny();

        var actualKeyId = Convert.ToHexString(
            SHA256.HashData(independentlyPinnedPublicKey!));
        if (actualKeyId != checkpoint!.TestKeyFingerprint)
            Deny();
        try
        {
            var includedPublic = Convert.FromBase64String(
                checkpoint.TestPublicKeySpkiBase64);
            // Even though a copy of a public key appears in the detached
            // checkpoint, only the EXTERNAL caller's pinned copy is trusted.
            if (!includedPublic.AsSpan().SequenceEqual(independentlyPinnedPublicKey))
                Deny();

            var signature = Convert.FromBase64String(checkpoint.SignatureBase64);
            if (signature.Length != 64)
                Deny();
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(
                independentlyPinnedPublicKey, out var bytesRead);
            if (bytesRead != independentlyPinnedPublicKey.Length ||
                verifier.KeySize != 256 ||
                !verifier.VerifyData(Canonical(checkpoint), signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                Deny();
        }
        catch (FormatException) { Deny(); }
        catch (CryptographicException) { Deny(); }
        catch (ArgumentException) { Deny(); }

        // Opening with FailIfMissing=true detects deletion, not merely a
        // corrupt row. The signed head detects complete-file rollback.
        var head = SyntheticMigrationAuditChain.ReadHeadFromFile(auditDatabasePath);
        if (head.Entries != checkpoint.Entries ||
            head.LastHash != checkpoint.HeadHash)
            Deny();
    }

    internal static byte[] Canonical(SyntheticSignedAuditCheckpoint item)
    {
        var s = string.Join("\n",
            "ATTENDANCE_SYNTHETIC_DETACHED_CHECKPOINT_V1",
            item.Format, item.Purpose,
            item.SourceSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            item.Entries.ToString(System.Globalization.CultureInfo.InvariantCulture),
            item.HeadHash,
            item.IssuedAtUnixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            item.TestKeyFingerprint,
            item.TestPublicKeySpkiBase64,
            item.SyntheticOnly ? "1" : "0",
            item.ProductionAuthorized ? "1" : "0",
            item.Decision);
        return Encoding.UTF8.GetBytes(s);
    }

    private static bool IsHex(string? s, int n) =>
        s is not null && s.Length == n && s.All(Uri.IsHexDigit);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Deny() => throw new InvalidOperationException(
        "Synthetic checkpoint integrity or trust verification denied.");
}

internal static class SyntheticAuditCheckpointEvidence
{
    /// <summary>
    /// Public disposable TEST keys are written only to a *separate* temporary
    /// CI pin directory. They are deliberately NOT included in the uploaded
    /// evidence: a signer cannot bootstrap institutional trust from its own
    /// self-reported checkpoint.
    /// </summary>
    internal static void WriteWitnessTestPins(
        string pinsDirectory, IReadOnlyDictionary<int, byte[]> publicKeys)
    {
        if (publicKeys.Count != 2 ||
            !publicKeys.Keys.Order().SequenceEqual(new[] { 0, 1 }))
            throw new InvalidOperationException("Expected two synthetic witness pins.");

        var full = Path.GetFullPath(pinsDirectory);
        var temp = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(full);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(full), temp, comparison) ||
            !name.StartsWith("attendance-rc11-witness-pins-", StringComparison.Ordinal) ||
            name.Length != "attendance-rc11-witness-pins-".Length + 32 ||
            !name["attendance-rc11-witness-pins-".Length..].All(Uri.IsHexDigit) ||
            !Directory.Exists(full))
            throw new InvalidOperationException("Synthetic witness pin location denied.");

        var info = new DirectoryInfo(full);
        if (info.LinkTarget is not null ||
            info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            Directory.EnumerateFileSystemEntries(full).Any())
            throw new InvalidOperationException("Synthetic witness pin directory must be empty.");

        foreach (var schema in new[] { 0, 1 })
        {
            var spki = publicKeys[schema];
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(spki, out var read);
            if (read != spki.Length || verifier.KeySize != 256)
                throw new InvalidOperationException("Invalid synthetic witness public key.");
            using var target = new FileStream(
                Path.Combine(full, "audit-checkpoint-schema" + schema + ".spki"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None);
            target.Write(spki);
            target.Flush(flushToDisk: true);
        }
    }

    internal static void Write(
        string evidenceDirectory,
        IReadOnlyList<SyntheticSignedAuditCheckpoint> checkpoints)
    {
        if (checkpoints.Count != 2 ||
            !checkpoints.Select(x => x.SourceSchemaVersion).Order()
                .SequenceEqual(new[] { 0, 1 }) ||
            checkpoints.Any(x => x.Entries != 2 || !x.SyntheticOnly ||
                x.ProductionAuthorized || x.Decision != "NO-GO"))
            throw new InvalidOperationException(
                "Synthetic checkpoint evidence incomplete.");

        var directory = Path.GetFullPath(evidenceDirectory);
        var temp = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(directory), temp,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                    StringComparison.Ordinal) ||
            Path.GetFileName(directory) != "attendance-rc6-migration-evidence")
            throw new InvalidOperationException(
                "Detached checkpoints must use the fixed synthetic evidence folder.");

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        foreach (var checkpoint in checkpoints)
        {
            var name = "audit-checkpoint-schema" +
                       checkpoint.SourceSchemaVersion + ".json";
            using var output = new FileStream(Path.Combine(directory, name),
                FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(output, checkpoint, options);
            output.Flush(flushToDisk: true);
        }
    }
}
