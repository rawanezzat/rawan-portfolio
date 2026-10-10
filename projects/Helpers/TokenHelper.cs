using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Credit2.Core.Models;
using Credit2.Data;

namespace Credit2.Helpers
{
    public static class TokenHelper
    {
        public static string GenerateAccessToken(int recordId, int userId, string role)
        {
            // Create token with record ID, user ID, role, and 24-hour expiry
            string tokenData = $"{recordId}|{userId}|{role}|{DateTime.Now.AddHours(24).Ticks}";
            byte[] data = System.Text.Encoding.UTF8.GetBytes(tokenData);
            return Convert.ToBase64String(data);
        }

        public static bool TryParseToken(string token, out int recordId, out int userId, out string role, out DateTime expiry)
        {
            recordId = 0;
            userId = 0;
            role = "";
            expiry = DateTime.MinValue;

            try
            {
                byte[] data = Convert.FromBase64String(token);
                string tokenData = System.Text.Encoding.UTF8.GetString(data);
                var parts = tokenData.Split('|');

                if (parts.Length != 4)
                    return false;

                recordId = int.Parse(parts[0]);
                userId = int.Parse(parts[1]);
                role = parts[2];
                long expiryTicks = long.Parse(parts[3]);
                expiry = new DateTime(expiryTicks);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}