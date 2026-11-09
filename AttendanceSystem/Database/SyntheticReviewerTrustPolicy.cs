using System.Security.Cryptography;

namespace AttendanceSystem.Database;

/// <summary>
/// Explicit synthetic reviewer registry. Key IDs are SHA-256 fingerprints of
/// trusted SPKI DER bytes, not self-proclaimed names or uploaded identities.
/// Test signers generate all trust material in memory; no institutional root
/// of trust is wired to any migration entry point.
/// </summary>
internal sealed record SyntheticReviewerKey(
    string Role,
    string KeyId,
    byte[] SubjectPublicKeyInfo,
    long NotBeforeUnixSeconds,
    long NotAfterUnixSeconds,
    bool Revoked);

internal sealed class SyntheticReviewerTrustPolicy
{
    private readonly Dictionary<string, SyntheticReviewerKey> reviewers;

    internal SyntheticReviewerTrustPolicy(IReadOnlyList<SyntheticReviewerKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count != 2 ||
            keys.Any(k => k is null ||
                k.Role is not (SyntheticMigrationApprovalGate.PrivacyRole or
                               SyntheticMigrationApprovalGate.OperationsRole)) ||
            keys.Select(k => k.Role).Distinct(StringComparer.Ordinal).Count() != 2 ||
            keys.Select(k => k.KeyId).Distinct(StringComparer.Ordinal).Count() != 2)
            throw new InvalidOperationException("Synthetic reviewer registry is invalid.");

        // Take defensive copies so a caller cannot change public keys or
        // revocation flags while an approval is being validated.
        reviewers = keys.ToDictionary(k => k.Role,
            k => k with
            {
                SubjectPublicKeyInfo = (byte[])k.SubjectPublicKeyInfo.Clone()
            }, StringComparer.Ordinal);
        foreach (var key in reviewers.Values)
        {
            if (key.SubjectPublicKeyInfo.Length is < 64 or > 200 ||
                key.KeyId != Fingerprint(key.SubjectPublicKeyInfo) ||
                key.NotBeforeUnixSeconds >= key.NotAfterUnixSeconds)
                throw new InvalidOperationException("Invalid reviewer trust anchor.");

            try
            {
                using var verifier = ECDsa.Create();
                verifier.ImportSubjectPublicKeyInfo(key.SubjectPublicKeyInfo, out var used);
                if (used != key.SubjectPublicKeyInfo.Length || verifier.KeySize != 256)
                    throw new InvalidOperationException("Invalid reviewer P-256 key.");
            }
            catch (CryptographicException)
            {
                throw new InvalidOperationException("Reviewer public key rejected.");
            }
        }
    }

    internal byte[] Resolve(string role, string keyId, DateTimeOffset at)
    {
        if (!reviewers.TryGetValue(role, out var anchor) ||
            anchor.KeyId != keyId ||
            anchor.Revoked ||
            at.ToUnixTimeSeconds() < anchor.NotBeforeUnixSeconds ||
            at.ToUnixTimeSeconds() >= anchor.NotAfterUnixSeconds)
            throw new InvalidOperationException(
                "Synthetic reviewer key missing, revoked or outside validity.");
        return (byte[])anchor.SubjectPublicKeyInfo.Clone();
    }

    internal IReadOnlyList<SyntheticReviewerKey> Snapshot() =>
        reviewers.Values.Select(k => k with
        {
            SubjectPublicKeyInfo = (byte[])k.SubjectPublicKeyInfo.Clone()
        }).ToArray();

    internal static string Fingerprint(byte[] spki) =>
        Convert.ToHexString(SHA256.HashData(spki));
}
