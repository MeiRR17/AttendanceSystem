using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AttendanceSystem.Services
{
    public static class HolidayFetcher
    {
        public static async Task<List<DateTime>> GetHebrewHolidays(int year)
        {
            var holidays = new List<DateTime>();

            using (var client = new HttpClient())
            {
                var response = await client.GetStringAsync($"https://www.hebcal.com/hebcal?v=1&cfg=json&year={year}&maj=on&mod=on&minor=on");
                var json = JObject.Parse(response);

                foreach (var item in json["items"])
                {
                    var date = DateTime.Parse(item["date"].ToString());
                    holidays.Add(date);
                }
            }

            return holidays;
        }
    }
}
