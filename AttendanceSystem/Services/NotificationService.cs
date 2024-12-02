using System;
using System.Globalization;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace AttendanceSystem.Services
{
    public class NotificationService
    {
        private readonly string accountSid = "your_account_sid";
        private readonly string authToken = "your_auth_token";

        public NotificationService()
        {
            TwilioClient.Init(accountSid, authToken);
        }

        public void SendLateNotification(string studentName, string studentID, string studentPhoneNumber, DateTime date)
        {
            // Convert Gregorian date to Hebrew date
            HebrewCalendar hebrewCalendar = new HebrewCalendar();
            int hebrewDay = hebrewCalendar.GetDayOfMonth(date);
            int hebrewMonth = hebrewCalendar.GetMonth(date);
            int hebrewYear = hebrewCalendar.GetYear(date);
            string hebrewMonthName = GetHebrewMonthName(hebrewMonth, hebrewYear);
            string hebrewDayFormatted = GetFormattedHebrewDay(hebrewDay);

            // Check for ראש חודש
            bool isRoshChodesh = IsRoshChodesh(hebrewDay, hebrewMonth, hebrewYear);

            // Format the date in Hebrew
            string hebrewDate = $"{hebrewDayFormatted} ב{hebrewMonthName}";

            // Create the message in Hebrew
            string messageBody = isRoshChodesh
                ? $"שלום, {studentName}\n" +
                  $"היום {hebrewDate}, ראש חודש שמנו לב שאיחרת לשיעור.\n" +
                  $"אנא הקפד להגיע בזמן.\n" +
                  $"תודה,\n" +
                  $"הנהלת הישיבה"
                : $"שלום, {studentName}\n" +
                  $"היום {hebrewDate} שמנו לב שאיחרת לשיעור.\n" +
                  $"אנא הקפד להגיע בזמן.\n" +
                  $"תודה,\n" +
                  $"הנהלת הישיבה";

            var messageOptions = new CreateMessageOptions(new PhoneNumber($"whatsapp:{studentPhoneNumber}"));
            messageOptions.From = new PhoneNumber("whatsapp:+14155238886");
            messageOptions.Body = messageBody;

            try
            {
                var message = MessageResource.Create(messageOptions);
                Console.WriteLine("Message sent: " + message.Sid);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to send message: " + ex.Message);
            }
        }

        private string GetFormattedHebrewDay(int day)
        {
            string[] hebrewDays = { "א", "ב", "ג", "ד", "ה", "ו", "ז", "ח", "ט", "י", "יא", "יב", "יג", "יד", "טו", "טז", "יז", "יח", "יט", "כ", "כא", "כב", "כג", "כד", "כה", "כו", "כז", "כח", "כט", "ל" };
            string formattedDay = hebrewDays[day - 1];

            if (day <= 10 || day == 20 || day == 30)
            {
                formattedDay += "'";
            }
            else if (formattedDay.Contains("ו"))
            {
                formattedDay = formattedDay.Insert(1, "\"");
            }

            return formattedDay;
        }

        private string GetHebrewMonthName(int month, int year)
        {
            string[] hebrewMonths = { "", "תשרי", "חשון", "כסליו", "טבת", "שבט", "אדר", "אדר א", "ניסן", "אייר", "סיון", "תמוז", "אב", "אלול" };

            if (new HebrewCalendar().IsLeapYear(year))
            {
                hebrewMonths[6] = "אדר ב";
            }
            return hebrewMonths[month];
        }

        private bool IsRoshChodesh(int day, int month, int year)
        {
            HebrewCalendar hebrewCalendar = new HebrewCalendar();
            int daysInMonth = hebrewCalendar.GetDaysInMonth(year, month);

            if (daysInMonth == 29)
            {
                // The next month has two days of ראש חודש: 30th of the current month and 1st of the next month
                return day == 30 || day == 1;
            }
            else // daysInMonth == 30
            {
                // The next month has one day of ראש חודש: 1st of the next month
                return day == 1;
            }
        }
    }
}
