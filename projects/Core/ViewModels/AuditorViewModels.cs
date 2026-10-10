// File: ViewModels/AuditorViewModels.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Credit2.Core.ViewModels
{
    public class AuditorViewModels
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
    }

    public class AuditorDetailsViewModel
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

        // Existing Auditor Fields (Read Only)
        public List<AuditorFieldViewModel> ExistingAuditorFields { get; set; } = new();

        // New Auditor Fields (Editable)
        public List<AuditorFieldViewModel> AuditorFields { get; set; } = new();
    }

    public class AuditorFieldViewModel
    {
        public int? AuditorFieldId { get; set; }

        [Display(Name = "Comment")]
        public string Comment { get; set; }

        [Display(Name = "Attachment")]
        public IFormFile AuditFile { get; set; }

        [Display(Name = "File URL")]
        public string AuditFileUrl { get; set; }

        [Display(Name = "Status")]
        public string Status { get; set; } = "Pending";

        [Display(Name = "Review Date")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}