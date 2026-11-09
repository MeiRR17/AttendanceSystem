using System.Text.RegularExpressions;

namespace AttendanceSystem.Deployment;

/// <summary>
/// Preparation of a candidate review sheet, never an authority to release.
/// Boolean flags and user-provided evidence references must be checked by
/// an accountable human outside this process; even all-true cannot authorize GO.
/// </summary>
internal static class ReleaseGovernanceGate
{
    internal static readonly string[] RequiredEvidence =
    [
        "historic_git_privacy_assessment",
        "vendor_sdk_license_and_hardware_protocol",
        "institutional_retention_consent_and_guardian_rights",
        "real_oidc_and_per_student_rbac",
        "approved_sender_content_and_opt_out",
        "restored_offsite_backup_and_measured_rpo_rto",
        "approved_pilot_14_consecutive_days",
        "real_device_ack_replay_and_disconnect",
        "security_threat_model_and_tls_host_review",
        "supported_runtime_and_dependency_licenses",
        "production_service_account_and_key_custody",
        "audited_release_artifact_and_complete_sbom",
        "independent_artifact_signature_verified",
        "immutable_release_tag_and_source_commit",
        "monitoring_oncall_incident_and_rollback_drill",
        "product_owner_approval",
        "privacy_security_owner_approval",
        "operations_owner_approval"
    ];

    internal static ReleaseReviewResult Evaluate(
        string sourceSha,
        IReadOnlyDictionary<string, ReleaseGateEvidence>? suppliedEvidence)
    {
        ArgumentNullException.ThrowIfNull(sourceSha);
        var missing = new List<string>();
        if (!Regex.IsMatch(sourceSha, "^[a-fA-F0-9]{40}$", RegexOptions.CultureInvariant))
            missing.Add("invalid_source_sha");

        foreach (var requirement in RequiredEvidence)
        {
            if (suppliedEvidence is null ||
                !suppliedEvidence.TryGetValue(requirement, out var evidence) ||
                !evidence.ReviewedByAuthorizedOwner ||
                string.IsNullOrWhiteSpace(evidence.EvidenceReference) ||
                evidence.EvidenceReference.Length > 512 ||
                !IsReviewableReference(evidence.EvidenceReference))
                missing.Add(requirement);
        }

        // A CI run is not a digital signature and references/flags are not
        // independent verification. Separate formal GO is ALWAYS mandatory.
        return new ReleaseReviewResult(
            Candidate: "v1.0.0-rc",
            SourceSha: sourceSha,
            EngineeringEvidenceCompleteForHumanReview: missing.Count == 0,
            MissingRequirements: missing.AsReadOnly(),
            ProductionGoAuthorized: false,
            Decision: "NO-GO");
    }

    private static bool IsReviewableReference(string reference)
    {
        // A stable document/evidence pointer, not an uploaded payload, token,
        // free-text secret, or arbitrary remote command. It is NOT proof that
        // the document contains valid evidence; a human still verifies it.
        return reference.StartsWith("https://github.com/MeiRR17/AttendanceSystem/", StringComparison.Ordinal) ||
               reference.StartsWith("internal-ticket:", StringComparison.Ordinal);
    }
}

internal sealed record ReleaseGateEvidence(
    bool ReviewedByAuthorizedOwner, string EvidenceReference);

internal sealed record ReleaseReviewResult(
    string Candidate, string SourceSha,
    bool EngineeringEvidenceCompleteForHumanReview,
    IReadOnlyList<string> MissingRequirements,
    bool ProductionGoAuthorized,
    string Decision);

internal static class ReleaseGovernanceSmokeChecks
{
    internal static void Run()
    {
        const string sha = "0123456789abcdef0123456789abcdef01234567";

        var none = ReleaseGovernanceGate.Evaluate(sha, null);
        Verify(!none.EngineeringEvidenceCompleteForHumanReview &&
               !none.ProductionGoAuthorized &&
               none.Decision == "NO-GO" &&
               none.MissingRequirements.Count == ReleaseGovernanceGate.RequiredEvidence.Length,
               "default with no real approval must be NO-GO");

        var fakeEvidence = ReleaseGovernanceGate.RequiredEvidence.ToDictionary(
            key => key,
            key => new ReleaseGateEvidence(
                ReviewedByAuthorizedOwner: true,
                EvidenceReference: "internal-ticket:SYNTHETIC-ONLY"),
            StringComparer.Ordinal);
        var allSelfReported = ReleaseGovernanceGate.Evaluate(sha, fakeEvidence);
        Verify(allSelfReported.EngineeringEvidenceCompleteForHumanReview &&
               !allSelfReported.ProductionGoAuthorized,
               "self-declared approval cannot authorize a release");

        fakeEvidence["historic_git_privacy_assessment"] =
            new ReleaseGateEvidence(false, "internal-ticket:SYNTHETIC-ONLY");
        var missingOwner = ReleaseGovernanceGate.Evaluate(sha, fakeEvidence);
        Verify(!missingOwner.EngineeringEvidenceCompleteForHumanReview &&
               missingOwner.MissingRequirements.Contains("historic_git_privacy_assessment"),
               "unreviewed historical PII risk blocks candidate");

        fakeEvidence["historic_git_privacy_assessment"] =
            new ReleaseGateEvidence(true, "http://untrusted.example/false-proof");
        var invalidLink = ReleaseGovernanceGate.Evaluate(sha, fakeEvidence);
        Verify(!invalidLink.EngineeringEvidenceCompleteForHumanReview,
               "unreviewable evidence pointers blocked");

        Verify(ReleaseGovernanceGate.Evaluate("latest", fakeEvidence)
                   .MissingRequirements.Contains("invalid_source_sha"),
               "mutable branch ref cannot replace immutable commit SHA");

        Console.WriteLine(
            "RC release governance smoke passed: missing gate, unapproved PII, " +
            "invalid evidence and invalid source ref denied; self-reported ALL GREEN " +
            "still produces NO-GO and cannot activate production.");
    }

    private static void Verify(bool condition, string reason)
    {
        if (!condition)
            throw new InvalidOperationException("Release governance check failed: " + reason);
    }
}
