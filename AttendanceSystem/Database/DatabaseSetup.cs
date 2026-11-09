using System.Data.SQLite;
using System.Globalization;

namespace AttendanceSystem.Database;

/// <summary>
/// Additive SQLite migrations. Historical Students/Attendance/Holidays tables
/// are preserved byte-for-byte by SQL (no DROP, ALTER, or data rewrite).
/// Existing databases require an explicit upgrade flag and SQLite backup.
/// </summary>
public static class DatabaseSetup
{
    public const int CurrentSchemaVersion = 4;

    public static void InitializeDatabase()
    {
        var path = Environment.GetEnvironmentVariable("ATTENDANCE_DB_PATH");
        InitializeDatabaseAtPath(string.IsNullOrWhiteSpace(path) ? "attendance.db" : path);
    }

    // This obsolete boolean cannot grant authority for a private database.
    // The public method can create a new DB or validate an existing current
    // DB, but MUST NOT upgrade an existing schema, regardless of flag value.
    public static void InitializeDatabaseAtPath(string path, bool allowExistingUpgrade = false)
    {
        if (allowExistingUpgrade)
            throw new InvalidOperationException(
                "Existing database upgrades are unavailable via public API.");
        InitializeDatabaseCore(path, permitSyntheticExistingUpgrade: false);
    }

    // Test-only entry point. No production CLI, HTTP route, or public API
    // exposes this path. A dedicated synthetic-only scope is checked first.
    internal static void InitializeSyntheticFixtureAtPath(string path)
    {
        SyntheticMigrationScope.Demand(path);
        InitializeDatabaseCore(path, permitSyntheticExistingUpgrade: true);
    }

