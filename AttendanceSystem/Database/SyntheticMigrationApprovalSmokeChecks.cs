using System.Security.Cryptography;

namespace AttendanceSystem.Database;

/// <summary>
/// Ephemeral test signers. Keys never exist in the shipping deployment,
/// repository, environment secrets, GitHub artifacts or review receipts.
/// </summary>
internal sealed class SyntheticMigrationApprovalTestSigners : IDisposable
{
    private readonly ECDsa privacy = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa operations = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly SyntheticReviewerKey[] anchors;

    internal SyntheticReviewerTrustPolicy TrustPolicy => new(anchors);

    internal SyntheticMigrationApprovalTestSigners()
    {
        var at = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        anchors =
        [
            MakeKey(SyntheticMigrationApprovalGate.PrivacyRole, privacy, at),
            MakeKey(SyntheticMigrationApprovalGate.OperationsRole, operations, at)
        ];
    }

    private static SyntheticReviewerKey MakeKey(string role, ECDsa signer, long now)
    {
        var publicKey = signer.ExportSubjectPublicKeyInfo();
        return new SyntheticReviewerKey(
            role, SyntheticReviewerTrustPolicy.Fingerprint(publicKey),
            publicKey, now - 60, now + 1800, Revoked: false);
    }

    internal SyntheticReviewerTrustPolicy Alter(
        string role, Func<SyntheticReviewerKey, SyntheticReviewerKey> change) =>
        new(anchors.Select(k => k.Role == role ? change(k) : k).ToArray());

    internal SyntheticMigrationReviewRequest NewRequest(
        string sha, int sourceSchema, DateTimeOffset now)
    {
        var issued = now.ToUnixTimeSeconds();
        return new SyntheticMigrationReviewRequest(
            Format: SyntheticMigrationApprovalGate.Format,
            Purpose: SyntheticMigrationApprovalGate.Purpose,
            SourceSha256: sha.ToUpperInvariant(),
            SourceSchemaVersion: sourceSchema,
            TargetSchemaVersion: DatabaseSetup.CurrentSchemaVersion,
            Nonce: Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            IssuedAtUnixSeconds: issued,
            ExpiresAtUnixSeconds: issued + 300);
    }

