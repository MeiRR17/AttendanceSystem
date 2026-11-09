using System.Security.Cryptography;

namespace AttendanceSystem.Database;

/// <summary>
/// All private signing keys are generated in memory at test time. They are
/// neither institutional credentials nor persisted in source, logs or CI
/// artifacts. A passing fixture signature cannot authorize real migration.
/// </summary>
internal sealed class SyntheticMigrationApprovalTestSigners : IDisposable
{
    private readonly ECDsa privacy = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa operations = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    internal IReadOnlyDictionary<string, byte[]> TrustedKeys { get; }

    internal SyntheticMigrationApprovalTestSigners()
    {
        TrustedKeys = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [SyntheticMigrationApprovalGate.PrivacyRole] =
                privacy.ExportSubjectPublicKeyInfo(),
            [SyntheticMigrationApprovalGate.OperationsRole] =
                operations.ExportSubjectPublicKeyInfo()
        };
    }

    internal SyntheticMigrationReviewRequest NewRequest(
        string sourceSha, int sourceSchema, DateTimeOffset now)
    {
        var issued = now.ToUnixTimeSeconds();
        return new SyntheticMigrationReviewRequest(
            Format: SyntheticMigrationApprovalGate.Format,
            Purpose: SyntheticMigrationApprovalGate.Purpose,
            SourceSha256: sourceSha.ToUpperInvariant(),
            SourceSchemaVersion: sourceSchema,
            TargetSchemaVersion: DatabaseSetup.CurrentSchemaVersion,
            Nonce: Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            IssuedAtUnixSeconds: issued,
            ExpiresAtUnixSeconds: issued + 300);
    }

    internal SyntheticMigrationReviewEnvelope Sign(
        SyntheticMigrationReviewRequest request) =>
        new(request,
        [
            new SyntheticMigrationEndorsement(
                SyntheticMigrationApprovalGate.PrivacyRole,
                privacy.SignData(
                    SyntheticMigrationApprovalGate.CanonicalMessage(
                        request, SyntheticMigrationApprovalGate.PrivacyRole),
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
            new SyntheticMigrationEndorsement(
                SyntheticMigrationApprovalGate.OperationsRole,
                operations.SignData(
                    SyntheticMigrationApprovalGate.CanonicalMessage(
                        request, SyntheticMigrationApprovalGate.OperationsRole),
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        ]);

    public void Dispose()
    {
        privacy.Dispose();
        operations.Dispose();
    }
}

internal static class SyntheticMigrationApprovalSmokeChecks
{
    internal static void Run(
        string actualSourceSha, int sourceSchema,
        SyntheticMigrationApprovalTestSigners signers)
    {
        var now = DateTimeOffset.UtcNow;
        var request = signers.NewRequest(actualSourceSha, sourceSchema, now);
        var valid = signers.Sign(request);
        var keys = signers.TrustedKeys;

        void Reject(
            SyntheticMigrationReviewEnvelope? candidate,
            IReadOnlyDictionary<string, byte[]>? anchors = null)
        {
            var denied = false;
            try
            {
                SyntheticMigrationApprovalGate.VerifyAndConsume(
                    candidate, actualSourceSha, sourceSchema,
                    anchors ?? keys, new SyntheticReviewNonceLedger(), now);
            }
            catch (InvalidOperationException) { denied = true; }
            if (!denied)
                throw new InvalidOperationException(
                    "Approval negative control incorrectly accepted.");
        }

        Reject(null);
        Reject(valid with { Endorsements = [] }); // zero reviewers
        Reject(valid with { Endorsements = [valid.Endorsements[0]] }); // one reviewer
        Reject(valid with
        {
            Endorsements = [valid.Endorsements[0], valid.Endorsements[0]]
        }); // duplicate reviewer cannot impersonate second reviewer
        Reject(valid with
        {
            Endorsements =
            [
                new SyntheticMigrationEndorsement(
                    "unknown-role", valid.Endorsements[0].Signature),
                valid.Endorsements[1]
            ]
        });

        var modifiedSignature = (byte[])valid.Endorsements[0].Signature.Clone();
        modifiedSignature[0] ^= 0x01;
        Reject(valid with
        {
            Endorsements =
            [
                new SyntheticMigrationEndorsement(
                    SyntheticMigrationApprovalGate.PrivacyRole, modifiedSignature),
                valid.Endorsements[1]
            ]
        });

        Reject(signers.Sign(request with { SourceSha256 = new string('0', 64) }));
        Reject(signers.Sign(request with { SourceSchemaVersion = 99 }));
        Reject(signers.Sign(request with { TargetSchemaVersion = 3 }));
        Reject(signers.Sign(request with { Purpose = "live-migration" }));
        Reject(signers.Sign(request with { Nonce = "bad-nonce" }));
        Reject(signers.Sign(request with
        {
            IssuedAtUnixSeconds = now.ToUnixTimeSeconds() - 300,
            ExpiresAtUnixSeconds = now.ToUnixTimeSeconds() - 1
        }));
        Reject(signers.Sign(request with
        {
            IssuedAtUnixSeconds = now.ToUnixTimeSeconds() + 120,
            ExpiresAtUnixSeconds = now.ToUnixTimeSeconds() + 300
        }));
        Reject(signers.Sign(request with
        {
            ExpiresAtUnixSeconds = now.ToUnixTimeSeconds() + 3600
        }));

        using (var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            var wrongTrust = keys.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
            {
                [SyntheticMigrationApprovalGate.OperationsRole] =
                    stranger.ExportSubjectPublicKeyInfo()
            };
            Reject(valid, wrongTrust);
        }

        // A valid pair is accepted once by the in-memory synthetic ledger,
        // and the same signed nonce is denied on the next attempt.
        var oneTime = new SyntheticReviewNonceLedger();
        SyntheticMigrationApprovalGate.VerifyAndConsume(
            valid, actualSourceSha, sourceSchema, keys, oneTime, now);
        var reused = false;
        try
        {
            SyntheticMigrationApprovalGate.VerifyAndConsume(
                valid, actualSourceSha, sourceSchema, keys, oneTime, now);
        }
        catch (InvalidOperationException) { reused = true; }
        if (!reused)
            throw new InvalidOperationException(
                "Synthetic approval nonce was accepted twice.");

        Console.WriteLine(
            "Synthetic dual ECDSA-P256 review PASS: two ephemeral pinned keys, " +
            "role separation, signed source/schema/expiry/purpose, bad signature, " +
            "expiry, wrong key, one-reviewer, tamper and replay controls denied. " +
            "All fixture approvals remain NO-GO for production.");
    }
}
