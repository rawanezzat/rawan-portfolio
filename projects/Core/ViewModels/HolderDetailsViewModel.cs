// File: ViewModels/HolderDetailsViewModel.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Credit2.Core.ViewModels
{
    public class HolderDetailsViewModel
    {
        // Record Information (Read Only)
        public int RecordId { get; set; }
        public string HolderName { get; set; }
        public string BankName { get; set; }
        public string CardNumber { get; set; }
        public string Description { get; set; }

        [DataType(DataType.Date)]
        public DateTime StatementDate { get; set; }

        public string AccountantComment { get; set; }
        public string AccountantFileUrl { get; set; }
        public string CurrentPhase { get; set; }

        public string CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }

        // Auditor Feedback Information
        public string AuditorComment { get; set; }
        public string AuditorFileUrl { get; set; }
        public string AuditorName { get; set; }
        public DateTime? AuditDate { get; set; }
        public bool IsEditMode { get; set; }

        // Holder Fields (Editable)
        public List<HolderFieldViewModel> HolderFields { get; set; } = new List<HolderFieldViewModel>();
    }

    public class HolderFieldViewModel
    {
        public int HolderFieldId { get; set; }

        [Required(ErrorMessage = "Date is required")]
        [DataType(DataType.Date)]
        [Display(Name = "Date")]
        public DateTime Date { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "Comment is required")]
        [StringLength(500, ErrorMessage = "Comment cannot exceed 500 characters")]
        [Display(Name = "Comment")]
        public string Comment { get; set; }

        [Required(ErrorMessage = "Amount is required")]
        [Range(0.01, 9999999.99, ErrorMessage = "Amount must be greater than 0")]
        [Display(Name = "Amount")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "Currency is required")]
        [Display(Name = "Currency")]
        public string Currency { get; set; } = "EGP";

        [Required(ErrorMessage = "Cost Center is required")]
        [Display(Name = "Cost Center")]
        public string CostCenter { get; set; }

        [Required(ErrorMessage = "Expenses type is required")]
        [Display(Name = "Expenses")]
        public string Expenses { get; set; }

        [Display(Name = "Invoice Number")]
        public string Invoice { get; set; }

        [Display(Name = "Attachment")]
        public IFormFile File { get; set; }

        public string FileUrl { get; set; }

        // Flag to identify existing fields
        public bool IsExisting { get; set; }
    }
}