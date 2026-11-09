# AttendanceSystem — תוכנית פיתוח מלאה עד Production
תאריך בסיס: 2026-11-09 | מקור אמת: ענף master במאגר MeiRR17/AttendanceSystem
מצב: תכנון מחייב לביצוע בהדרגה; **לא** אישור לכך שהמערכת מוכנה לשימוש אמיתי.

## 1. חזון, גבולות ומדיניות
מערכת אמינה לנוכחות תלמידים בישיבה/מוסד לימוד, עם קליטת זיהויים ממכשיר Realand, חישוב הגעה ביחס למערכת שעות ולחגים, דוחות ניהול, והתראות WhatsApp מבוקרות. יחידת האמת היא אירוע נוכחות מתועד; סטטוס איחור הוא החלטה נגזרת שניתנת לתיקון ולביקורת.

MVP לפרודקשן: מוסד אחד, שער מכשירים מאובטח, SQLite מתועד ומגובה (או שרת DB לפי נפח), API וממשק מנהל, הודעות Opt-in, יכולת שחזור. פיצ'רים מתקדמים כגון ניהול רב-מוסדי, מובייל וזיהוי ביומטרי חדש אינם חוסמים v1.0.0.

כללי יסוד: אין אחסון תבניות טביעת אצבע או תמונות ביומטריות באפליקציה; אין פרטי תלמידים בקוד, לוגים, דוגמאות, צילומי מסך או Git; אין שליחת WhatsApp אמיתית בברירת מחדל או על סמך נתוני דוגמה; כלל התקן/ימי לימוד חייב להיות ניתן להגדרה. פרטים ביומטריים במכשיר הפיזי מחייבים בחינה משפטית ואבטחתית נפרדת.

## 2. אמת נוכחית לפי קוד המקור
- פלטפורמה: פרויקט Console ב-C# המכוון ל-.NET 8; סוף תמיכה של .NET 8 ביום 2026-11-10, לכן יעד .NET 10 LTS.
- נקודת כניסה: AttendanceSystem/Program.cs. אתחול SQLite, שרת Realand WebSocket בפורט 2012, ובגרסת הבסיס קריאה ישירה להתראת דוגמה (תוסר בגרסת hardening).
- מודל DB: DatabaseSetup יוצר Students, Attendance ו-Holidays. שתי מחלקות קליטת אירועים מנסות INSERT עם עמודות Name, PhoneNumber ו-Status שאינן בטבלת Attendance הקיימת; אירועי נוכחות ייכשלו בעת כתיבה.
- SDK: שני נתיבי WebSocket (SDK/RealandSDK.cs ו-WebSocket/WebSocketServerSetup.cs) עם לוגיקה כפולה ולא מאוחדת.
- תלויות: קובצי DLL של SuperSocket/לוגים וקישורים לשמות קבצים בתוך Downloads מקומי; בניית נקייה אינה מובטחת.
- Hebrew calendar: HolidayChecker מתייחס גם ליום ראשון כיום מנוחה באופן קשיח; HolidayFetcher קורא Hebcal ללא סיווג מפורש בין חגים, חופשות ומועדים שבהם מתקיימים לימודים.
- Messaging: Twilio עם משתני דוגמה וטקסט חופשי; בפרודקשן נדרשים אישור נמען ותבניות WhatsApp מאושרות מחוץ לחלון 24 השעות.
- Repo exposure: ההיסטוריה כללה attendance.db תחת bin/obj, שהוסרו מענף master במסגרת PR #1, אך לא מהיסטוריית Git/שכפולים. המאגר ציבורי. אין ראיה בקוד למוכנות לפרודקשן.
- README הישן מציג דרישות .NET Framework 4.7.2 ואינו תואם את csproj. לא נצפו פרויקט Tests, workflow CI או מפרט פריסה.

## 3. מדדי סיום ודרך עבודה
כל גרסה: קוד + תיעוד + migration לפי צורך + בדיקות ממוקדות + changelog + commit/PR; אין מיזוג לקו הראשי במקרה של כשל ב-P0. כל שלב דורש DoD (Definition of Done) מפורש. ערכי זמן הם הערכות מאמץ של מפתח יחיד, לא התחייבות למועד.

