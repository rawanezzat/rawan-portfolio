namespace Credit2.Core.Models
{
    public class BoardMemberField
    {
        public int BoardMemberFieldId { get; set; }
        public int RecordId { get; set; }
        public int? BoardMemberUserId { get; set; }
        public string Comment { get; set; }
        public string BoardMemberFileUrl { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation properties
        public Records Record { get; set; }
        public User? BoardMemberUser { get; set; }
    }

}
