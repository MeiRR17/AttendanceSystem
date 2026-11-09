using System.Security.Cryptography;

namespace AttendanceSystem.Database;

/// <summary>
/// Independent synthetic root signer: the private key is generated, held and
/// disposed only in the test harness. This is NOT a real institution root key.
/// </summary>
internal sealed class SyntheticOfflineRootTestSigner : IDisposable
{
    private readonly ECDsa root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    internal byte[] PinnedRootPublicKey => root.ExportSubjectPublicKeyInfo();

    internal SyntheticRootSignedPolicy Sign(
        IReadOnlyList<SyntheticReviewerKey> reviewers, long epoch,
        DateTimeOffset now)
    {
        var issued = now.ToUnixTimeSeconds();
        var unsigned = new SyntheticRootSignedPolicy(
            SyntheticOfflineRootTrust.Format,
            SyntheticOfflineRootTrust.Purpose,
            epoch,
            issued,
            issued + 900,
            reviewers.Select(k => k with
            {
                SubjectPublicKeyInfo = (byte[])k.SubjectPublicKeyInfo.Clone()
            }).ToArray(),
            []);
        var signature = root.SignData(
            SyntheticOfflineRootTrust.CanonicalMessage(unsigned),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return unsigned with { Signature = signature };
    }

    public void Dispose() => root.Dispose();
}

/// <summary>
/// Dedicated negative-control epoch file. Does not advance the epoch used by
/// the actual synthetic rehearsal: its signer authority is still test-only.
/// </summary>
internal static class SyntheticOfflineRootTrustSmokeChecks
{
    internal static void Run(
        string fixturePath,
        SyntheticMigrationApprovalTestSigners reviewers,
        SyntheticOfflineRootTestSigner root)
    {
        var now = DateTimeOffset.UtcNow;
        var members = reviewers.TrustPolicy.Snapshot();
        var signedV1 = root.Sign(members, 1, now);
        var pin = root.PinnedRootPublicKey;
        var ledger = new SyntheticTrustEpochLedger(
            fixturePath, negativeControls: true);

        void Reject(SyntheticRootSignedPolicy? candidate, byte[]? rootPin = null)
        {
            var denied = false;
            try
            {
                SyntheticOfflineRootTrust.VerifyAndPin(
                    candidate, rootPin ?? pin, ledger, now);
            }
            catch (InvalidOperationException) { denied = true; }
            if (!denied)
                throw new InvalidOperationException(
                    "Offline root policy negative control incorrectly accepted.");
        }

        Reject(null);
        Reject(signedV1 with { Signature = [] });
        Reject(signedV1 with { Epoch = 0 });
        Reject(signedV1 with { Epoch = 2 }); // epoch changed, signature retained
        Reject(signedV1 with { Format = "unrecognized" });
        Reject(signedV1 with { Purpose = "production-student-migration" });
        Reject(signedV1 with { ExpiresAtUnixSeconds = now.ToUnixTimeSeconds() - 1 });
        Reject(signedV1 with { ExpiresAtUnixSeconds = now.ToUnixTimeSeconds() + 8000 });
        Reject(signedV1 with { IssuedAtUnixSeconds = now.ToUnixTimeSeconds() + 1000 });
        Reject(signedV1 with { Reviewers = [members[0]] });
        Reject(signedV1 with { Reviewers = [members[0], members[0]] });
        Reject(signedV1 with
        {
            Reviewers = [members[0] with { Revoked = true }, members[1]]
        }); // changing revocation flag breaks signed manifest
        Reject(signedV1 with
        {
            Reviewers = [members[0] with { KeyId = new string('F', 64) }, members[1]]
        });
        var signature = (byte[])signedV1.Signature.Clone();
        signature[0] ^= 1;
        Reject(signedV1 with { Signature = signature });

        using (var outsider = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            Reject(signedV1, outsider.ExportSubjectPublicKeyInfo());

        // A valid root signature must be independently verifiable using the
        // separately supplied pin, without access to the root signing key.
        var policy = SyntheticOfflineRootTrust.VerifyAndPin(
            signedV1, pin, ledger, now);
        if (policy.Snapshot().Count != 2)
            throw new InvalidOperationException("Valid root-signed reviewer policy failed.");

        // Identical signed manifest may be reverified (not a migration replay).
        SyntheticOfflineRootTrust.VerifyAndPin(
            signedV1, pin, new SyntheticTrustEpochLedger(
                fixturePath, negativeControls: true), now);

        // A signed higher epoch supersedes older policy; an old signed epoch
        // and a fork under the same epoch are denied even after reopening DB.
        var signedV2 = root.Sign(members, 2, now);
        SyntheticOfflineRootTrust.VerifyAndPin(
            signedV2, pin, ledger, now);
        var reopened = new SyntheticTrustEpochLedger(
            fixturePath, negativeControls: true);
        var rejectedRollback = false;
        try
        {
            SyntheticOfflineRootTrust.VerifyAndPin(
                signedV1, pin, reopened, now);
        }
        catch (InvalidOperationException) { rejectedRollback = true; }
        if (!rejectedRollback)
            throw new InvalidOperationException("Older root epoch accepted after upgrade.");

        var forkedV2 = root.Sign([
            members[0] with { Revoked = true },
            members[1]], 2, now);
        var rejectedFork = false;
        try
        {
            SyntheticOfflineRootTrust.VerifyAndPin(
                forkedV2, pin, reopened, now);
        }
        catch (InvalidOperationException) { rejectedFork = true; }
        if (!rejectedFork)
            throw new InvalidOperationException("Different manifest fork at same epoch accepted.");

        Console.WriteLine(
            "Synthetic offline root trust PASS: P-256 root signature, pinned root, " +
            "reviewer fingerprints, revocation tampering, wrong root, expired policy, " +
            "version rollback and same-epoch fork refused after SQLite reopen; NO-GO.");
    }
}