| גרסה | תחום | מאמץ הנדסי | תלות | תנאי שחרור מרכזי |
| --- | --- | ---: | --- | --- |
| v0.1.1 | בלימת דליפה/פעולות דוגמה ותוכנית | 4–8 שעות | ללא | אפס פעולות הודעה אוטומטיות בדוגמה; קובצי DB מוחרגים |
| v0.2.0 | בנייה משוחזרת ו-.NET 10 | 12–20 שעות | v0.1.1 | restore/build ממחשב נקי במערכת הפעלה שהוגדרה |
| v0.3.0 | מסד נתונים ו-migrations | 16–28 שעות | v0.2.0 | מסד חדש וקיים עוברים מיגרציה; אירוע נשמר פעם אחת |
| v0.4.0 | שער Realand אמין ומאובטח | 20–40 שעות | v0.3.0, חומרה | replay/ניתוק/נתונים פגומים לא פוגעים בעקביות |
| v0.5.0 | מנוע נוכחות ולוח שנה | 14–24 שעות | v0.4.0 | סטטוסים נכונים לפי מוסד, שעה, חג, אזור וזמן קיץ |
| v0.6.0 | מנוע התראות/Opt-in | 12–22 שעות | v0.5.0 | Dry-run ברירת מחדל; outbox מבוקר, ללא הודעות כפולות |
| v0.7.0 | API, זהויות והרשאות | 22–38 שעות | v0.5.0 | גישה לפי תפקיד, בידוד נתונים ו-Audit |
| v0.8.0 | ממשק מנהל RTL | 28–48 שעות | v0.7.0 | מנהל מבצע תפעול יומי בלי גישה ישירה ל-DB |
| v0.9.0 | אמינות, בדיקות וניטור | 20–35 שעות | v0.4–0.8 | CI, תרחישי כשל, התראות, ביצועים |
| v0.10.0 | סביבת Staging ופיילוט | 18–32 שעות | v0.9.0 | פריסה חוזרת, שחזור גיבוי ופיילוט מאושר |
| v1.0.0 | אישור Production | 16–28 שעות | v0.10.0 | כל שערי האיכות/אבטחה/תפעול חתומים |
| **סה״כ** | **עד v1.0.0** | **182–323 שעות** | חומרה והחלטות ארגוניות | **לא כולל עיכובי ספקים או פיילוט לוח-שנה** |

קצב של 20 שעות פיתוח נטו בשבוע משמעו כ-9–17 שבועות הנדסיים, בנוסף לפיילוט של כשבועיים לפחות, לאישור פרטיות ולזמינות מכשיר. אין לקבוע אחוז התקדמות ללא ראיות לסגירת דרישות ושערי שחרור.

## 4. מפרט מחייב לכל גרסה

### v0.1.1 — Security & planning baseline (P0)
- להסיר קריאה להפעלת SendLateNotification עם פרטי דוגמה בתוך Program.cs.
- להחריג attendance.db, קובצי WAL/SHM, .env, secrets וקובצי סביבה מקומית; אין למחוק נתוני אמת.
- לפרסם Audit, תוכנית גרסאות וצ'קליסט פרודקשן, ולתקן README מטעה.
- לבצע בדיקת חשיפה מאושרת של היסטוריית ה-DB והסיכונים; לשקול הפיכת המאגר לפרטי, טיפול בהיסטוריה, מדיניות שמירה ופנייה לגורמים המתאימים. עצם המחיקה מה-HEAD אינה הסרת חשיפה היסטורית.
- DoD: סריקה ידנית של הדיפ החדש להיעדר PII וסודות; אין הודעת דוגמה ב-boot; התיעוד מזהיר שלא לפרוס.

### v0.2.0 — Reproducible build & architecture (P0)
- להגדיר .NET 10 LTS (SDK/CI, TargetFramework לאחר אימות תאימות), סקר NuGet, נעילת גרסאות וגיבוי נתיב rollback.
- לטפל בקישורי DLL לשבילים מקומיים: מקור מורשה, רישוי, checksum, הערכת התאמה ל-.NET 10; אין להעתיק DLL בלי בדיקת זכויות.
- לבחור מתאם חומרה תחום-אחריות: אם SDK ישן Windows-only, ליצור Windows Gateway מבודד ולהפריד Core/API שניתן להריץ ב-Linux; לא להכריח cross-platform ללא בדיקת חומרה.
- לפרק Main ל-host בר-עצירה, HostedService, dependency injection, ביטול נקי; להמיר קבועי מערכת לקונפיגורציה עם validation.
- להוסיף CI מינימלי לבנייה נקייה ובדיקת תלות, בלי סודות ובלי חומרה אמיתית.
- DoD: Restore/build מצליח ב-environment מתועד; runner ללא SDK מקומי אינו נדרש להוריד קבצים ממחשב אישי; graceful shutdown.

### v0.3.0 — Data model, migrations & idempotency (P0)
- לתכנן טבלאות Students, Devices, AttendanceEvents, AttendanceDecisions, Schedules, Holidays/Overrides, NotificationOutbox, AuditEvents.
- ליישם מיגרציות גרסאות כולל dry-run, backup לפני שינוי, דוח migration, ונתיב rollback בטוח.
- להפריד מזהה תלמיד פנימי ממזהה שמגיע ממכשיר; למזער PII, foreign keys, unique indexes ו-UTC timestamps.
- הגדרת מפתח dedup לפי deviceId + eventId; בהיעדר eventId מהספק, לקבוע fingerprint דטרמיניסטי מבוקר על tuple ו-window.
- כתיבה טרנזקציונית, WAL לפי צורך, busy_timeout, retry מוגבל, בדיקת integrity.
- DoD: כניסה כפולה אינה מייצרת רשומה כפולה; כשל כתיבה מדווח ונשמר לשחזור; migration ישן/חדש עובר בדיקה.

