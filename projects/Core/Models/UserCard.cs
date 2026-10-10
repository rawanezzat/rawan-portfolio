using System.ComponentModel.DataAnnotations;

namespace Credit2.Core.Models
{
    public class UserCard
    {
        [Key]
        public int CardId { get; set; }
        public int UserId { get; set; }               // Foreign key to User
        public string CardNumber { get; set; }        // e.g., "4407-04****1234"
        public string Bank { get; set; }              // e.g., "HSBC", "NBK"

        public User User { get; set; }                // Navigation property
    }
}
