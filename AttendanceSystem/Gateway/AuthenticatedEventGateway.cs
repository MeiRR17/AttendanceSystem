using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using AttendanceSystem.Database;
using AttendanceSystem.Operations;
using System.Diagnostics;

namespace AttendanceSystem.Gateway;

/// <summary>
/// Authenticated local-bridge event boundary, NOT the Realand device protocol.
/// Each authorized bridge signs an exact UTF-8 XML message + route + timestamp.
/// Device identity comes from the configured binding, never from XML.
/// </summary>
public sealed class AuthenticatedEventGateway
{
    public const int MaxXmlBytes = 8192;
    private const int MaxRequestsPerMinute = 2500;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly AttendanceEventStore _store;
    private readonly string _boundDeviceId;
    private readonly byte[] _secret;
    private readonly TimeProvider _clock;
    private readonly object _rateLock = new();
    private DateTimeOffset _rateWindowStart;
    private int _rateWindowCount;

    public AuthenticatedEventGateway(
        AttendanceEventStore store, string boundDeviceId, byte[] secret,
        TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (string.IsNullOrWhiteSpace(boundDeviceId) || boundDeviceId.Length > 80)
            throw new ArgumentException("An enrolled bridge device ID is required.", nameof(boundDeviceId));
        if (secret is null || secret.Length < 32)
            throw new ArgumentException("Use at least 256 bits of random bridge key material.", nameof(secret));
        _boundDeviceId = boundDeviceId;
        _secret = (byte[])secret.Clone();
        _clock = clock ?? TimeProvider.System;
        _rateWindowStart = _clock.GetUtcNow();
    }

