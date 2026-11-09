using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AttendanceSystem.Database;

/// <summary>
/// Signable, short-lived **synthetic test review**. No institution credentials,
/// durable key trust, approval service or real-data migration authority exists.
/// </summary>
internal sealed record SyntheticMigrationReviewRequest(
    string Format,
    string Purpose,
    string SourceSha256,
    int SourceSchemaVersion,
    int TargetSchemaVersion,
    string Nonce,
    long IssuedAtUnixSeconds,
    long ExpiresAtUnixSeconds);

internal sealed record SyntheticMigrationEndorsement(
    string Role, string KeyId, byte[] Signature);

internal sealed record SyntheticMigrationReviewEnvelope(
    SyntheticMigrationReviewRequest Request,
    IReadOnlyList<SyntheticMigrationEndorsement> Endorsements);

/// <summary>
/// In-memory nonce tracking is intentionally ephemeral: an offline CI smoke
/// control against accidental replay, NOT a production replay ledger.
/// </summary>
internal interface ISyntheticReviewNonceLedger
{
    bool TryConsume(string key);
}

internal sealed class SyntheticReviewNonceLedger : ISyntheticReviewNonceLedger
{
    private readonly HashSet<string> consumed = new(StringComparer.Ordinal);

    public bool TryConsume(string key)
    {
        lock (consumed)
            return consumed.Add(key);
    }
}

internal static class SyntheticMigrationApprovalGate
{
    internal const string Format = "attendance-synthetic-migration-approval-v1";
    internal const string Purpose = "offline-fixture-recovery-rehearsal-only";
    internal const string PrivacyRole = "privacy-security-test-reviewer";
    internal const string OperationsRole = "operations-test-reviewer";

    // No production trust anchors exist in this application. The only trusted
    // keys are separately generated in memory by the disposable CI test harness.
    internal static void VerifyAndConsume(
        SyntheticMigrationReviewEnvelope? envelope,
        string actualFixtureSha256,
        int actualSchemaVersion,
        SyntheticReviewerTrustPolicy? trustedReviewerRegistry,
        ISyntheticReviewNonceLedger? replayLedger,
        DateTimeOffset nowUtc)
    {
        if (envelope is null || trustedReviewerRegistry is null ||
            replayLedger is null || envelope.Request is null)
            Deny();

        var request = envelope!.Request;
        if (request.Format != Format ||
            request.Purpose != Purpose ||
            request.TargetSchemaVersion != DatabaseSetup.CurrentSchemaVersion ||
            request.SourceSchemaVersion != actualSchemaVersion ||
            actualSchemaVersion is not (0 or 1) ||
            !IsHex(request.SourceSha256, 64) ||
            !IsHex(actualFixtureSha256, 64) ||
            !string.Equals(request.SourceSha256, actualFixtureSha256,
                           StringComparison.OrdinalIgnoreCase) ||
            !IsHex(request.Nonce, 32))
            Deny();

        var now = nowUtc.ToUnixTimeSeconds();
        if (request.IssuedAtUnixSeconds > now + 60 ||
            request.IssuedAtUnixSeconds < now - 900 ||
            request.ExpiresAtUnixSeconds <= now ||
            request.ExpiresAtUnixSeconds - request.IssuedAtUnixSeconds > 900 ||
            request.ExpiresAtUnixSeconds <= request.IssuedAtUnixSeconds)
            Deny();

        if (envelope.Endorsements is null ||
            envelope.Endorsements.Count != 2)
            Deny();

        var seenRoles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endorsement in envelope.Endorsements)
        {
            if (endorsement is null ||
                !seenRoles.Add(endorsement.Role) ||
                endorsement.Role is not (PrivacyRole or OperationsRole) ||
                !IsHex(endorsement.KeyId, 64) ||
                endorsement.Signature is null ||
                endorsement.Signature.Length != 64)
                Deny();

            try
            {
                var trustedPublicKey = trustedReviewerRegistry!.Resolve(
                    endorsement.Role, endorsement.KeyId, nowUtc);

                using var verifier = ECDsa.Create();
                verifier.ImportSubjectPublicKeyInfo(trustedPublicKey, out int read);
                if (read != trustedPublicKey.Length || verifier.KeySize != 256 ||
                    !verifier.VerifyData(
                        CanonicalMessage(request, endorsement.Role, endorsement.KeyId),
                        endorsement.Signature,
                        HashAlgorithmName.SHA256,
                        DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                    Deny();
            }
            catch (CryptographicException) { Deny(); }
            catch (ArgumentException) { Deny(); }
        }

        if (seenRoles.Count != 2)
            Deny();

        // Only a valid, fully signed synthetic request can consume its nonce.
        // The original DB is still immutable and public upgrades stay blocked.
        var replayKey = request.SourceSha256.ToUpperInvariant() + ":" +
                        request.SourceSchemaVersion.ToString(CultureInfo.InvariantCulture) +
                        ":" + request.Nonce.ToUpperInvariant();
        if (!replayLedger!.TryConsume(replayKey))
            Deny();
    }

    internal static byte[] CanonicalMessage(
        SyntheticMigrationReviewRequest request, string role, string keyId)
    {
        // Exact, domain-separated, culture-independent ASCII fields.
        // Each field is constrained above before verification; signers in CI
        // call this same method to create only disposable synthetic signatures.
        var value = string.Join("\n", new[]
        {
            "ATTENDANCE_SYNTHETIC_APPROVAL_V1",
            request.Format,
            request.Purpose,
            request.SourceSha256.ToUpperInvariant(),
            request.SourceSchemaVersion.ToString(CultureInfo.InvariantCulture),
            request.TargetSchemaVersion.ToString(CultureInfo.InvariantCulture),
            request.Nonce.ToUpperInvariant(),
            request.IssuedAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
            request.ExpiresAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
            role,
            keyId.ToUpperInvariant()
        });
        return Encoding.ASCII.GetBytes(value);
    }

    private static bool IsHex(string? input, int length) =>
        input is not null &&
        input.Length == length &&
        input.All(Uri.IsHexDigit);

    [DoesNotReturn]
    private static void Deny() =>
        throw new InvalidOperationException(
            "Synthetic review verification failed; no migration permitted.");
}
