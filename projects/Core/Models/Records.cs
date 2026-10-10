using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Credit2.Core.Models
{
    public class Records
    {
        [Key]
        public int RecordsId { get; set; }

        // Who the record is for
        [Required(ErrorMessage = "Holder is required")]
        [Display(Name = "Holder")]
        public int HolderId { get; set; }

        [ForeignKey("HolderId")]
        public User Holder { get; set; }          // Navigation property

        [Required(ErrorMessage = "Card is required")]
        [Display(Name = "Card")]
        public int UserCardId { get; set; }
        [ForeignKey("UserCardId")]
        public UserCard UserCard { get; set; }    // Bank & CardNumber

        [StringLength(500)]
        public string Description { get; set; }

        // Accountant info (assigned accountant)
        [Required]
        public int AccountantUserId { get; set; }
        [ForeignKey("AccountantUserId")]
        public User AccountantUser { get; set; }

        // ** NEW FIELD - Who created this record **
        [Required]
        public int CreatedByUserId { get; set; }
        [ForeignKey("CreatedByUserId")]
        public User CreatedByUser { get; set; }

        // Optional: Track when it was created
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Comments / Files
        [StringLength(1000)]
        public string AccountantComment { get; set; }
        public string? AccountantFileUrl { get; set; }

        // Other phases
        public List<HolderField> HolderFields { get; set; } = new();
        public List<RecordPhase> RecordPhases { get; set; } = new();
        public List<AuditorField> AuditorApprovals { get; set; } = new();
        public List<BoardMemberField> BoardMemberApprovals { get; set; } = new();

        [Required]
        [StringLength(50)]
        public string CurrentPhase { get; set; }

        public DateTime? StatusTime { get; set; }

        [Required(ErrorMessage = "Statement date is required")]
        [DataType(DataType.Date)]
        [Display(Name = "Statement Date")]
        public DateTime StatementDate { get; set; }
    }
}