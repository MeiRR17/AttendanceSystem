# AttendanceSystem v0.8.0 — Hebrew RTL admin UI (engineering preview)

**Status:** Pre-production. There is no access to private student data, Realand devices, Twilio sending or a real institutional login. This version supplies real HTML/CSS/JS source and actual localhost serving, but uses a synthetic sample dataset until a separate, authorized API session is configured.

## Live synthetic preview: no DB, tokens, device or internet

    dotnet restore AttendanceSystem.sln
    dotnet build AttendanceSystem.sln -c Release --no-restore
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --preview-ui

Open **http://127.0.0.1:21814/app** on that same computer. The preview explicitly binds IPv4 loopback and exposes no attendance backend. All displayed event IDs are synthetic; it loads no private contact, name or biometric information. Its JavaScript uses in-memory demo rows with a status distribution, searchable/filterable event table, keyboard-accessible correction dialog, simulated correction history and **demo-only** CSV export.

Source: AttendanceSystem/AdminUi/index.html, style.css, app.js, AdminUiAssets.cs. Assets are embedded in the .NET assembly and served with a fixed allowlist, no arbitrary file routing and no external libraries/fonts/CDN.

## Operator features included

- Hebrew right-to-left responsive layout for desktop/tablet/mobile with a monochrome sidebar, top bar, status notice, four summary metrics and status bars.
- Overview: counts from classified event statuses, not a guessed student roster; recent events and operational risk flags. Actual Realand connectivity and message delivery status are intentionally not fabricated.
- Events: opaque event IDs, UTC timestamps, classified statuses and revision; event ID search, status filtering and cursor-based loading from a JWT-protected API; role-gated correction modal requiring an approved reason and optimistic revision.
- Audit: admin-only server route GET /api/v1/corrections?limit=50 (1..100 bound), returning opaque event ID, before/after status, revision, enumerated reason, actor hash and UTC timestamp. Viewer/Staff get HTTP 403. No student ID, name or phone is returned.
- Real records export is still HTTP 501 on the server even for Administrator. A separate demo CSV uses fake IDs only, escapes spreadsheet formula triggers and is never available in live API mode.
- User-friendly loading/empty/error states, labelled inputs, logical headings, semantic data tables, skip-link, keyboard focus, aria-live feedback and dialog semantics. Manual assistive-technology and color-contrast sign-off are still required.

## Connecting an authorized local API (developers only)

The existing v0.7.0 API supports **OIDC JWT resource server validation** and explicit role policies. When the API is separately configured and launched on loopback with an approved schema-v4 DB, open http://127.0.0.1:21813/app. The static shell may be loaded anonymously but all actual data endpoints are protected by bearer JWT and server-side RBAC. The browser interface includes a temporary **local-development-only** bearer token entry. That token is held in JavaScript memory only; no localStorage, sessionStorage, cookies or URL parameters are used. It is cleared on disconnect/pagehide. It must not be used with actual institutional credentials before security review. The resource-server API does not implement Windows SSO, PKCE, an IdP login flow or audited refresh-token lifecycle.

A validated Viewer can see summary aggregates but cannot open raw events, correction actions or audit. Staff can open bounded event lists and submit manual corrections. Administrators can access the global correction audit. Interface navigation visibility is only usability; the API is the authorization boundary. The HTML does not contain student data or any access token.

Serving files uses restrictive Content-Security-Policy (self-only CSS/scripts/connections), no-store, nosniff, frame denial and no-referrer. No public/LAN listener or permissive CORS is enabled. Do not proxy or container-publish either loopback port.

## Tests

    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release --no-build -- --self-test-ui
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release --no-build -- --self-test-api
    node --check AttendanceSystem/AdminUi/app.js

The UI smoke starts Kestrel on an ephemeral loopback port and verifies HTML/CSS/JS content types, real HTTP delivery, Hebrew dir=rtl, focus/accessibility hooks, CSP, no storage of bearer tokens, no raw HTML insertion, demo-only CSV, and no backend in preview mode. API tests include anonymous/Viewer/Staff/Administrator global audit enforcement, bounded pagination, pseudonymous data fields and conflict-safe corrections on synthetic SQLite.

CI validates source and protocol contracts, not human keyboard/screen-reader behavior. No screenshots of synthetic data should be described as real production.

## Deferred beyond this version
- Create/edit actual students, class rosters, schedule authoring, holiday exception approvals, device management and consent approvals are **not implemented**. They require protected per-student authorization and privacy workflows.
- Full live CSV/XLSX exports, signed downloads, retention and guardian subject-access policy are **blocked**.
- The login handoff should be replaced with reviewed OIDC authorization-code + PKCE or backend-for-frontend and proper Windows/enterprise SSO before production.
- Device state, message receipts, real hosting, tenancy, true per-student Staff scopes, accessibility manual QA, privacy/legal authorization and backups remain gates.
- Never enable scanning, WhatsApp, or real student information just to demonstrate this UI.

Tracking: issue #11; based on v0.7.0 draft PR #20 and earlier stacked PRs.
