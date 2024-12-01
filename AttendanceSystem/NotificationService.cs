using System;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace AttendanceSystem
{
    public class NotificationService
    {
        private readonly string accountSid = "AC38b57ae87794f00812a11e3c34598a4f"; //SID
        private readonly string authToken = "d07060897130267f1decd1b2023fd28c"; //Auth Token

        public NotificationService()
        {
            TwilioClient.Init(accountSid, authToken);
        }

        public void SendLateNotification(string studentName, string studentID, string studentPhoneNumber, string date)
        {
            string messageBody = $"Hello {studentName},\n\n" +
                                 $"We noticed that you were late for class today, {date}. " +
                                 "Please ensure to be on time in the future to avoid any issues.\n\n" +
                                 "Thank you,\n" +
                                 "[Your School Name]";

            var messageOptions = new CreateMessageOptions(new PhoneNumber($"whatsapp:{studentPhoneNumber}"));
            messageOptions.From = new PhoneNumber("whatsapp:+14155238886");
            messageOptions.Body = messageBody;

            var message = MessageResource.Create(messageOptions);
            Console.WriteLine("Message sent: " + message.Sid);
        }
    }
}