    internal SyntheticMigrationReviewEnvelope Sign(
        SyntheticMigrationReviewRequest request)
    {
        var privacyId = anchors[0].KeyId;
        var opsId = anchors[1].KeyId;
        return new(request,
        [
            new SyntheticMigrationEndorsement(
                SyntheticMigrationApprovalGate.PrivacyRole, privacyId,
                privacy.SignData(
                    SyntheticMigrationApprovalGate.CanonicalMessage(
                        request, SyntheticMigrationApprovalGate.PrivacyRole, privacyId),
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
            new SyntheticMigrationEndorsement(
                SyntheticMigrationApprovalGate.OperationsRole, opsId,
                operations.SignData(
                    SyntheticMigrationApprovalGate.CanonicalMessage(
                        request, SyntheticMigrationApprovalGate.OperationsRole, opsId),
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        ]);
    }

    public void Dispose()
    {
        privacy.Dispose();
        operations.Dispose();
    }
}

internal static class SyntheticMigrationApprovalSmokeChecks
{
    internal static void Run(
        string sourceSha, int schema,
        SyntheticMigrationApprovalTestSigners signers, string fixturePath)
    {
        var now = DateTimeOffset.UtcNow;
        var request = signers.NewRequest(sourceSha, schema, now);
        var valid = signers.Sign(request);
        var trust = signers.TrustPolicy;

        void Reject(SyntheticMigrationReviewEnvelope? candidate,
                    SyntheticReviewerTrustPolicy? policy = null,
                    ISyntheticReviewNonceLedger? ledger = null)
        {
            var rejected = false;
            try
            {
                SyntheticMigrationApprovalGate.VerifyAndConsume(
                    candidate, sourceSha, schema, policy ?? trust,
                    ledger ?? new SyntheticReviewNonceLedger(), now);
            }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected)
                throw new InvalidOperationException(
                    "Synthetic review negative control incorrectly accepted.");
        }

        Reject(null);
        Reject(valid with { Endorsements = [] });
        Reject(valid with { Endorsements = [valid.Endorsements[0]] });
        Reject(valid with
        {
            Endorsements = [valid.Endorsements[0], valid.Endorsements[0]]
        });
        Reject(valid with
        {
            Endorsements =
            [
                new SyntheticMigrationEndorsement(
                    "untrusted-role", valid.Endorsements[0].KeyId,
                    valid.Endorsements[0].Signature),
                valid.Endorsements[1]
            ]
        });

        var alteredSignature = (byte[])valid.Endorsements[0].Signature.Clone();
        alteredSignature[0] ^= 0x01;
        Reject(valid with
        {
            Endorsements =
            [
                valid.Endorsements[0] with { Signature = alteredSignature },
                valid.Endorsements[1]
            ]
        });
        Reject(valid with
        {
            Endorsements =
            [
                valid.Endorsements[0] with { KeyId = valid.Endorsements[1].KeyId },
                valid.Endorsements[1]
            ]
        });

        Reject(signers.Sign(request with { SourceSha256 = new string('0', 64) }));
        Reject(signers.Sign(request with { SourceSchemaVersion = 99 }));
        Reject(signers.Sign(request with { TargetSchemaVersion = 3 }));
        Reject(signers.Sign(request with { Purpose = "real-student-migration" }));
        Reject(signers.Sign(request with { Nonce = "INVALID" }));
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

        // A revoked key cannot authorize, even though its signature verifies.
        Reject(valid, signers.Alter(
            SyntheticMigrationApprovalGate.PrivacyRole,
            key => key with { Revoked = true }));
        Reject(valid, signers.Alter(
            SyntheticMigrationApprovalGate.PrivacyRole,
            key => key with { NotAfterUnixSeconds = now.ToUnixTimeSeconds() - 1 }));
        Reject(valid, signers.Alter(
            SyntheticMigrationApprovalGate.OperationsRole,
            key => key with { NotBeforeUnixSeconds = now.ToUnixTimeSeconds() + 100 }));

        // A new valid but not trust-pinned key cannot replace the operator.
        using (var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            var replacement = stranger.ExportSubjectPublicKeyInfo();
            Reject(valid, signers.Alter(
                SyntheticMigrationApprovalGate.OperationsRole,
                key => key with
                {
                    SubjectPublicKeyInfo = replacement,
                    KeyId = SyntheticReviewerTrustPolicy.Fingerprint(replacement)
                }));
        }

        // In-memory test replay denial still works.
        var memory = new SyntheticReviewNonceLedger();
        SyntheticMigrationApprovalGate.VerifyAndConsume(
            valid, sourceSha, schema, trust, memory, now);
        Reject(valid, trust, memory);

        // NEW: survive independent ledger re-instantiation and file reopening.
        var durable = new SyntheticDurableReviewNonceLedger(fixturePath);
        var durableRequest = signers.Sign(
            signers.NewRequest(sourceSha, schema, DateTimeOffset.UtcNow));
        SyntheticMigrationApprovalGate.VerifyAndConsume(
            durableRequest, sourceSha, schema, trust, durable, now);
        var reopened = new SyntheticDurableReviewNonceLedger(fixturePath);
        Reject(durableRequest, trust, reopened);

        // A different nonce still works after reopening, proving the durable
        // unique constraint does not block independent valid test reviews.
        var distinct = signers.Sign(
            signers.NewRequest(sourceSha, schema, DateTimeOffset.UtcNow));
        SyntheticMigrationApprovalGate.VerifyAndConsume(
            distinct, sourceSha, schema, trust, reopened, now);

        Console.WriteLine(
            "Synthetic reviewer trust and durable replay PASS: key fingerprint, " +
            "key revocation/expiry/future validity, wrong-key rotation, signed role/purpose, " +
            "tamper, forged reviewer, expired approval and persisted SQLite nonce replay denied. " +
            "Test signatures and durable ledger do not authorize production.");
    }
}