    private static void InitializeDatabaseCore(
        string path, bool permitSyntheticExistingUpgrade)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Database path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !System.IO.Directory.Exists(directory))
            throw new DirectoryNotFoundException("Database parent directory does not exist.");

        var existed = File.Exists(fullPath);
        using var connection = Open(fullPath, failIfMissing: false);
        var version = ReadVersion(connection);
        if (version > CurrentSchemaVersion)
            throw new InvalidOperationException("Database schema is newer than this application.");

        if (version == CurrentSchemaVersion)
        {
            AssertRequiredTables(connection);
            return;
        }

        if (existed && !permitSyntheticExistingUpgrade)
            throw new InvalidOperationException(
                "Existing database migration requires a verified backup and explicit approval. " +
                "Use an authorized upgrade process; never migrate a real student database in CI.");

        if (existed)
        {
            // Existing old layouts must match the known 0/1 schema exactly,
            // BEFORE making a backup or attempting any DDL. Unknown columns,
            // triggers, foreign keys, corrupt pages or orphaned references are
            // a hard stop, even when allowExistingUpgrade is explicitly true.
            if (version <= 1)
            {
                var inspection = LegacyMigrationPreflight.Inspect(connection);
                if (!inspection.StructurallyCompatible)
                    throw new InvalidOperationException(
                        "Unsupported legacy database layout; migration blocked. " +
                        "Use an approved isolated and sanitized investigation.");
            }
            CreateSqliteBackup(connection, fullPath);
        }

        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Version INTEGER PRIMARY KEY,
                AppliedAtUtc TEXT NOT NULL
            );
            """);

        if (version < 1)
        {
            Execute(connection, transaction, """
                CREATE TABLE IF NOT EXISTS Students (
                    StudentID TEXT PRIMARY KEY,
                    Name TEXT,
                    PhoneNumber TEXT
                );
                CREATE TABLE IF NOT EXISTS Attendance (
                    StudentID TEXT,
                    Date TEXT,
                    Time TEXT,
                    FirstLessonStatus TEXT,
                    SecondLessonStatus TEXT
                );
                CREATE TABLE IF NOT EXISTS Holidays (
                    Date TEXT PRIMARY KEY
                );
                """);
            RecordMigration(connection, transaction, 1);
        }

        if (version < 2)
        {
            // New event model does not overwrite unreliable legacy attendance rows.
            Execute(connection, transaction, """
                CREATE TABLE IF NOT EXISTS Devices (
                    DeviceID TEXT PRIMARY KEY,
                    Enabled INTEGER NOT NULL DEFAULT 0 CHECK (Enabled IN (0, 1)),
                    RegisteredAtUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS AttendanceEvents (
                    EventID INTEGER PRIMARY KEY AUTOINCREMENT,
                    DeviceID TEXT NOT NULL REFERENCES Devices(DeviceID),
                    EventKey TEXT NOT NULL,
                    StudentID TEXT NOT NULL,
                    OccurredAtUtc TEXT NOT NULL,
                    ReceivedAtUtc TEXT NOT NULL,
                    UNIQUE (DeviceID, EventKey)
                );
                CREATE INDEX IF NOT EXISTS IX_AttendanceEvents_Student_OccurredAt
                    ON AttendanceEvents(StudentID, OccurredAtUtc);
                CREATE TABLE IF NOT EXISTS AttendanceDecisions (
                    EventID INTEGER PRIMARY KEY REFERENCES AttendanceEvents(EventID),
                    RuleVersion TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    EvaluatedAtUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS Schedules (
                    ScheduleID INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    StartsAtLocal TEXT NOT NULL,
                    GraceMinutes INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS AuditEvents (
                    AuditID INTEGER PRIMARY KEY AUTOINCREMENT,
                    Action TEXT NOT NULL,
                    EntityKey TEXT NOT NULL,
                    ActorKey TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );
                """);
            RecordMigration(connection, transaction, 2);
        }

        if (version < 3)
        {
            // No real phone numbers, names, message bodies or Twilio secrets in outbox tables.
            Execute(connection, transaction, """
                CREATE TABLE IF NOT EXISTS NotificationConsents (
                    RecipientKey TEXT NOT NULL,
                    Purpose TEXT NOT NULL,
                    IsGranted INTEGER NOT NULL CHECK(IsGranted IN (0, 1)),
                    EvidenceRef TEXT NOT NULL,
                    ChangedAtUtc TEXT NOT NULL,
                    PRIMARY KEY (RecipientKey, Purpose)
                );
                CREATE TABLE IF NOT EXISTS NotificationTemplates (
                    TemplateKey TEXT NOT NULL PRIMARY KEY,
                    Purpose TEXT NOT NULL,
                    ApprovalEvidenceRef TEXT NOT NULL,
                    IsEnabled INTEGER NOT NULL DEFAULT 0 CHECK(IsEnabled IN (0, 1))
                );
                CREATE TABLE IF NOT EXISTS NotificationOutbox (
                    OutboxID INTEGER PRIMARY KEY AUTOINCREMENT,
                    EventID INTEGER NOT NULL REFERENCES AttendanceEvents(EventID),
                    RecipientKey TEXT NOT NULL,
                    Purpose TEXT NOT NULL,
                    TemplateKey TEXT NOT NULL REFERENCES NotificationTemplates(TemplateKey),
                    RuleVersion TEXT NOT NULL,
                    Status TEXT NOT NULL CHECK(
                        Status IN ('Queued','Leased','Retry','Simulated','Suppressed',
                                   'DeadLetter','Uncertain')
                    ),
                    AttemptCount INTEGER NOT NULL DEFAULT 0,
                    NextAttemptUtc TEXT NOT NULL,
                    LeaseToken TEXT,
                    LeaseUntilUtc TEXT,
                    LastResultCode TEXT,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    UNIQUE(EventID, RecipientKey, Purpose)
                );
                CREATE INDEX IF NOT EXISTS IX_NotificationOutbox_Status_Due
                    ON NotificationOutbox(Status, NextAttemptUtc);
                CREATE TABLE IF NOT EXISTS NotificationAttempts (
                    AttemptID INTEGER PRIMARY KEY AUTOINCREMENT,
                    OutboxID INTEGER NOT NULL REFERENCES NotificationOutbox(OutboxID),
                    AttemptNo INTEGER NOT NULL,
                    ResultCode TEXT NOT NULL,
                    RecordedAtUtc TEXT NOT NULL,
                    UNIQUE(OutboxID, AttemptNo)
                );
                """);
            RecordMigration(connection, transaction, 3);
        }

        if (version < 4)
        {
            // Manual corrections are append-only. Source AttendanceEvents and
            // computed AttendanceDecisions are never rewritten by this migration.
            Execute(connection, transaction, """
                CREATE TABLE IF NOT EXISTS AttendanceCorrections (
                    CorrectionID INTEGER PRIMARY KEY AUTOINCREMENT,
                    EventID INTEGER NOT NULL REFERENCES AttendanceEvents(EventID),
                    Revision INTEGER NOT NULL CHECK(Revision > 0),
                    BeforeStatus TEXT NOT NULL,
                    AfterStatus TEXT NOT NULL,
                    ReasonCode TEXT NOT NULL,
                    ActorHash TEXT NOT NULL,
                    RecordedAtUtc TEXT NOT NULL,
                    UNIQUE(EventID, Revision)
                );
                CREATE INDEX IF NOT EXISTS IX_AttendanceCorrections_Event_Revision
                    ON AttendanceCorrections(EventID, Revision DESC);
                """);
            RecordMigration(connection, transaction, 4);
        }

        transaction.Commit();
        AssertRequiredTables(connection);
    }

    internal static SQLiteConnection Open(string path, bool failIfMissing = true)
    {
        var builder = new SQLiteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Version = 3,
            FailIfMissing = failIfMissing
        };
        var connection = new SQLiteConnection(builder.ToString());
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public static int ReadVersion(SQLiteConnection connection)
    {
        using var table = connection.CreateCommand();
        table.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type='table' AND name='SchemaMigrations';
            """;
        if (Convert.ToInt32(table.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            return 0;

        using var version = connection.CreateCommand();
        version.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigrations;";
        return Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void AssertRequiredTables(SQLiteConnection connection)
    {
        foreach (var tableName in new[]
                 { "Students", "Attendance", "Holidays", "Devices", "AttendanceEvents",
                   "AttendanceDecisions", "Schedules", "AuditEvents", "SchemaMigrations",
                   "NotificationConsents", "NotificationTemplates", "NotificationOutbox",
                   "NotificationAttempts", "AttendanceCorrections" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
            command.Parameters.AddWithValue("@name", tableName);
            if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException("Missing schema table: " + tableName);
        }
    }

    private static void RecordMigration(SQLiteConnection connection, SQLiteTransaction tx, int version)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            "INSERT INTO SchemaMigrations (Version, AppliedAtUtc) VALUES (@version, @applied);";
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@applied", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void Execute(SQLiteConnection connection, SQLiteTransaction tx, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void CreateSqliteBackup(SQLiteConnection source, string path)
    {
        // SQLite online backup API, rather than File.Copy on a potentially active WAL DB.
        // Backup path is local/private; operators must secure access and retention.
        var suffix = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture);
        var backup = path + ".pre-migration-" + suffix + ".bak";
        using var destination = Open(backup, failIfMissing: false);
        source.BackupDatabase(destination, "main", "main", -1, null, 0);
        using var verification = destination.CreateCommand();
        verification.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(Convert.ToString(verification.ExecuteScalar(), CultureInfo.InvariantCulture),
                           "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Backup integrity check failed.");
    }
}
