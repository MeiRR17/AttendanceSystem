# v0.4.0 — authenticated localhost bridge (development)

Status: engineering preview. Not a Realand-compatible device transport; do not connect real scanners or student records.

## Implemented boundaries

The optional Kestrel localhost endpoint accepts signed XML TimeLog messages on POST /v1/events. The caller must be a trusted local bridge, not untrusted fingerprint-reader firmware. Its device binding is configured by the operator; device IDs from XML are never trusted. It refuses startup without an explicit flag, external 256-bit-or-stronger HMAC-SHA256 key, existing schema-v2 SQLite DB and registered enabled device.

The gateway checks request age <=5 minutes, signature in constant time, maximum 8192 UTF-8 bytes, XML DTD/XXE prohibition, exact field schema, UTC event timestamp (within 90 days past, 5 minutes future), enabled device in database and 2500 requests/minute local limit. No student names, phone numbers, biometrics, XML contents, or secrets are logged.

Database write occurs in a transaction before returning HTTP 201 (inserted) or HTTP 200 (duplicate). Identical replays never create another event. A conflicting event key returns 409. Invalid authentication returns 401; disabled device 403; invalid XML 422; oversized request 413; rate limit 429; SQLite error 503. Failed persistence must not be acknowledged.

This is a separate authenticated transport. The old unauthenticated RealandSDK path intentionally rejects scanner events. The v0.4 hardware protocol, firmware and event ACK semantics are unknown and still require authorized validation.

## Isolated local test deployment

Use Windows with .NET 10, an existing approved synthetic SQLite v2 database and an enabled synthetic device record. Provide environment configuration in a secret store, never a Git file:

    ATTENDANCE_BRIDGE_ENABLED=true
    ATTENDANCE_BRIDGE_DEVICE_ID=test-device
    ATTENDANCE_BRIDGE_SECRET_BASE64=<base64 encoding of 32+ random bytes>
    ATTENDANCE_DB_PATH=<absolute path of existing synthetic SQLite v2 DB>
    ATTENDANCE_BRIDGE_PORT=21812

Then run:

    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --serve-bridge

The server listens on loopback HTTP only. Never expose port 21812 through LAN, reverse proxy, port forwarding, container publishing, VPN or a hardware device. Only one configured device is supported per bridge instance.

## Signed request contract (synthetic identifiers)

POST http://localhost:21812/v1/events
Content-Type: application/xml
X-Attendance-Device: test-device
X-Attendance-Timestamp: current Unix seconds (time of forwarding)
X-Attendance-Signature: 64-character hex HMAC-SHA256 digest

UTF-8 XML body, with no extra whitespace changes between signing and sending:

    <TimeLog><EventKey>synthetic-sequence-0001</EventKey><StudentID>student-001</StudentID><OccurredAtUtc>2026-11-09T09:00:00Z</OccurredAtUtc></TimeLog>

Compute the HMAC over UTF-8 of the following fields separated by literal LF newlines:
HTTP method POST, the path /v1/events, configured DeviceID, Unix timestamp, exact XML body.
The implementation uses the canonical prefix "POST", LF, "/v1/events", LF, DeviceID, LF, UnixTimestamp, LF, body.

On retry, sign with a fresh forwarding timestamp but retain original EventKey and OccurredAtUtc. Only use opaque synthetic identifiers.

## Synthetic verification

    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release --no-build -- --self-test-bridge

The smoke suite creates a temporary DB and random secret in memory, inserts 1000 synthetic events, replays all 1000, checks the exact event count, and verifies rejection of bad signatures, wrong device, old signed envelopes, DTD/XXE, malformed/oversized XML, unexpected personal fields, future clock, conflicting event key and disabled device. It never contacts a scanner, network endpoint or WhatsApp. Parent versions also test migration and safe startup.

CI verifies authentication, SQLite idempotency and an actual in-process Kestrel loopback HTTP round trip (insert, duplicate, bad signature, malformed body, wrong content type). This still does not validate Realand hardware, TLS/WSS, networking outside localhost or a physical scanner's acknowledgment protocol.

## Remaining gates

- Confirm actual Realand model/firmware, vendor SDK provenance, device handshake and stable event IDs; authenticate physical device or isolate a proven compatible broker.
- Test real HTTP loopback transport, disconnect/reconnect and actual delivery acknowledgement; add real network threat review, WSS/TLS where supported.
- Secure secret lifecycle and rotation; device enrollment/admin UI and lockout across instances; operational metrics.
- Authorized privacy assessment for historical public attendance.db, plus real DB migration/backup restoration using sanitized fixtures.
- Physical-device pilot and production approval, without exposing this HTTP bridge beyond the local machine.

See GitHub issue #7 and stacked draft PRs #15–#16.