### v0.4.0 — Trusted Realand ingest (P0)
- לפרסם protocol fixtures אנונימיים מתיעוד/מכשיר מורשה; לתעד firmware ודגם מדויק.
- שרת WSS/TLS או Gateway מאחורי גבולות רשת, זיהוי מכשיר, הרשאות, whitelist/segmentation; אם למכשיר אין Auth, לבודד אותו ברשת ולא להצהיר שהוא מאומת.
- מגבלת גודל הודעה, Rate limit, parse XML עם DTD/XXE מבוטל, אימות schema, טיפול בשגיאות והעברת אירועים לתור/יומן durable.
- ACK רק אחרי persist; ניהול reconnect, ordering, timestamp skew, כפילויות ו-replay, ניטור מצב חיבור.
- לאסור הדפסת payload גולמי המכיל שם/טלפון.
- DoD: 1,000 אירועי replay/duplicate בסביבת סימולציה ללא אובדן או double count; integration test מאושר עם מכשיר פיזי לפני פרודקשן.

### v0.5.0 — Business rules & Jewish calendar (P1)
- פרופיל מוסד: ימי פעילות, שני שיעורים/יותר, מועדי התחלה, grace minutes, איחור, היעדרות, יציאות וחופשות.
- שעון אזור Asia/Jerusalem, UTC בפנים, DST, הגעה סביב חצות, ותיקון שעון מכשיר.
- Hebcal עם timeout/retries/cache, הגדרת Israel/Diaspora; סיווג holiday vs minor/rosh-chodesh, override מנהל ואפשרות Offline.
- אלגוריתם ניתן לשחזור: event + rule version -> decision; שינוי החלטה אינו מוחק raw event.
- DoD: בדיקות תאריכים (שבת, ראשון לפי מדיניות, חגים, שעון קיץ) עם fixture קבוע; אין "skip Sunday" קשיח.

### v0.6.0 — Compliant notifications (P1)
- Consent מפורש ומתועד, unsubscribe, phone validation, opt-out, מגבלות שליחה.
- NotificationOutbox עם dedup לפי תלמיד/תאריך/סוג, retry backoff ו-dead-letter, רישום התוצאה/עלות.
- Twilio credential secret-store; WhatsApp-approved content template/ContentSid ליזמת מוסד מחוץ לחלון 24 שעות. לא להשתמש בגוף חופשי באופן שאינו תואם מדיניות.
- Dry-run ושירות Mock כברירת מחדל בסביבות פיתוח ו-CI; מתג הפעלת prod דו-שלבי.
- DoD: אפס התראות כפולות ואפס שליחות ללא consent; כשל ספק אינו מוחק אירוע נוכחות; templates מאושרים לפני שחרור.

### v0.7.0 — Backend/API/security (P1)
- HTTP API גרסתי ל-Students, Schedules, Devices, Attendance, Overrides, Reports, Audit.
- Auth OIDC או מנגנון SSO מוכר; תפקידי Administrator/Staff/Viewer, least privilege, session expiry, CSRF/CORS תואמים למודל האימות.
- פילטור ושאילתות paginated, validation, audit trail לא-מחיק של תיקונים; הפרדת מידע לפי מוסד אם נדרש.
- Rate limits, export מוגבל הרשאות, redaction ומדיניות מחיקה/שמירה.
- DoD: בדיקות authorization חיוביות ושליליות; לא ניתן לראות מידע אישי ללא הרשאה.

### v0.8.0 — Admin portal (P1)
- RTL עברית, mobile/responsive, נגישות בסיסית; דשבורד חיבור מכשיר, נוכחים, מאחרים, חסרים והודעות ממתינות.
- ניהול תלמידים/מערכת שעות/חופשות, תיקון נוכחות עם סיבה וחותמת משתמש, חיפוש/סינון, CSV/XLSX בכפוף להרשאה.
- מסך אישורי שליחה, היסטוריית הודעות, שגיאות, תצוגת Audit ופעילות מכשירים.
- DoD: מסלול משתמש מלא: הוספת תלמיד -> זיהוי -> החלטה -> בדיקה -> תיקון -> ייצוא; אישור בעל תפקיד.

