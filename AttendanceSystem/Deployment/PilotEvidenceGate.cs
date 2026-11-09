namespace AttendanceSystem.Deployment;

/// <summary>
/// Read-only evidence quality gate. This cannot sign a release, activate devices,
/// enable WhatsApp or infer that a synthetic fixture is a real pilot.
/// </summary>
internal static class PilotEvidenceGate
{
    internal const int MinimumConsecutiveDays = 14;
    internal const double CaptureRateTarget = 0.995;
    internal const double MaxP95PersistenceMs = 5000.0;
    internal const double MaxRpoHours = 24.0;
    internal const double MaxRtoHours = 4.0;

    internal static PilotGateResult Evaluate(
        IReadOnlyList<PilotDayEvidence> days, PilotApprovalEvidence approvals)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(approvals);
        var blockers = new List<string>();

        if (days.Count < MinimumConsecutiveDays)
            blockers.Add("pilot_less_than_14_days");
        var ordered = days.OrderBy(d => d.Day).ToArray();
        if (ordered.Select(d => d.Day).Distinct().Count() != ordered.Length ||
            ordered.Zip(ordered.Skip(1), (left, right) =>
                right.Day.DayNumber - left.Day.DayNumber).Any(gap => gap != 1))
            blockers.Add("pilot_days_not_unique_and_consecutive");

        long accepted = 0, correctlyCaptured = 0;
        foreach (var day in ordered)
        {
            if (day.Accepted <= 0 || day.CorrectlyCaptured < 0 ||
                day.CorrectlyCaptured > day.Accepted ||
                day.LostAcknowledged < 0 ||
                double.IsNaN(day.P95PersistenceMs) ||
                double.IsInfinity(day.P95PersistenceMs) ||
                day.P95PersistenceMs < 0)
            {
                blockers.Add("pilot_evidence_invalid");
                continue;
            }
            accepted = checked(accepted + day.Accepted);
            correctlyCaptured = checked(correctlyCaptured + day.CorrectlyCaptured);
            if (!day.OperatorReconciled)
                blockers.Add("daily_reconciliation_not_signed");
            if (day.LostAcknowledged > 0)
                blockers.Add("lost_acknowledged_events");
            if (day.P95PersistenceMs >= MaxP95PersistenceMs)
                blockers.Add("pilot_p95_persistence_over_target");
        }

        var rate = accepted == 0 ? 0d : (double)correctlyCaptured / accepted;
        if (rate < CaptureRateTarget)
            blockers.Add("pilot_capture_rate_below_target");

        if (!double.IsFinite(approvals.MeasuredRpoHours) ||
            approvals.MeasuredRpoHours < 0 ||
            approvals.MeasuredRpoHours > MaxRpoHours)
            blockers.Add("rpo_measurement_not_approved");
        if (!double.IsFinite(approvals.MeasuredRtoHours) ||
            approvals.MeasuredRtoHours < 0 ||
            approvals.MeasuredRtoHours > MaxRtoHours)
            blockers.Add("rto_measurement_not_approved");

        if (!approvals.PrivacyOwnerSigned)
            blockers.Add("privacy_owner_approval_missing");
        if (!approvals.OperationsOwnerSigned)
            blockers.Add("operations_owner_approval_missing");
        if (!approvals.ProductOwnerSigned)
            blockers.Add("product_owner_approval_missing");
        if (!approvals.RealDeviceTestApproved)
            blockers.Add("physical_device_gate_missing");
        if (!approvals.SdkProvenanceApproved)
            blockers.Add("sdk_legal_provenance_missing");
        if (!approvals.BackupRestoreVerified)
            blockers.Add("restore_drill_not_verified");
        if (!approvals.OffsiteCopyVerified)
            blockers.Add("offsite_copy_not_verified");
        if (!approvals.RollbackRehearsed)
            blockers.Add("migration_rollback_not_rehearsed");
        if (!approvals.ConsentAndRetentionApproved)
            blockers.Add("consent_or_retention_unapproved");
        if (!approvals.RealMessagingDisabled)
            blockers.Add("live_messaging_must_remain_disabled");

        var unique = blockers.Distinct(StringComparer.Ordinal).Order().ToArray();
        return new PilotGateResult(
            accepted, correctlyCaptured, Math.Round(rate, 6),
            unique.Length == 0, unique,
            // An external release authority must verify source-of-truth evidence
            // and sign the real GO separately. No code path auto-authorizes GO.
            ProductionGoAuthorized: false);
    }
}

