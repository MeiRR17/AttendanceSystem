using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Deliberately a STANDALONE process: no reference to AttendanceSystem,
// migration internals, or sqlite. The two public-key trust pins are handed
// across a SEPARATE CI directory that is never included in evidence artifacts.
// This does NOT provide a genuine institutional trust root or remote WORM.
internal static class Program
{
    private const string CheckpointFormat = "attendance-synthetic-detached-audit-v1";
    private const string CheckpointPurpose = "synthetic-recovery-audit-checkpoint-only";
    private const string EvidenceFormat = "attendance-synthetic-restore-evidence-v1";

    private static int Main(string[] args)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("ATTENDANCE_SYNTHETIC_FIXTURES") != "true" ||
                args.Length != 3 || args[0] != "--verify" && args[0] != "--self-test")
                throw new InvalidOperationException("Synthetic-only verifier invocation denied.");
            var evidencePath = GuardFolder(args[1], "attendance-rc6-migration-evidence");
            var pinPath = GuardFolder(args[2], "attendance-rc11-witness-pins-");
            var evidence = VerifyBundle(evidencePath, pinPath);
            if (args[0] == "--self-test")
                NegativeControls(evidence);
            Console.WriteLine(args[0] == "--self-test"
                ? "SYNTHETIC_INDEPENDENT_WITNESS_NEGATIVE_CONTROLS=PASS"
                : "SYNTHETIC_INDEPENDENT_WITNESS=PASS");
            Console.WriteLine("PRODUCTION_DECISION=NO-GO; test public keys are not institutional credentials.");
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
                                     or CryptographicException or JsonException
                                     or InvalidOperationException or FormatException)
        {
            // NEVER echo a path, source identifier, user record or JSON payload.
            Console.Error.WriteLine("Synthetic evidence verification FAILED (fail closed).");
            return 1;
        }
    }

    private static string GuardFolder(string input, string nameOrPrefix)
    {
        var path = Path.GetFullPath(input);
        var temp = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var name = Path.GetFileName(path);
        if (!string.Equals(Path.GetDirectoryName(path), temp, comparison) ||
            !(nameOrPrefix.EndsWith('-')
                ? name.StartsWith(nameOrPrefix, StringComparison.Ordinal) &&
                  name.Length == nameOrPrefix.Length + 32 &&
                  name[nameOrPrefix.Length..].All(Uri.IsHexDigit)
                : name == nameOrPrefix) ||
            !Directory.Exists(path))
            Deny();
        var directory = new DirectoryInfo(path);
        if (directory.LinkTarget is not null ||
            directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            Deny();
        return path;
    }

    private static VerifiedBundle VerifyBundle(string evidencePath, string pinPath)
    {
        var expectedEvidence = new[]
        {
            "migration-rehearsal.json", "SHA256SUMS.txt",
            "audit-checkpoint-schema0.json", "audit-checkpoint-schema1.json"
        };
        var expectedPins = new[]
        {
            "audit-checkpoint-schema0.spki", "audit-checkpoint-schema1.spki"
        };
        CheckExactFiles(evidencePath, expectedEvidence);
        CheckExactFiles(pinPath, expectedPins);
        var manifestBytes = ReadBounded(Path.Combine(evidencePath, expectedEvidence[0]), 16_384);
        var checksum = File.ReadAllText(Path.Combine(evidencePath, expectedEvidence[1]),
            Encoding.ASCII).Trim();
        var expected = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant() +
                       "  migration-rehearsal.json";
        if (!string.Equals(checksum, expected, StringComparison.Ordinal))
            Deny();

        var report = Encoding.UTF8.GetString(manifestBytes);
        VerifyManifest(report);
        var verified = new VerifiedBundle(report, new Dictionary<int, string>(),
            new Dictionary<int, byte[]>());
        foreach (var schema in new[] { 0, 1 })
        {
            var checkpoint = File.ReadAllText(Path.Combine(evidencePath,
                "audit-checkpoint-schema" + schema + ".json"), Encoding.UTF8);
            var pinned = ReadBounded(Path.Combine(pinPath,
                "audit-checkpoint-schema" + schema + ".spki"), 200);
            VerifyCheckpoint(checkpoint, pinned, schema, DateTimeOffset.UtcNow);
            verified.Checkpoints.Add(schema, checkpoint);
            verified.Pins.Add(schema, pinned);
        }
        return verified;
    }

    private static void CheckExactFiles(string dir, string[] allowed)
    {
        var items = Directory.EnumerateFileSystemEntries(dir).ToArray();
        if (items.Length != allowed.Length ||
            !items.All(item => allowed.Contains(Path.GetFileName(item),
                StringComparer.Ordinal)))
            Deny();
        foreach (var name in allowed)
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path))
                Deny();
            var info = new FileInfo(path);
            if (info.LinkTarget is not null ||
                info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                Deny();
        }
    }

    private static byte[] ReadBounded(string path, int bytes)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 || file.Length > bytes)
            Deny();
        return File.ReadAllBytes(path);
    }

    private static void VerifyManifest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("format").GetString() != EvidenceFormat ||
            !root.GetProperty("generatedDataOnly").GetBoolean() ||
            !root.GetProperty("noPrivateDataExported").GetBoolean() ||
            root.GetProperty("signed").GetBoolean() ||
            root.GetProperty("productionMigrationAuthorized").GetBoolean() ||
            root.GetProperty("decision").GetString() != "NO-GO" ||
            root.GetProperty("sourceProvider").GetString() !=
                "System.Data.SQLite.Core/1.0.119" ||
            root.GetProperty("targetProvider").GetString() !=
                "System.Data.SQLite/2.0.4")
            Deny();
        var results = root.GetProperty("result").EnumerateArray().ToArray();
        if (results.Length != 2)
            Deny();
        for (var schema = 0; schema < 2; schema++)
        {
            var item = results[schema];
            if (item.GetProperty("OriginalSchema").GetInt32() != schema ||
                item.GetProperty("TargetSchema").GetInt32() != 4 ||
                item.GetProperty("StudentCount").GetInt32() != 4 ||
                item.GetProperty("AttendanceCount").GetInt32() != 3 ||
                item.GetProperty("HolidayCount").GetInt32() != 2 ||
                item.GetProperty("RealDataUsed").GetBoolean() ||
                item.GetProperty("ProductionMigrationAuthorized").GetBoolean() ||
                item.GetProperty("Decision").GetString() != "NO-GO")
                Deny();
            foreach (var key in new[]
            {
                "CloneOnly", "OriginalUnchanged",
                "PreMigrationBackupVerified", "RollbackToOriginalSchemaVerified",
                "SyntheticDualReviewVerified", "DurableReviewLedgerUsed",
                "OfflineRootTrustVerified", "AuditTrailRecorded"
            })
                if (!item.GetProperty(key).GetBoolean())
                    Deny();
        }
    }

    private static void VerifyCheckpoint(string json, byte[] pinned, int schema,
        DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        var x = doc.RootElement;
        var format = x.GetProperty("format").GetString();
        var purpose = x.GetProperty("purpose").GetString();
        var version = x.GetProperty("sourceSchemaVersion").GetInt32();
        var entries = x.GetProperty("entries").GetInt32();
        var head = x.GetProperty("headHash").GetString();
        var issued = x.GetProperty("issuedAtUnixSeconds").GetInt64();
        var keyId = x.GetProperty("testKeyFingerprint").GetString();
        var included = x.GetProperty("testPublicKeySpkiBase64").GetString();
        var sig = x.GetProperty("signatureBase64").GetString();
        var synthetic = x.GetProperty("syntheticOnly").GetBoolean();
        var authorized = x.GetProperty("productionAuthorized").GetBoolean();
        var decision = x.GetProperty("decision").GetString();
        if (format != CheckpointFormat || purpose != CheckpointPurpose ||
            version != schema || entries != 2 || head is null ||
            head.Length != 64 || !head.All(Uri.IsHexDigit) ||
            head.All(c => c == '0') ||
            keyId != Convert.ToHexString(SHA256.HashData(pinned)) ||
            included is null || sig is null || !synthetic || authorized ||
            decision != "NO-GO" ||
            Math.Abs(now.ToUnixTimeSeconds() - issued) > 3600)
            Deny();
        var claimedKey = Convert.FromBase64String(included!);
        var signature = Convert.FromBase64String(sig!);
        if (!claimedKey.AsSpan().SequenceEqual(pinned) || signature.Length != 64)
            Deny();

        var canonical = Encoding.UTF8.GetBytes(string.Join("\n",
            "ATTENDANCE_SYNTHETIC_DETACHED_CHECKPOINT_V1",
            format, purpose,
            version.ToString(CultureInfo.InvariantCulture),
            entries.ToString(CultureInfo.InvariantCulture),
            head, issued.ToString(CultureInfo.InvariantCulture),
            keyId, included, synthetic ? "1" : "0",
            authorized ? "1" : "0", decision));

        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(pinned, out var used);
        if (used != pinned.Length || verifier.KeySize != 256 ||
            !verifier.VerifyData(canonical, signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            Deny();
    }

    private static void NegativeControls(VerifiedBundle bundle)
    {
        void Refuse(string text, byte[] pin, int schema, DateTimeOffset at)
        {
            var denied = false;
            try { VerifyCheckpoint(text, pin, schema, at); }
            catch (Exception e) when (e is JsonException or InvalidOperationException
                                      or CryptographicException or FormatException)
            { denied = true; }
            if (!denied) Deny();
        }

        VerifyManifest(bundle.Manifest);
        var changedManifest = JsonNode.Parse(bundle.Manifest)!;
        changedManifest["productionMigrationAuthorized"] = true;
        var rejected = false;
        try { VerifyManifest(changedManifest.ToJsonString()); }
        catch (InvalidOperationException) { rejected = true; }
        if (!rejected) Deny();

        foreach (var schema in new[] { 0, 1 })
        {
            var original = bundle.Checkpoints[schema];
            var pinned = bundle.Pins[schema];
            var now = DateTimeOffset.UtcNow;
            var mutated = JsonNode.Parse(original)!;
            mutated["headHash"] = new string('A', 64);
            Refuse(mutated.ToJsonString(), pinned, schema, now);
            mutated = JsonNode.Parse(original)!;
            mutated["entries"] = 1;
            Refuse(mutated.ToJsonString(), pinned, schema, now);
            mutated = JsonNode.Parse(original)!;
            mutated["decision"] = "GO";
            Refuse(mutated.ToJsonString(), pinned, schema, now);
            mutated = JsonNode.Parse(original)!;
            mutated["productionAuthorized"] = true;
            Refuse(mutated.ToJsonString(), pinned, schema, now);
            mutated = JsonNode.Parse(original)!;
            mutated["purpose"] = "real-student-migration";
            Refuse(mutated.ToJsonString(), pinned, schema, now);
            mutated = JsonNode.Parse(original)!;
            mutated["signatureBase64"] = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(64));
            Refuse(mutated.ToJsonString(), pinned, schema, now);
            mutated = JsonNode.Parse(original)!;
            mutated["issuedAtUnixSeconds"] = now.AddHours(-3).ToUnixTimeSeconds();
            Refuse(mutated.ToJsonString(), pinned, schema, now);

            using var outsider = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Refuse(original, outsider.ExportSubjectPublicKeyInfo(), schema, now);
            Refuse(original, pinned, schema ^ 1, now);

            // A fabricated self-signed checkpoint with a different embedded
            // key is NOT trusted. This explicit negative test protects
            // against treating the checkpoint's public-key field as a root.
            mutated = JsonNode.Parse(original)!;
            var outsiderSpki = outsider.ExportSubjectPublicKeyInfo();
            mutated["testPublicKeySpkiBase64"] =
                Convert.ToBase64String(outsiderSpki);
            mutated["testKeyFingerprint"] =
                Convert.ToHexString(SHA256.HashData(outsiderSpki));
            Refuse(mutated.ToJsonString(), pinned, schema, now);
        }
    }

    private sealed record VerifiedBundle(
        string Manifest,
        Dictionary<int, string> Checkpoints,
        Dictionary<int, byte[]> Pins);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Deny() =>
        throw new InvalidOperationException("Synthetic-only witness rejected.");
}