### v0.9.0 — Reliability & observability (P1)
- Unit/API/integration tests, contract fixtures ל-Realand, fault injection ל-DB/Hebcal/Twilio, dependency/vulnerability checks.
- Structured logs ללא PII, traces/metrics (ingest lag, error rate, device uptime, failed outbox, DB integrity), alerts ו-health/live-ready.
- יעדי שירות מוצעים לפיילוט: >=99.5% קליטת אירועים תקינה במערכת פעילה; p95 קליטה-לשמירה <5 שניות (לא כולל תקלה חיצונית); אפס אירועים אבודים אחרי ACK.
- DoD: תרחישי כשל ותפעול מאושרים, בדיקות רגרסיה ב-CI, ספי התראה מכוילים.

### v0.10.0 — Deployment, backup & pilot (P0)
- סביבת Staging עם נתונים סינתטיים; תצורת הפעלה כ-Service עבור OS שנבדק; secrets ו-TLS, חסימת פורטים לא נחוצים.
- מסמכי install, upgrade, rollback, incident, backup/restore; הצפנה ו-retention סבירים לתלמידים/קטינים.
- RPO מוצע <=24 שעות, RTO מוצע <=4 שעות; לבצע תרגול שחזור בפועל ולתעד מדידות ולא רק לכתוב יעדים.
- פיילוט לפחות שבועיים עם מכשיר מורשה, משתמש מנהל, בדיקת הסכמה ומעקב יומי אחר חריגים; אופציית כיבוי Notifications.
- DoD: גיבוי משוחזר במערכת נקייה, deployment reproducible ו-Go/No-Go מפורש.

### v1.0.0 — Production release (P0)
- סגירת כל חסמי P0/P1, אישור הגנת פרטיות ושמירת נתונים, סקירת סיכוני ביומטריה, הרשאות, אבטחה ותפעול.
- Build signed/checksummed, immutable release artifact, tagged v1.0.0, release notes, rollback plan ומסמכי תמיכה.
- KPI production: אירועים נקלטים/נדחים, החלטות במחלוקת, התראות כשירות, זמינות מכשירים, שחזור גיבויים, SLA.
- DoD: Product owner + operator חותמים על checklist, לא על הערכת אחוז התקדמות בלבד.

## 5. אחרי v1.0.0 (לא חסמי השקה)
- v1.1.0: ריבוי מוסדות/סניפים, הרשאות ותצוגות לפי tenant, תצורת ימי לימודים מתקדמת (20–40 ש').
- v1.2.0: אנליטיקה, מגמות איחורים, ייצוא אוטומטי, פרטיות בדוחות (18–32 ש').
- v1.3.0: Self-service וערוצים נוספים להתראות באישור מוסד (18–30 ש').
- v2.0.0: מעבר נתונים/סקייל, HA לפי נפח ממשי, API אינטגרציות פומבי מוגבל (40–90 ש').

## 6. סיכונים, החלטות ותלויות חיצוניות
1. P0: מידע אישי במאגר ציבורי ובהיסטוריה — לבצע triage פרטיות מורשה ולהפוך לפרטי לפי החלטת בעל המאגר, לפני מידע אמיתי חדש.
2. P0: מכשיר Realand, firmware, פרוטוקול וחיבור רשת — ללא חומרה מאומתת אין אישור production.
3. P0: SDK וספריות Legacy — להוכיח מקור/תאימות/רישוי או לבודד Gateway ייעודי; לא להניח ש-.NET 10 עובד כמות שהוא.
4. P0: אישורים משפטיים ארגוניים לנתונים ביומטריים וזיהוי תלמידים, תקופת שמירה, הרשאות ומדיניות דיווח.
5. P1: WhatsApp Business verification, consent ו-approved templates תלויים בספק.
6. P1: עלות hosting, גיבויים, monitoring ו-WhatsApp — להעריך לפי כמויות תלמידים/אירועים; אין הבטחת עלות אפס.

## 7. רצף ביצוע והתנהלות GitHub
- source of truth: אותו מאגר AttendanceSystem, ענף master; AttendanceSystem-v2 ריק ולא יועבר אליו קוד ללא החלטה.
- ענף אחד לכל גרסה (feature/vX.Y-feature), PR עם DoD, ו-version tag רק אחרי מיזוג ואימות; להימנע משכפול פעילות לענפים מתחרים.
- בתחילת כל מחזור "תמשיך" לקרוא roadmap, ענפים ו-PRs, לבחור את הגרסה הבלתי גמורה הראשונה, לממש, לדווח על commit, תוצאות בדיקות ומה **לא** אומת.
- לא לעקוף בדיקות אבטחה/פרטיות/תקינות נתונים, גם כשמצמצמים בדיקות איטיות; אינטגרציית מכשיר ומשלוחי אמת דורשים אימות ייעודי.
- קישורים להנחיות חיצוניות: https://dotnet.microsoft.com/en-us/platform/support/policy ; https://www.twilio.com/docs/whatsapp/key-concepts ; https://www.hebcal.com/api-docs/