internal sealed record PilotDayEvidence(
    DateOnly Day, long Accepted, long CorrectlyCaptured,
    long LostAcknowledged, double P95PersistenceMs, bool OperatorReconciled);

internal sealed record PilotApprovalEvidence(
    bool PrivacyOwnerSigned, bool OperationsOwnerSigned, bool ProductOwnerSigned,
    bool RealDeviceTestApproved, bool SdkProvenanceApproved,
    bool BackupRestoreVerified, bool OffsiteCopyVerified,
    bool RollbackRehearsed, bool ConsentAndRetentionApproved,
    bool RealMessagingDisabled, double MeasuredRpoHours, double MeasuredRtoHours);

internal sealed record PilotGateResult(
    long AcceptedEvents, long CorrectlyCapturedEvents, double CaptureRate,
    bool ReadyForHumanEvidenceReview, IReadOnlyList<string> BlockerCodes,
    bool ProductionGoAuthorized);

internal static class PilotEvidenceSmokeChecks
{
    internal static void Run()
    {
        var first = new DateOnly(2026, 10, 1);
        var days = Enumerable.Range(0, 14)
            .Select(n => new PilotDayEvidence(first.AddDays(n),
                Accepted: 1000, CorrectlyCaptured: 999,
                LostAcknowledged: 0, P95PersistenceMs: 80,
                OperatorReconciled: true)).ToArray();
        var approvals = new PilotApprovalEvidence(
            PrivacyOwnerSigned: true,
            OperationsOwnerSigned: true,
            ProductOwnerSigned: true,
            RealDeviceTestApproved: true,
            SdkProvenanceApproved: true,
            BackupRestoreVerified: true,
            OffsiteCopyVerified: true,
            RollbackRehearsed: true,
            ConsentAndRetentionApproved: true,
            RealMessagingDisabled: true,
            MeasuredRpoHours: 2, MeasuredRtoHours: 1);

        var illustrative = PilotEvidenceGate.Evaluate(days, approvals);
        Assert(illustrative.ReadyForHumanEvidenceReview &&
               !illustrative.ProductionGoAuthorized &&
               illustrative.CaptureRate >= PilotEvidenceGate.CaptureRateTarget,
               "even fictional perfect data can NEVER authorize deployment");
        var incomplete = PilotEvidenceGate.Evaluate(days[..7], approvals);
        Assert(!incomplete.ReadyForHumanEvidenceReview &&
               incomplete.BlockerCodes.Contains("pilot_less_than_14_days"),
               "less than 14 days fails closed");
        var unreconciled = (PilotDayEvidence[])days.Clone();
        unreconciled[4] = unreconciled[4] with {
            LostAcknowledged = 1, OperatorReconciled = false,
            P95PersistenceMs = 6000
        };
        var failed = PilotEvidenceGate.Evaluate(unreconciled, approvals);
        Assert(failed.BlockerCodes.Contains("lost_acknowledged_events") &&
               failed.BlockerCodes.Contains("pilot_p95_persistence_over_target") &&
               failed.BlockerCodes.Contains("daily_reconciliation_not_signed"),
               "lost ACK, slow p95 and missing daily review block gate");

        var unsigned = PilotEvidenceGate.Evaluate(
            days, approvals with {
                PrivacyOwnerSigned = false,
                BackupRestoreVerified = false,
                RealMessagingDisabled = false,
                MeasuredRpoHours = 25,
                MeasuredRtoHours = 5
            });
        Assert(unsigned.BlockerCodes.Contains("privacy_owner_approval_missing") &&
               unsigned.BlockerCodes.Contains("restore_drill_not_verified") &&
               unsigned.BlockerCodes.Contains("live_messaging_must_remain_disabled") &&
               unsigned.BlockerCodes.Contains("rpo_measurement_not_approved") &&
               unsigned.BlockerCodes.Contains("rto_measurement_not_approved"),
               "security/legal/restore/RPO/RTO gate failures explicit");

        var gap = days.ToArray();
        gap[5] = gap[5] with { Day = gap[5].Day.AddDays(1) };
        Assert(PilotEvidenceGate.Evaluate(gap, approvals).BlockerCodes.Contains(
                   "pilot_days_not_unique_and_consecutive"),
               "non-consecutive pilot calendar blocked");
        Console.WriteLine("Synthetic pilot evidence checks passed: 14 consecutive days, " +
                          "capture target, no lost ACK, p95, RPO/RTO, daily reconciliation, " +
                          "privacy/backup approvals and permanent no-auto-GO.");
    }

    private static void Assert(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException("Pilot evidence smoke failed: " + scenario);
    }
}
