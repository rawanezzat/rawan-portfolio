using System;

namespace Credit2.Core.Models
{
    public class HolderField
    {
        public int HolderFieldId { get; set; }

        public DateTime Date { get; set; }
        public string Comment { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string CostCenter { get; set; }
        public string Expenses { get; set; }
        public string Invoice { get; set; }
        public string FileUrl { get; set; }

        // Link to Records
        public int RecordId { get; set; }
        public Records Record { get; set; }

        // Link to Holder (User)
        public int? HolderId { get; set; }
        public User? Holder { get; set; }
    }
}
