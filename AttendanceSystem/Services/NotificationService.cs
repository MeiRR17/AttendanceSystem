namespace AttendanceSystem.Services;

/// <summary>
/// Retired direct-to-Twilio prototype. It previously sent ad-hoc WhatsApp
/// messages without consent, approved templates or a durable outbox.
/// This method always fails closed: only a future reviewed, authorized
/// delivery adapter may transmit messages.
/// </summary>
[Obsolete("Unsafe direct WhatsApp sending removed; use opt-in NotificationOutboxService.")]
public sealed class NotificationService
{
    public void SendLateNotification(
        string studentName, string studentID, string studentPhoneNumber, DateTime date) =>
        throw new NotSupportedException(
            "Direct WhatsApp messaging is disabled. No consent or approved transport configured.");
}
