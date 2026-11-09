# AttendanceSystem v0.5.0 — versioned academic timetable, Israel timezone and calendar

Status: development implementation, NOT approved for real student data, live absence declarations or WhatsApp notifications. Parent v0.2–0.4 are stacked draft PRs.

## Scope implemented

**AttendanceRuleEngine** is a deterministic, offline rule evaluator. It accepts an explicit per-institution policy JSON with stable RuleVersion, Asia/Jerusalem timezone, study weekdays, named class sessions, start local time, grace period, late cutoff, early-arrival window and date-specific overrides. Sunday is NOT automatically a weekend: each institution chooses its own study weekdays. Friday or Saturday can similarly be enabled deliberately if a valid session exists.

**Day overrides:** Study (force class on normally non-study weekday), NoStudy (do not infer absence), Excused (explicit approved closure/exception). An external holiday entry **never** automatically closes the institution. Per-date overrides must be approved and reasoned by an authorized operator outside this code. Missing rules, unrecognized session, out-of-window event or incomplete data results in Unclassified, not absence.

**States:** OnTime, Late, Absent, Excused, Unclassified, Invalid. The last is reserved for future separately authenticated invalid-event reporting. Incoming trusted events can be classified as OnTime, Late, Excused or Unclassified. Absent is evaluated ONLY by an explicit roster/session caller after the session's cutoff; it is never guessed from a missing device packet. No automatic absence batch, messaging or student roster integration is enabled.

**Time:** timestamps must carry a UTC zero offset. The evaluator converts from UTC into Asia/Jerusalem using the platform's timezone data (fallback Israel Standard Time on Windows without IANA mapping). It never assumes a fixed +02:00 offset; DST and midnight-spanning sessions use converted local wall time, and overnight events belong to the date on which the session started.

**Decision persistence:** AttendanceDecisionWriter reads one already-validated event from the new v2 AttendanceEvents, produces a derived AttendanceDecisions row within a transaction, and appends an AuditEvents record when created or revised. Same-rule reevaluation is idempotent. Revised rules update the derived row but never edit the original immutable attendance event. The audit currently records actor=rules-engine and opaque numeric event ID, not student name/phone. The v0.7 authorization workflow and full before/after audit details remain future work.

**HebcalObservationClient:** explicit opt-in to Hebcal's documented JSON REST API. It requests a specified Gregorian year and the Israel/Diaspora option, uses 5 s timeout and 1 MiB response cap, caches fresh responses for 12 h, and can return flagged stale observations only up to 7 days after a failed refresh. It passes through date, title, category and yomtov flag, and clearly labels cached/stale results. No automatic fetching at scanner-ingest time; a provider outage never deletes an event or changes the institution policy. Content attribution: Hebcal.com, CC BY 4.0. API reference: https://www.hebcal.com/api-docs/

The previous HolidayChecker and HolidayFetcher prototype methods now fail closed rather than classifying **every Sunday** or every Hebcal event as a holiday. Unclassified events do not trigger outgoing notifications.

## Configuration example (SYNTHETIC; NOT a real school calendar)

    {
      "RuleVersion": "policy-2026-11-09-r1",
      "TimeZoneId": "Asia/Jerusalem",
      "StudyDays": ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday"],
      "Sessions": [
        {
          "Day": "Sunday", "Name": "Morning",
          "StartLocal": "08:00:00", "CutoffLocal": "09:30:00",
          "GraceMinutes": 10, "EarlyArrivalMinutes": 20
        }
      ],
      "Overrides": [
        {"Date": "2026-11-15", "Mode": "Excused", "Reason": "synthetic fixture"}
      ]
    }

The engine requires an explicit session for each weekday whose arrivals should be classified. This sample has only Sunday. An administrator must approve versioned policies and exception dates before use; do not check in production policy containing private details.

## Offline CI verification (no devices, external HTTP or real student data)

    dotnet restore AttendanceSystem.sln
    dotnet build AttendanceSystem.sln -c Release --no-restore
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release --no-build -- --self-test-rules

The synthetic suite covers Sunday working, Friday exception opening, Saturday disabled, named class start/grace/late/cutoff, absence only after cutoff, holiday Excused vs NoStudy, overnight classes, DST summer conversion, UTC requirement, malformed/unknown-policy rejection, versioned decision materialization and append-only audit, and a mocked (no-network) Hebcal Israel feed with cache and stale-outage fallback.

## Remaining release gates
- No UI or RBAC for policy changes; future v0.7 handles operator authorization and reliable exception approvals.
- No absence materialization from an authorized class roster and no notification triggers. No automated aggregation of multiple sessions, tardiness thresholds by student, or real holiday classification policy.
- Calendar observations need operator approval before becoming exceptions. There is no external API call in default process startup, CI or event ingestion.
- Must validate daylight saving transitions near ambiguous fall-back hour with representative institutional schedules, review time zone package updates, and introduce audited manual corrections.
- Physical Realand protocol remains unverified; privacy and SDK provenance gates are open. No real student/biometric processing authorized.

See production roadmap and GitHub Issue #8.
