using Credit2.Core.Models;
using Credit2.Core.ViewModels;
using Credit2.Data;
using Credit2.Helpers;
using Credit2.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Credit2.Controllers
{
    public class AuditorController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AuditorController> _logger;
        private readonly EmailService _emailService;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public AuditorController(
            ApplicationDbContext context,
            ILogger<AuditorController> logger,
            EmailService emailService,
            IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _logger = logger;
            _emailService = emailService;
            _webHostEnvironment = webHostEnvironment;
        }

        // ================= AUDITOR INDEX (DASHBOARD) =================
        public async Task<IActionResult> Index()
        {
            try
            {
                var records = await _context.Records
                    .Include(r => r.Holder)
                    .Include(r => r.UserCard)
                    .Include(r => r.HolderFields)
                    .Include(r => r.AuditorApprovals)
                    .Where(r => r.CurrentPhase == "Auditor")
                    .OrderByDescending(r => r.StatementDate)
                    .ToListAsync();

                var viewModel = records.Select(r => new AuditorViewModels
                {
                    RecordId = r.RecordsId,
                    HolderName = r.Holder?.Name ?? "N/A",
                    BankName = r.UserCard?.Bank ?? "N/A",
                    CardNumber = r.UserCard?.CardNumber ?? "N/A",
                    Description = r.Description ?? "N/A",
                    StatementDate = r.StatementDate,
                    AccountantComment = r.AccountantComment ?? "---",
                    AccountantFileUrl = r.AccountantFileUrl,
                    CurrentPhase = r.CurrentPhase ?? "Auditor",
                    HolderFieldsCount = r.HolderFields?.Count ?? 0,
                    TotalAmount = r.HolderFields?.Sum(hf => hf.Amount) ?? 0,
                    Currency = r.HolderFields?.FirstOrDefault()?.Currency ?? "EGP",
                    AuditorFieldsCount = r.AuditorApprovals?.Count ?? 0,
                    AuditStatus = r.AuditorApprovals?
                        .OrderByDescending(a => a.CreatedAt)
                        .FirstOrDefault()?.Status ?? "Pending"
                }).ToList();

                ViewBag.TotalPending = viewModel.Count;
                ViewBag.SuccessMessage = TempData["SuccessMessage"];
                ViewBag.ErrorMessage = TempData["ErrorMessage"];

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading auditor dashboard");
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return RedirectToAction("Index", "Home");
            }
        }



        // ================= ACCESS AUDITOR RECORD FROM EMAIL (NO LOGIN REQUIRED) =================
        public async Task<IActionResult> AccessAuditorRecord(string token)
        {
            try
            {
                _logger.LogInformation($"AccessAuditorRecord called with token");

                if (string.IsNullOrEmpty(token))
                {
                    TempData["ErrorMessage"] = "Invalid access link.";
                    return RedirectToAction("Index", "Home");
                }

                // Parse the token
                if (!TokenHelper.TryParseToken(token, out int recordId, out int tokenAuditorId, out string role, out DateTime expiry))
                {
                    _logger.LogWarning("Invalid token format");
                    TempData["ErrorMessage"] = "Invalid access link.";
                    return RedirectToAction("Index", "Home");
                }

                // FORCE AUDITOR ID TO BE 4
                int forcedAuditorId = 4;

                _logger.LogInformation($"Token contained AuditorId: {tokenAuditorId}, but forcing to: {forcedAuditorId}");

                // Check if token has expired
                if (DateTime.Now > expiry)
                {
                    _logger.LogWarning($"Expired token for Record #{recordId}");
                    TempData["ErrorMessage"] = "This access link has expired. Please contact support.";
                    return RedirectToAction("Index", "Home");
                }

                // Verify the forced auditor exists
                var auditor = await _context.Users.FindAsync(forcedAuditorId);
                if (auditor == null)
                {
                    _logger.LogError($"Forced auditor ID {forcedAuditorId} not found!");
                    TempData["ErrorMessage"] = "System error: Default auditor not found.";
                    return RedirectToAction("Index", "Home");
                }

                // Verify the record exists
                var record = await _context.Records
                    .Include(r => r.Holder)
                    .FirstOrDefaultAsync(r => r.RecordsId == recordId);

                if (record == null)
                {
                    _logger.LogWarning($"Record #{recordId} not found");
                    TempData["ErrorMessage"] = "Record not found.";
                    return RedirectToAction("Index", "Home");
                }

                // CRITICAL: Store the FORCED auditor ID (4) in session
                HttpContext.Session.SetInt32("AuditorId", forcedAuditorId);
                HttpContext.Session.SetInt32("RecordId", recordId);
                HttpContext.Session.SetInt32("HolderId", record.HolderId);
                HttpContext.Session.SetString("AccessToken", token);
                HttpContext.Session.SetString("UserRole", "Auditor");

                // Verify session was set
                int? checkAuditorId = HttpContext.Session.GetInt32("AuditorId");
                _logger.LogInformation($"Session set - Forced AuditorId: {checkAuditorId} (should be 4)");

                TempData["TokenAccess"] = true;
                TempData["SuccessMessage"] = "Access granted! Please complete your audit review.";

                return RedirectToAction("AddAuditorFields", new { recordId = recordId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AccessAuditorRecord");
                TempData["ErrorMessage"] = "An error occurred. Please try again.";
                return RedirectToAction("Index", "Home");
            }
        }        // ================= ADD AUDITOR FIELDS (GET) =================
        // ================= ADD AUDITOR FIELDS (GET) =================
        public async Task<IActionResult> AddAuditorFields(int recordId)
        {
            try
            {
                _logger.LogInformation($"AddAuditorFields called for recordId={recordId}");

                // Try to get auditor ID from session (set by AccessAuditorRecord)
                int? auditorId = HttpContext.Session.GetInt32("AuditorId");
                string userRole = HttpContext.Session.GetString("UserRole");
                bool isTokenAccess = TempData["TokenAccess"] != null && (bool)TempData["TokenAccess"];

                // If no session, check if they're authenticated normally
                if (!auditorId.HasValue)
                {
                    var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                    if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int authAuditorId))
                    {
                        auditorId = authAuditorId;
                        userRole = "Authenticated";
                        _logger.LogInformation($"Using authenticated user ID: {authAuditorId}");
                    }
                    else
                    {
                        _logger.LogWarning("No auditor ID found in session or claims");
                        TempData["ErrorMessage"] = "Please use the link from your email to access this page, or log in.";
                        return RedirectToAction("Login", "Account");
                    }
                }

                var record = await _context.Records
                    .Include(r => r.Holder)
                    .Include(r => r.UserCard)
                    .Include(r => r.HolderFields)
                    .Include(r => r.AuditorApprovals)
                        .ThenInclude(a => a.AuditorUser)
                    .FirstOrDefaultAsync(r => r.RecordsId == recordId);

                if (record == null)
                {
                    TempData["ErrorMessage"] = "Record not found.";
                    return RedirectToAction("Index", "Records");
                }

                // Verify the record is in the correct phase
                if (record.CurrentPhase != "Auditor" && record.CurrentPhase != "Holder" && record.CurrentPhase != "BoardMember")
                {
                    _logger.LogWarning($"Record #{recordId} is in phase {record.CurrentPhase}, not accessible for audit");
                    TempData["ErrorMessage"] = "This record is not available for audit review at this time.";
                    return RedirectToAction("Index", "Records");
                }

                var viewModel = new AuditorDetailsViewModel
                {
                    RecordId = record.RecordsId,
                    HolderName = record.Holder?.Name ?? "N/A",
                    BankName = record.UserCard?.Bank ?? "N/A",
                    CardNumber = record.UserCard?.CardNumber ?? "N/A",
                    Description = record.Description ?? "N/A",
                    StatementDate = record.StatementDate,
                    AccountantComment = record.AccountantComment ?? "---",
                    AccountantFileUrl = record.AccountantFileUrl,
                    CurrentPhase = record.CurrentPhase ?? "Auditor",
                    HolderFields = record.HolderFields?.Select(hf => new HolderFieldViewModel
                    {
                        Date = hf.Date,
                        Comment = hf.Comment,
                        Amount = hf.Amount,
                        Currency = hf.Currency,
                        CostCenter = hf.CostCenter,
                        Expenses = hf.Expenses,
                        Invoice = hf.Invoice,
                        FileUrl = hf.FileUrl
                    }).ToList() ?? new List<HolderFieldViewModel>(),
                    ExistingAuditorFields = record.AuditorApprovals?.Select(af => new AuditorFieldViewModel
                    {
                        AuditorFieldId = af.AuditorFieldId,
                        Comment = af.Comment,
                        AuditFileUrl = af.AuditFileUrl,
                        Status = af.Status,
                        CreatedAt = af.CreatedAt,
                    }).ToList() ?? new List<AuditorFieldViewModel>(),
                    AuditorFields = new List<AuditorFieldViewModel>
            {
                new AuditorFieldViewModel { Status = "" }
            }
                };

                ViewBag.IsTokenAccess = isTokenAccess;
                ViewBag.UserRole = userRole;

                return View("AddAuditorFields", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading AddAuditorFields");
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return RedirectToAction("Index", "Records");
            }
        }
        // ================= SAVE AUDITOR FIELDS (POST) =================
        [HttpPost]
        [ValidateAntiForgeryToken]
        
        public async Task<IActionResult> SaveAuditorFields(AuditorDetailsViewModel model)
        {
            try
            {
                // Remove validation for read-only properties
                ModelState.Remove("HolderName");
                ModelState.Remove("BankName");
                ModelState.Remove("CardNumber");
                ModelState.Remove("Description");
                ModelState.Remove("AccountantComment");
                ModelState.Remove("AccountantFileUrl");
                ModelState.Remove("CurrentPhase");
                ModelState.Remove("HolderFields");
                ModelState.Remove("ExistingAuditorFields");

                var record = await _context.Records
                    .Include(r => r.Holder)
                    .Include(r => r.UserCard)
                    .FirstOrDefaultAsync(r => r.RecordsId == model.RecordId);

                if (record == null)
                {
                    TempData["ErrorMessage"] = "Record not found.";
                    return RedirectToAction("Index", "Records");
                }

                // Validate that status is selected
                var firstField = model.AuditorFields?.FirstOrDefault();
                if (firstField == null || string.IsNullOrEmpty(firstField.Status))
                {
                    ModelState.AddModelError("AuditorFields[0].Status", "Please select a status.");
                    return await ReloadAuditorView(model.RecordId);
                }

                // Validate that comment is provided
                if (string.IsNullOrWhiteSpace(firstField.Comment))
                {
                    ModelState.AddModelError("AuditorFields[0].Comment", "Please enter an audit comment.");
                    return await ReloadAuditorView(model.RecordId);
                }

                // Filter out empty comments
                var validFields = model.AuditorFields?
                    .Where(f => !string.IsNullOrWhiteSpace(f.Comment))
                    .ToList() ?? new List<AuditorFieldViewModel>();

                if (!validFields.Any())
                {
                    ModelState.AddModelError("", "Please add at least one audit comment.");
                    return await ReloadAuditorView(model.RecordId);
                }

                // Get the selected status
                string selectedStatus = firstField.Status;

                // ========== FORCE AUDITOR ID TO BE 4 ==========
                int forcedAuditorId = 4;

                // Get holder ID from session or record
                int? holderId = HttpContext.Session.GetInt32("HolderId") ?? record.HolderId;

                _logger.LogInformation($"SaveAuditorFields - FORCING AuditorUserId: {forcedAuditorId}, HolderId: {holderId} for Record #{record.RecordsId}");

                // Process each auditor field
                foreach (var fieldVM in validFields)
                {
                    string fileUrl = null;

                    // Handle file upload
                    if (fieldVM.AuditFile != null && fieldVM.AuditFile.Length > 0)
                    {
                        // Validate file size (10MB max)
                        if (fieldVM.AuditFile.Length > 10 * 1024 * 1024)
                        {
                            ModelState.AddModelError("", $"File {fieldVM.AuditFile.FileName} exceeds 10MB limit.");
                            return await ReloadAuditorView(model.RecordId);
                        }

                        // Validate file extension
                        string[] allowedExtensions = { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx", ".xls", ".xlsx" };
                        string fileExtension = Path.GetExtension(fieldVM.AuditFile.FileName).ToLowerInvariant();

                        if (!allowedExtensions.Contains(fileExtension))
                        {
                            ModelState.AddModelError("", $"File type {fileExtension} is not allowed.");
                            return await ReloadAuditorView(model.RecordId);
                        }

                        string folder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "audit-files");
                        Directory.CreateDirectory(folder);

                        string fileName = $"{Guid.NewGuid()}_{Path.GetFileName(fieldVM.AuditFile.FileName)}";
                        string fullPath = Path.Combine(folder, fileName);

                        using (var stream = new FileStream(fullPath, FileMode.Create))
                        {
                            await fieldVM.AuditFile.CopyToAsync(stream);
                        }

                        fileUrl = "/uploads/audit-files/" + fileName;
                    }

                    // Create AuditorField entity with FORCED auditor ID (4)
                    var auditorField = new AuditorField
                    {
                        RecordId = record.RecordsId,
                        AuditorUserId = forcedAuditorId, // ALWAYS 4
                        Comment = fieldVM.Comment,
                        AuditFileUrl = fileUrl,
                        Status = selectedStatus,
                        CreatedAt = DateTime.Now
                    };

                    _context.AuditorField.Add(auditorField);
                    _logger.LogInformation($"Added AuditorField for Record #{record.RecordsId} by FORCED Auditor #{forcedAuditorId} (Holder #{holderId} created the record)");
                }

                // Determine next phase and send appropriate emails based on status
                if (selectedStatus == "Redirect")
                {
                    record.CurrentPhase = "Holder";
                    record.StatusTime = DateTime.Now;
                    await SendEmailToHolderForCorrection(record, firstField.Comment);
                    TempData["SuccessMessage"] = "Record redirected to holder for corrections.";
                }
                else if (selectedStatus == "Approved")
                {
                    record.CurrentPhase = "BoardMember";
                    record.StatusTime = DateTime.Now;
                    await SendEmailToBoardMembers(record, validFields.Count, selectedStatus);
                    TempData["SuccessMessage"] = "Audit review approved and sent to board members!";
                }
                else if (selectedStatus == "Rejected")
                {
                    record.CurrentPhase = "Auditor";
                    record.StatusTime = DateTime.Now;

                    // ADD THIS LINE TO SEND REJECTION EMAIL TO ACCOUNTANT
                    await SendRejectionEmailToAccountant(record, firstField.Comment);

                    _logger.LogInformation($"Audit review rejected for record #{record.RecordsId} by FORCED Auditor #{forcedAuditorId}");
                    TempData["SuccessMessage"] = "Audit review rejected. Record has been updated and accountant has been notified.";
                }

                await _context.SaveChangesAsync();

                return RedirectToAction("Index", "Records");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving auditor fields");
                ModelState.AddModelError("", $"Error saving details: {ex.Message}");
                return await ReloadAuditorView(model?.RecordId ?? 0);
            }
        }        // ================= VIEW RECORD DETAILS =================
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var record = await _context.Records
                    .Include(r => r.Holder)
                    .Include(r => r.UserCard)
                    .Include(r => r.AccountantUser)
                    .Include(r => r.HolderFields)
                    .Include(r => r.AuditorApprovals)
                        .ThenInclude(a => a.AuditorUser)
                    .FirstOrDefaultAsync(r => r.RecordsId == id);

                if (record == null)
                    return NotFound();

                var viewModel = new AuditorDetailsViewModel
                {
                    RecordId = record.RecordsId,
                    HolderName = record.Holder?.Name,
                    BankName = record.UserCard?.Bank,
                    CardNumber = record.UserCard?.CardNumber,
                    Description = record.Description,
                    StatementDate = record.StatementDate,
                    AccountantComment = record.AccountantComment,
                    AccountantFileUrl = record.AccountantFileUrl,
                    CurrentPhase = record.CurrentPhase,
                    HolderFields = record.HolderFields?.Select(hf => new HolderFieldViewModel
                    {
                        Date = hf.Date,
                        Comment = hf.Comment,
                        Amount = hf.Amount,
                        Currency = hf.Currency,
                        CostCenter = hf.CostCenter,
                        Expenses = hf.Expenses,
                        Invoice = hf.Invoice,
                        FileUrl = hf.FileUrl
                    }).ToList() ?? new List<HolderFieldViewModel>(),
                    ExistingAuditorFields = record.AuditorApprovals?.Select(af => new AuditorFieldViewModel
                    {
                        AuditorFieldId = af.AuditorFieldId,
                        Comment = af.Comment,
                        AuditFileUrl = af.AuditFileUrl,
                        Status = af.Status,
                        CreatedAt = af.CreatedAt
                    }).ToList() ?? new List<AuditorFieldViewModel>(),
                    AuditorFields = new List<AuditorFieldViewModel>()
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading record details");
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return RedirectToAction("Index", "Records");
            }
        }

        // ================= HELPER METHODS =================
        private async Task<IActionResult> ReloadAuditorView(int recordId)
        {
            var record = await _context.Records
                .Include(r => r.Holder)
                .Include(r => r.UserCard)
                .Include(r => r.HolderFields)
                .Include(r => r.AuditorApprovals)
                    .ThenInclude(a => a.AuditorUser)
                .FirstOrDefaultAsync(r => r.RecordsId == recordId);

            if (record == null)
            {
                return RedirectToAction("Index");
            }

            var viewModel = new AuditorDetailsViewModel
            {
                RecordId = record.RecordsId,
                HolderName = record.Holder?.Name ?? "N/A",
                BankName = record.UserCard?.Bank ?? "N/A",
                CardNumber = record.UserCard?.CardNumber ?? "N/A",
                Description = record.Description ?? "N/A",
                StatementDate = record.StatementDate,
                AccountantComment = record.AccountantComment ?? "---",
                AccountantFileUrl = record.AccountantFileUrl,
                CurrentPhase = record.CurrentPhase ?? "Auditor",
                HolderFields = record.HolderFields?.Select(hf => new HolderFieldViewModel
                {
                    Date = hf.Date,
                    Comment = hf.Comment,
                    Amount = hf.Amount,
                    Currency = hf.Currency,
                    CostCenter = hf.CostCenter,
                    Expenses = hf.Expenses,
                    Invoice = hf.Invoice,
                    FileUrl = hf.FileUrl
                }).ToList() ?? new List<HolderFieldViewModel>(),
                ExistingAuditorFields = record.AuditorApprovals?.Select(af => new AuditorFieldViewModel
                {
                    AuditorFieldId = af.AuditorFieldId,
                    Comment = af.Comment,
                    AuditFileUrl = af.AuditFileUrl,
                    Status = af.Status,
                    CreatedAt = af.CreatedAt
                }).ToList() ?? new List<AuditorFieldViewModel>(),
                AuditorFields = new List<AuditorFieldViewModel>
                {
                    new AuditorFieldViewModel { Status = "" } // Empty string as default
                }
            };

            return View("AddAuditorFields", viewModel);
        }

        private async Task<int> GetCurrentUserId()
        {
            // ALWAYS return auditor ID 4
            int forcedAuditorId = 4;

            // Verify that auditor 4 exists
            var auditor = await _context.Users.FindAsync(forcedAuditorId);
            if (auditor == null)
            {
                _logger.LogError($"Forced auditor ID {forcedAuditorId} not found in database!");
                throw new Exception($"Auditor with ID {forcedAuditorId} does not exist. Please run seed to create default auditor.");
            }

            _logger.LogInformation($"GetCurrentUserId: Forcing return of auditor ID: {forcedAuditorId}");
            return forcedAuditorId;
        }

        // ================= SEND REJECTION EMAIL TO ACCOUNTANT =================
        private async Task SendRejectionEmailToAccountant(Records record, string auditComment)
        {
            try
            {
                // Find the accountant who created this record
                if (record.AccountantUserId == 0)
                {
                    _logger.LogWarning($"No accountant assigned to record #{record.RecordsId}");
                    return;
                }

                var accountant = await _context.Users.FindAsync(record.AccountantUserId);
                if (accountant == null || string.IsNullOrEmpty(accountant.Email))
                {
                    _logger.LogWarning($"Accountant not found or no email for record #{record.RecordsId}");
                    return;
                }

                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var recordUrl = $"{baseUrl}/Records/Details/{record.RecordsId}";

                string subject = $"❌ Record #{record.RecordsId} Has Been Rejected by Auditor";

                string body = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #dc3545; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
        .content {{ background-color: #f8f9fa; padding: 20px; border: 1px solid #dee2e6; }}
        .feedback {{ background-color: #fff; padding: 15px; border-left: 4px solid #dc3545; margin: 15px 0; }}
        .details {{ background-color: #e9ecef; padding: 15px; border-radius: 5px; margin-top: 15px; }}
        .button {{ background-color: #6c757d; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; margin-top: 15px; }}
        .footer {{ margin-top: 20px; font-size: 12px; color: #6c757d; text-align: center; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>❌ Record Rejected by Auditor</h2>
        </div>
        <div class='content'>
            <p>Hello <strong>{accountant.Name}</strong>,</p>
            <p>The auditor has <strong style='color: #dc3545;'>rejected</strong> Record <strong>#{record.RecordsId}</strong> that you submitted.</p>
            
            <div class='feedback'>
                <h3 style='margin-top: 0; color: #721c24;'>📝 Auditor's Rejection Reason:</h3>
                <p style='font-size: 16px;'>{auditComment}</p>
            </div>

            <div class='details'>
                <h4 style='margin-top: 0;'>Record Details:</h4>
                <ul style='list-style: none; padding-left: 0;'>
                    <li><strong>Holder:</strong> {record.Holder?.Name}</li>
                    <li><strong>Bank:</strong> {record.UserCard?.Bank}</li>
                    <li><strong>Card Number:</strong> {record.UserCard?.CardNumber}</li>
                    <li><strong>Description:</strong> {record.Description}</li>
                    <li><strong>Statement Date:</strong> {record.StatementDate:dd MMM, yyyy}</li>
                </ul>
            </div>
            
            <p><strong>What happens next?</strong> The record has been sent to board members for their review despite the rejection. You can track its progress in the system.</p>
            
            <div style='text-align: center; margin-top: 20px;'>
                <a href='{recordUrl}' class='button'>👁️ View Rejected Record</a>
            </div>
        </div>
        <div class='footer'>
            <p>This is an automated message from the Credit System.</p>
            <p>Please do not reply directly to this email.</p>
            <p>© {DateTime.Now.Year} Credit System. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

                await _emailService.SendEmailAsync(accountant.Email, subject, body);
                _logger.LogInformation($"Rejection email sent to accountant {accountant.Email} for record #{record.RecordsId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send rejection email to accountant for record #{record.RecordsId}");
            }
        }
        private async Task SendEmailToHolderForCorrection(Records record, string auditComment)
        {
            try
            {
                if (record.Holder == null || string.IsNullOrEmpty(record.Holder.Email))
                {
                    _logger.LogWarning($"Holder email not found for record #{record.RecordsId}");
                    return;
                }

                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                // CHANGED: Point to AddHolderDetails instead of EditRecord
                var holderEditUrl = $"{baseUrl}/Holders/AddHolderDetails?recordId={record.RecordsId}&mode=edit";

                string subject = $"↩️ Action Required: Record #{record.RecordsId} Needs Corrections";

                string body = $@"
            <!DOCTYPE html>
            <html>
            <head>
                <style>
                    body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                    .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                    .header {{ background-color: #ffc107; color: #000; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
                    .content {{ background-color: #f8f9fa; padding: 20px; border: 1px solid #dee2e6; }}
                    .feedback {{ background-color: #fff; padding: 15px; border-left: 4px solid #ffc107; margin: 15px 0; }}
                    .button {{ background-color: #007bff; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; margin-top: 15px; }}
                    .details {{ background-color: #e9ecef; padding: 15px; border-radius: 5px; margin-top: 15px; }}
                    .footer {{ margin-top: 20px; font-size: 12px; color: #6c757d; text-align: center; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h2>↩️ Record Requires Corrections</h2>
                    </div>
                    <div class='content'>
                        <p>Hello <strong>{record.Holder?.Name}</strong>,</p>
                        <p>The auditor has reviewed your submission for <strong>Record #{record.RecordsId}</strong> and identified issues that need to be corrected.</p>
                        
                        <div class='feedback'>
                            <h3 style='margin-top: 0; color: #856404;'>📝 Auditor Feedback:</h3>
                            <p style='font-size: 16px;'>{auditComment}</p>
                        </div>

                        <div class='details'>
                            <h4 style='margin-top: 0;'>Record Details:</h4>
                            <ul style='list-style: none; padding-left: 0;'>
                                <li><strong>Bank:</strong> {record.UserCard?.Bank}</li>
                                <li><strong>Card Number:</strong> {record.UserCard?.CardNumber}</li>
                                <li><strong>Description:</strong> {record.Description}</li>
                                <li><strong>Statement Date:</strong> {record.StatementDate:dd MMM, yyyy}</li>
                            </ul>
                        </div>
                        
                        <p><strong>Please review the auditor's feedback and make the necessary corrections to your submission.</strong></p>
                        
                        <div style='text-align: center;'>
                            <a href='{holderEditUrl}' class='button'>✏️ Click Here to Correct Your Record</a>
                        </div>
                        
                        <p style='margin-top: 20px;'>Once you've made the corrections, your record will be resubmitted for review.</p>
                    </div>
                    <div class='footer'>
                        <p>This is an automated message. Please do not reply directly to this email.</p>
                        <p>If you have any questions, please contact the auditor directly.</p>
                    </div>
                </div>
            </body>
            </html>";

                await _emailService.SendEmailAsync(record.Holder.Email, subject, body);
                _logger.LogInformation($"Correction email sent to holder {record.Holder.Email} for record #{record.RecordsId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send correction email to holder for record #{record.RecordsId}");
            }
        }

        private async Task SendEmailToBoardMembers(Records record, int fieldsCount, string status)
        {
            try
            {
                var boardMembers = await _context.Users
                    .Where(u => u.Role == "BoardMember" || u.Role == "Admin")
                    .ToListAsync();

                if (!boardMembers.Any())
                {
                    _logger.LogWarning("No board members found.");
                    return;
                }

                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                string statusEmoji = status == "Approved" ? "✅" : "❌";
                string subject = $"{statusEmoji} Auditor Review {status} - Record #{record.RecordsId}";

                foreach (var member in boardMembers)
                {
                    if (!string.IsNullOrEmpty(member?.Email))
                    {
                        // Generate unique token for this board member
                        string token = TokenHelper.GenerateAccessToken(record.RecordsId, member.Id, member.Role);
                        string encodedToken = System.Web.HttpUtility.UrlEncode(token);

                        // Create access URL with token
                        var accessUrl = $"{baseUrl}/BoardMember/AccessBoardMemberRecord?token={encodedToken}";

                        string body = $@"
                    <!DOCTYPE html>
                    <html>
                    <head>
                        <style>
                            body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                            .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                            .header {{ background-color: {(status == "Approved" ? "#28a745" : "#dc3545")}; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
                            .content {{ background-color: #f8f9fa; padding: 20px; border: 1px solid #dee2e6; }}
                            .details {{ background-color: #e9ecef; padding: 15px; border-radius: 5px; margin: 15px 0; }}
                            .button {{ background-color: {(status == "Approved" ? "#28a745" : "#dc3545")}; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; }}
                            .token-info {{ background-color: #fff3cd; border: 1px solid #ffeeba; padding: 10px; border-radius: 5px; margin: 15px 0; font-size: 12px; color: #856404; }}
                            .footer {{ margin-top: 20px; font-size: 12px; color: #6c757d; text-align: center; }}
                        </style>
                    </head>
                    <body>
                        <div class='container'>
                            <div class='header'>
                                <h2>{statusEmoji} Auditor Review {status}</h2>
                            </div>
                            <div class='content'>
                                <p>Hello <strong>{member.Name}</strong>,</p>
                                <p>The auditor has completed the review for Record #{record.RecordsId} and it's now ready for your review.</p>
                                
                                <div class='details'>
                                    <h4 style='margin-top: 0;'>Record Details:</h4>
                                    <ul style='list-style: none; padding-left: 0;'>
                                        <li><strong>Holder:</strong> {record.Holder?.Name}</li>
                                        <li><strong>Bank:</strong> {record.UserCard?.Bank}</li>
                                        <li><strong>Card Number:</strong> {record.UserCard?.CardNumber}</li>
                                        <li><strong>Audit Status:</strong> <span style='color: {(status == "Approved" ? "#28a745" : "#dc3545")}; font-weight: bold;'>{status}</span></li>
                                        <li><strong>Audit Comments:</strong> {fieldsCount} review(s) added</li>
                                    </ul>
                                </div>
                                
                                <div class='token-info'>
                                    <strong>🔐 One-Click Access:</strong> Click the button below to access the record directly - no login required!
                                </div>
                                
                                <div style='text-align: center; margin: 25px 0;'>
                                    <a href='{accessUrl}' class='button' style='padding: 14px 28px; font-size: 16px;'>
                                        📋 Click Here to Review (No Login Required)
                                    </a>
                                </div>
                                
                                <p style='color: #666; font-size: 13px;'>
                                    ⚠️ This link is unique to you and will expire in 24 hours. Do not share it with others.
                                </p>
                            </div>
                            <div class='footer'>
                                <p>This is an automated message from the Credit System.</p>
                                <p>© {DateTime.Now.Year} Credit System. All rights reserved.</p>
                            </div>
                        </div>
                    </body>
                    </html>";

                        await _emailService.SendEmailAsync(member.Email, subject, body);
                        _logger.LogInformation($"Email sent to board member {member.Email} for record #{record.RecordsId} with token access");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to board members");
            }
        }
        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int auditorFieldId, string status)
        {
            try
            {
                var auditorField = await _context.AuditorField
                    .FirstOrDefaultAsync(a => a.AuditorFieldId == auditorFieldId);

                if (auditorField == null)
                    return NotFound();

                auditorField.Status = status;
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating auditor field status");
                return Json(new { success = false, error = ex.Message });
            }
        }
    }
}