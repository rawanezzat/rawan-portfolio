// Models/User.cs
using Microsoft.AspNetCore.Identity;

namespace Credit2.Core.Models
{
    public class User : IdentityUser<int>  // Use int as the key type
    {
        // Remove this line - IdentityUser already has Id
        // public int UserId { get; set; }

        // These properties are already in IdentityUser:
        // - Id (int now)
        // - UserName
        // - Email
        // - PhoneNumber
        // - etc.


        [PersonalData]
        public string Name { get; set; }

        [PersonalData]
        public string Role { get; set; } // Admin, Accountant, Auditor, BoardMember, Holder

        // Board Membership
        public int? BoardMemberForUserId { get; set; }
        public string? BoardMemberForUserName { get; set; }

        // Navigation
        public List<UserCard> Cards { get; set; } = new();

        // Optional: Add any additional fields you need
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}