namespace Credit2.Core.Models
{
    public class AuditorField
    {
        public int AuditorFieldId { get; set; }
        public int RecordId { get; set; }
        public int? AuditorUserId { get; set; }
        public string Comment { get; set; }
        public string AuditFileUrl { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;


        // Navigation properties
        public Records Record { get; set; }
        public User? AuditorUser { get; set; }
    }

}