    public GatewayOutcome Accept(
        string? deviceId, long signedAtUnixSeconds, string? signatureHex, string? xml)
    {
        var now = _clock.GetUtcNow();
        lock (_rateLock)
        {
            if (now - _rateWindowStart >= TimeSpan.FromMinutes(1) || now < _rateWindowStart)
            {
                _rateWindowStart = now;
                _rateWindowCount = 0;
            }
            if (++_rateWindowCount > MaxRequestsPerMinute)
                throw new GatewayRejection(429, "Rate limited.");
        }

        if (string.IsNullOrEmpty(xml) || StrictUtf8.GetByteCount(xml) > MaxXmlBytes)
            throw new GatewayRejection(413, "Invalid message length.");
        if (!string.Equals(deviceId, _boundDeviceId, StringComparison.Ordinal))
            throw new GatewayRejection(401, "Unknown bridge identity.");

        DateTimeOffset signedAt;
        try
        {
            signedAt = DateTimeOffset.FromUnixTimeSeconds(signedAtUnixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new GatewayRejection(401, "Invalid request time.");
        }
        if ((now - signedAt).Duration() > TimeSpan.FromMinutes(5))
            throw new GatewayRejection(401, "Expired request signature.");

        byte[] submitted;
        if (signatureHex is null || signatureHex.Length != 64)
            throw new GatewayRejection(401, "Missing request signature.");
        try
        {
            submitted = Convert.FromHexString(signatureHex);
        }
        catch (FormatException)
        {
            throw new GatewayRejection(401, "Malformed request signature.");
        }
        var expected = ComputeMac(_boundDeviceId, signedAtUnixSeconds, xml, _secret);
        if (!CryptographicOperations.FixedTimeEquals(submitted, expected))
            throw new GatewayRejection(401, "Request authentication failed.");

        var eventData = ParseAndValidate(xml, now);
        try
        {
            var start = Stopwatch.GetTimestamp();
            var result = _store.Save(new AttendanceEventInput(
                _boundDeviceId, eventData.EventKey, eventData.StudentId, eventData.OccurredAtUtc));
            var elapsed = Stopwatch.GetElapsedTime(start);
            // Metrics are updated only after SQLite transaction commit.
            // This path never labels a counter with a device/student/event key.
            if (result == AttendanceWriteResult.Inserted)
            {
                OperationalMetrics.RecordInserted(elapsed);
                return GatewayOutcome.Inserted;
            }
            OperationalMetrics.RecordReplay(elapsed);
            return GatewayOutcome.Duplicate;
        }
        catch (InvalidOperationException error)
        {
            if (error.Message.StartsWith("Device is not enrolled", StringComparison.Ordinal))
                throw new GatewayRejection(403, "Bridge device not authorized.");
            if (error.Message.StartsWith("Event key conflict", StringComparison.Ordinal))
                throw new GatewayRejection(409, "Conflicting event replay.");
            throw; // real persistence failures MUST NOT be acknowledged
        }
    }

    public static string Sign(
        string deviceId, long signedAtUnixSeconds, string xml, byte[] secret) =>
        Convert.ToHexString(ComputeMac(deviceId, signedAtUnixSeconds, xml, secret));

    private static byte[] ComputeMac(
        string deviceId, long signedAtUnixSeconds, string xml, byte[] secret)
    {
        // Domain-separate this signature from other uses of the same secret.
        var canonical = "POST\n/v1/events\n" + deviceId + "\n" +
                        signedAtUnixSeconds.ToString(CultureInfo.InvariantCulture) + "\n" + xml;
        return HMACSHA256.HashData(secret, StrictUtf8.GetBytes(canonical));
    }

    private static ValidatedEvent ParseAndValidate(string xml, DateTimeOffset now)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxXmlBytes,
                MaxCharactersFromEntities = 0
            };
            using var input = new StringReader(xml);
            using var reader = XmlReader.Create(input, settings);
            var doc = new XmlDocument { XmlResolver = null };
            doc.Load(reader);
            var root = doc.DocumentElement;
            if (root is null || root.Name != "TimeLog" || root.HasAttributes)
                throw new GatewayRejection(422, "Unsupported event structure.");

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XmlNode node in root.ChildNodes)
            {
                if (node is not XmlElement child || child.HasAttributes ||
                    child.ChildNodes.OfType<XmlElement>().Any() ||
                    (child.Name != "EventKey" && child.Name != "StudentID" &&
                     child.Name != "OccurredAtUtc") ||
                    !fields.TryAdd(child.Name, child.InnerText))
                    throw new GatewayRejection(422, "Unexpected event field.");
            }
            if (fields.Count != 3)
                throw new GatewayRejection(422, "Missing event fields.");
            var eventKey = fields["EventKey"];
            var studentId = fields["StudentID"];
            var timeText = fields["OccurredAtUtc"];
            if (!IsSafeKey(eventKey, 160) || !IsSafeKey(studentId, 128))
                throw new GatewayRejection(422, "Invalid event key or student key.");
            if (!(timeText.EndsWith("Z", StringComparison.Ordinal) ||
                  timeText.EndsWith("+00:00", StringComparison.Ordinal)) ||
                !DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var occurred) ||
                occurred.Offset != TimeSpan.Zero)
                throw new GatewayRejection(422, "UTC timestamp required.");
            if (occurred > now + TimeSpan.FromMinutes(5) ||
                occurred < now - TimeSpan.FromDays(90))
                throw new GatewayRejection(422, "Event clock is outside the accepted window.");
            return new ValidatedEvent(eventKey, studentId, occurred);
        }
        catch (XmlException)
        {
            throw new GatewayRejection(422, "Invalid XML.");
        }
    }

    private static bool IsSafeKey(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            return false;
        return value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');
    }

    private sealed record ValidatedEvent(
        string EventKey, string StudentId, DateTimeOffset OccurredAtUtc);
}

public enum GatewayOutcome
{
    Inserted,
    Duplicate
}

public sealed class GatewayRejection : Exception
{
    public int StatusCode { get; }

    public GatewayRejection(int statusCode, string description) : base(description) =>
        StatusCode = statusCode;
}
