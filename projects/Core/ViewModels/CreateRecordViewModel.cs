using System.ComponentModel.DataAnnotations;

namespace Credit2.Core.ViewModels
{
    public class CreateRecordViewModel
    {
        public int? RecordId { get; set; }

        [Required(ErrorMessage = "Holder is required")]
        [Display(Name = "Holder")]
        public int HolderId { get; set; }

        [Required(ErrorMessage = "Card is required")]
        [Display(Name = "Card")]
        public int UserCardId { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        [StringLength(1000)]
        public string? AccountantComment { get; set; }

        [Required(ErrorMessage = "Statement date is required")]
        [DataType(DataType.Date)]
        [Display(Name = "Statement Date")]
        public DateTime StatementDate { get; set; } = DateTime.Today;

        // File upload - NOT required
        public IFormFile? AccountantFile { get; set; }

        // File URL - NOT required
        public string? AccountantFileUrl { get; set; }

        // For display purposes only - NOT required, no validation
        public string? HolderName { get; set; }
        public string? CardNumber { get; set; }
        public string? BankName { get; set; }
        public string? CurrentPhase { get; set; }
    }
}