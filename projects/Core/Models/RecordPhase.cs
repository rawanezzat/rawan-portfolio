namespace Credit2.Core.Models
{
    public class RecordPhase
    {
        public int RecordPhaseId { get; set; }
        public int RecordId { get; set; }
        public Records Record { get; set; }

        public string PhaseName { get; set; } // "HolderDetails", "Auditor", "BoardMember", "Presenting"
        public int? CurrentUserId { get; set; }
        public User? CurrentUser { get; set; }

        public string? Status { get; set; }       // e.g., "Pending", "Approved", "Rejected", "Completed"

    }
}
