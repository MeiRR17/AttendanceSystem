# AttendanceSystem — Production go/no-go checklist

All boxes are **open** unless verified by a named operator with evidence, date, environment and version. Do not check a box based solely on code existence.

## P0: safety and privacy
- [ ] Public repository/old attendance DB history assessed by authorized data controller; visibility and retention decisions recorded.
- [ ] No PII, secret, real student database, biometric templates, or raw payloads committed, logged, or exported without authorization.
- [ ] Device identity verified or physically/network-isolated where legacy hardware cannot authenticate.
- [ ] XML inputs have length limits, DTD/XXE disabled, schema validation and failure isolation.
- [ ] Consent/opt-out, retention and access permissions documented; privacy/legal review complete.
- [ ] No unsolicited real WhatsApp traffic from dev/test/staging or startup.

## P0: data correctness and reliability
- [ ] Fresh DB migration passes, existing DB migration/rollback rehearsed with authorized sanitized backup.
- [ ] Every accepted event is durable before ACK; dedup, reconnect, restart and replay exercised.
- [ ] Referential integrity enabled; event clock and timezone behavior documented and tested.
- [ ] Attendance engine respects Sunday, Shabbat, local holidays, exceptions, lesson hours and DST per institution.
- [ ] Automated backups verified by tested restore; RPO/RTO goals met or formally revised.

## P1: application/product
- [ ] Clean build with supported .NET runtime, supported dependencies and verified SDK provenance.
- [ ] Admin API authenticates/authorizes all student operations; changes auditable.
- [ ] RTL dashboard and core attendance workflow accepted by end users.
- [ ] Twilio sender verified, approved WhatsApp templates, dry-run and dedup tested; consent present.
- [ ] Operational logs/metrics/alerts without private payloads; on-call and escalation documented.
- [ ] CI security/test gates pass and reproducible release artifact has tagged SHA.

## Launch evidence
- [ ] Two-week minimum authorized pilot completed with daily discrepancy reconciliation.
- [ ] Safe rollback rehearsed including database migration consequences.
- [ ] Capacity/performance measured on representative load.
- [ ] Product owner approval: name/date/evidence __________
- [ ] Privacy/security owner approval: name/date/evidence __________
- [ ] Operations owner approval: name/date/evidence __________

GO only when all applicable P0 and P1 conditions have verified evidence and any exceptions are formally risk-accepted by the responsible owner. If any P0 remains open: NO-GO.
