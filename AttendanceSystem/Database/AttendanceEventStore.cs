using System.Data.SQLite;
using System.Globalization;

namespace AttendanceSystem.Database;

/// <summary>
/// Idempotent persistence only for authorized, already validated device events.
/// DeviceId must come from a trusted gateway, never from unchecked scanner XML.
/// </summary>
public sealed class AttendanceEventStore
{
    private readonly string _databasePath;

    public AttendanceEventStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        _databasePath = Path.GetFullPath(databasePath);
    }

    public AttendanceWriteResult Save(AttendanceEventInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);

        using var connection = DatabaseSetup.Open(_databasePath);
        if (DatabaseSetup.ReadVersion(connection) != DatabaseSetup.CurrentSchemaVersion)
            throw new InvalidOperationException("Database migration required before event recording.");

        using var tx = connection.BeginTransaction();
        using (var device = connection.CreateCommand())
        {
            device.Transaction = tx;
            device.CommandText = "SELECT Enabled FROM Devices WHERE DeviceID=@id;";
            device.Parameters.AddWithValue("@id", input.DeviceId);
            var enabledValue = device.ExecuteScalar();
            if (enabledValue is null
                || Convert.ToInt32(enabledValue, CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException("Device is not enrolled and enabled.");
        }

        var occurred = input.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture);
        var received = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        int count;
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO AttendanceEvents
                    (DeviceID, EventKey, StudentID, OccurredAtUtc, ReceivedAtUtc)
                VALUES (@device, @key, @student, @occurred, @received)
                ON CONFLICT(DeviceID, EventKey) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("@device", input.DeviceId);
            insert.Parameters.AddWithValue("@key", input.EventKey);
            insert.Parameters.AddWithValue("@student", input.StudentId);
            insert.Parameters.AddWithValue("@occurred", occurred);
            insert.Parameters.AddWithValue("@received", received);
            count = insert.ExecuteNonQuery();
        }

        if (count == 1)
        {
            tx.Commit();
            return AttendanceWriteResult.Inserted;
        }

        using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = tx;
            lookup.CommandText = """
                SELECT StudentID, OccurredAtUtc FROM AttendanceEvents
                WHERE DeviceID=@device AND EventKey=@key;
                """;
            lookup.Parameters.AddWithValue("@device", input.DeviceId);
            lookup.Parameters.AddWithValue("@key", input.EventKey);
            using var reader = lookup.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException("Conflict without a matching stored event.");
            if (!string.Equals(reader.GetString(0), input.StudentId, StringComparison.Ordinal)
                || !string.Equals(reader.GetString(1), occurred, StringComparison.Ordinal))
                throw new InvalidOperationException("Event key conflict with different event contents.");
        }

        tx.Commit();
        return AttendanceWriteResult.Duplicate;
    }

    private static void Validate(AttendanceEventInput input)
    {
        CheckKey(input.DeviceId, nameof(input.DeviceId), 80);
        CheckKey(input.EventKey, nameof(input.EventKey), 160);
        CheckKey(input.StudentId, nameof(input.StudentId), 128);
        if (input.OccurredAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Event timestamp must be UTC.", nameof(input));
    }

    private static void CheckKey(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new ArgumentException(field + " is missing or exceeds its length limit.", field);
    }
}

public sealed record AttendanceEventInput(
    string DeviceId, string EventKey, string StudentId, DateTimeOffset OccurredAtUtc);

public enum AttendanceWriteResult
{
    Inserted,
    Duplicate
}
