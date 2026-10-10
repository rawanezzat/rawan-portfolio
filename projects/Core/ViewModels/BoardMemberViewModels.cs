using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Credit2.Core.ViewModels
{
    public class BoardMemberRecordViewModel
    {
        public int RecordId { get; set; }
        public string HolderName { get; set; }
        public string BankName { get; set; }
        public string CardNumber { get; set; }
        public string Description { get; set; }
        public DateTime StatementDate { get; set; }
        public string AccountantComment { get; set; }
        public string AccountantFileUrl { get; set; }
        public string CurrentPhase { get; set; }
        public int HolderFieldsCount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; }
        public int AuditorFieldsCount { get; set; }
        public string AuditStatus { get; set; }
        public int BoardMemberFieldsCount { get; set; }
        public string BoardMemberStatus { get; set; }
    }

    public class BoardMemberDetailsViewModel
    {
        public int RecordId { get; set; }
        public string HolderName { get; set; }
        public string BankName { get; set; }
        public string CardNumber { get; set; }
        public string Description { get; set; }
        public DateTime StatementDate { get; set; }
        public string AccountantComment { get; set; }
        public string AccountantFileUrl { get; set; }
        public string CurrentPhase { get; set; }

        // Holder Fields (Read Only)
        public List<HolderFieldViewModel> HolderFields { get; set; } = new();

        // Auditor Fields (Read Only)
        public List<AuditorFieldViewModel> ExistingAuditorFields { get; set; } = new();

        // Existing Board Member Fields (Read Only)
        public List<BoardMemberFieldViewModel> ExistingBoardMemberFields { get; set; } = new();

        // New Board Member Fields (Editable)
        public List<BoardMemberFieldViewModel> BoardMemberFields { get; set; } = new();
    }

    public class BoardMemberFieldViewModel
    {
        public int? BoardMemberFieldId { get; set; }

        [Display(Name = "Comment")]
        [Required(ErrorMessage = "Comment is required")]
        public string Comment { get; set; }

        [Display(Name = "Attachment")]
        public IFormFile BoardMemberFile { get; set; }

        [Display(Name = "File URL")]
        public string BoardMemberFileUrl { get; set; }

        [Display(Name = "Status")]
        public string Status { get; set; } = "Pending";

        [Display(Name = "Review Date")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}