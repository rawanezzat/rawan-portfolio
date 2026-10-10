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
    public class BoardMemberController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<BoardMemberController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly EmailService _emailService;

        public BoardMemberController(
            ApplicationDbContext context,
            ILogger<BoardMemberController> logger,
            IWebHostEnvironment webHostEnvironment,
            EmailService emailService)
        {
            _context = context;
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
            _emailService = emailService;
        }

        // ================= BOARD MEMBER DASHBOARD =================
        public async Task<IActionResult> Index()
        {
            try
            {
                var records = await _context.Records
                    .Include(r => r.Holder)
                    .Include(r => r.UserCard)
                    .Include(r => r.HolderFields)
                    .Include(r => r.AuditorApprovals)
                    .Include(r => r.BoardMemberApprovals)
                    .Where(r => r.CurrentPhase == "BoardMember")
                    .OrderByDescending(r => r.StatementDate)
                    .ToListAsync();

                var viewModel = records.Select(r => new BoardMemberRecordViewModel
                {
                    RecordId = r.RecordsId,
                    HolderName = r.Holder?.Name ?? "N/A",
                    BankName = r.UserCard?.Bank ?? "N/A",
                    CardNumber = r.UserCard?.CardNumber ?? "N/A",
                    Description = r.Description ?? "N/A",
                    StatementDate = r.StatementDate,
                    AccountantComment = r.AccountantComment ?? "---",
                    AccountantFileUrl = r.AccountantFileUrl,
                    CurrentPhase = r.CurrentPhase ?? "BoardMember",
                    HolderFieldsCount = r.HolderFields?.Count ?? 0,
                    TotalAmount = r.HolderFields?.Sum(hf => hf.Amount) ?? 0,
                    Currency = r.HolderFields?.FirstOrDefault()?.Currency ?? "EGP",
                    AuditorFieldsCount = r.AuditorApprovals?.Count ?? 0,
                    AuditStatus = r.AuditorApprovals?
                        .OrderByDescending(a => a.CreatedAt)
                        .FirstOrDefault()?.Status ?? "Pending",
                    BoardMemberFieldsCount = r.BoardMemberApprovals?.Count ?? 0,
                    BoardMemberStatus = r.BoardMemberApprovals?
                        .OrderByDescending(b => b.CreatedAt)
                        .FirstOrDefault()?.Status ?? "Pending"
                }).ToList();

                ViewBag.TotalPending = viewModel.Count;
                ViewBag.SuccessMessage = TempData["SuccessMessage"];
                ViewBag.ErrorMessage = TempData["ErrorMessage"];

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading board member dashboard");
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return RedirectToAction("Index", "Home");
            }
        }


        // ================= ACCESS BOARD MEMBER RECORD FROM EMAIL (NO LOGIN REQUIRED) =================
        // ================= ACCESS BOARD MEMBER RECORD FROM EMAIL (NO LOGIN REQUIRED) =================
        public async Task<IActionResult> AccessBoardMemberRecord(string token)
        {
            try
            {
                _logger.LogInformation($"AccessBoardMemberRecord called with token");

                if (string.IsNullOrEmpty(token))
                {
                    TempData["ErrorMessage"] = "Invalid access link.";
                    return RedirectToAction("Index", "Home");
                }

                // Parse the token
                if (!TokenHelper.TryParseToken(token, out int recordId, out int tokenBoardMemberId, out string role, out DateTime expiry))
                {
                    _logger.LogWarning("Invalid token format");
                    TempData["ErrorMessage"] = "Invalid access link.";
                    return RedirectToAction("Index", "Home");
                }

                // FORCE BOARD MEMBER ID TO BE 5
                int forcedBoardMemberId = 5;

                _logger.LogInformation($"Token contained BoardMemberId: {tokenBoardMemberId}, but forcing to: {forcedBoardMemberId}");

                // Check if token has expired
                if (DateTime.Now > expiry)
                {
                    _logger.LogWarning($"Expired token for Record #{recordId}");
                    TempData["ErrorMessage"] = "This access link has expired. Please contact support.";
                    return RedirectToAction("Index", "Home");
                }

                // Verify the forced board member exists
                var boardMember = await _context.Users.FindAsync(forcedBoardMemberId);
                if (boardMember == null)
                {
                    _logger.LogError($"Forced board member ID {forcedBoardMemberId} not found!");
                    TempData["ErrorMessage"] = "System error: Default board member not found.";
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

                // CRITICAL: Store the FORCED board member ID (5) in session
                HttpContext.Session.SetInt32("BoardMemberId", forcedBoardMemberId);
                HttpContext.Session.SetInt32("RecordId", recordId);
                HttpContext.Session.SetInt32("HolderId", record.HolderId);
                HttpContext.Session.SetString("AccessToken", token);
                HttpContext.Session.SetString("UserRole", "BoardMember");

                // Verify session was set
                int? checkBoardMemberId = HttpContext.Session.GetInt32("BoardMemberId");
                _logger.LogInformation($"Session set - Forced BoardMemberId: {checkBoardMemberId} (should be 5)");

                TempData["TokenAccess"] = true;
                //TempData["SuccessMessage"] = "Access granted! Please complete your board member review.";

                return RedirectToAction("AddBoardMemberDetails", new { recordId = recordId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AccessBoardMemberRecord");
                TempData["ErrorMessage"] = "An error occurred. Please try again.";
                return RedirectToAction("Index", "Home");
            }
        }

        // ================= ADD BOARD MEMBER DETAILS (GET) =================
        // ================= ADD BOARD MEMBER DETAILS (GET) =================
        public async Task<IActionResult> AddBoardMemberDetails(int recordId)
        {
            try
            {
                _logger.LogInformation($"AddBoardMemberDetails called for recordId={recordId}");

                // Try to get board member ID from session (set by AccessBoardMemberRecord)
                int? boardMemberId = HttpContext.Session.GetInt32("BoardMemberId");
                string userRole = HttpContext.Session.GetString("UserRole");
                bool isTokenAccess = TempData["TokenAccess"] != null && (bool)TempData["TokenAccess"];

                // If no session, check if they're authenticated normally
                if (!boardMemberId.HasValue)
                {
                    var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                    if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int authBoardMemberId))
                    {
                        boardMemberId = authBoardMemberId;
                        userRole = "Authenticated";
                        _logger.LogInformation($"Using authenticated user ID: {authBoardMemberId}");
                    }
                    else
                    {
                        _logger.LogWarning("No board member ID found in session or claims");
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
                    .Include(r => r.BoardMemberApprovals)
                        .ThenInclude(b => b.BoardMemberUser)
                    .FirstOrDefaultAsync(r => r.RecordsId == recordId);

                if (record == null)
                {
                    TempData["ErrorMessage"] = "Record not found.";
                    return RedirectToAction("Index");
                }

                // Store holder ID in session for later use in Save method
                HttpContext.Session.SetInt32("HolderId", record.HolderId);

                // Verify the record is in the correct phase
                if (record.CurrentPhase != "BoardMember" && record.CurrentPhase != "Completed")
                {
                    _logger.LogWarning($"Record #{recordId} is in phase {record.CurrentPhase}, not accessible for board member review");
                    //TempData["ErrorMessage"] = "This record is not available for board member review at this time.";
                    return RedirectToAction("Index", "Records");
                }

                var viewModel = new BoardMemberDetailsViewModel
                {
                    RecordId = record.RecordsId,
                    HolderName = record.Holder?.Name ?? "N/A",
                    BankName = record.UserCard?.Bank ?? "N/A",
                    CardNumber = record.UserCard?.CardNumber ?? "N/A",
                    Description = record.Description ?? "N/A",
                    StatementDate = record.StatementDate,
                    AccountantComment = record.AccountantComment ?? "---",
                    AccountantFileUrl = record.AccountantFileUrl,
                    CurrentPhase = record.CurrentPhase ?? "BoardMember",
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
                    ExistingBoardMemberFields = record.BoardMemberApprovals?.Select(bf => new BoardMemberFieldViewModel
                    {
                        BoardMemberFieldId = bf.BoardMemberFieldId,
                        Comment = bf.Comment,
                        BoardMemberFileUrl = bf.BoardMemberFileUrl,
                        Status = bf.Status,
                        CreatedAt = bf.CreatedAt,
                    }).ToList() ?? new List<BoardMemberFieldViewModel>(),
                    BoardMemberFields = new List<BoardMemberFieldViewModel>
            {
                new BoardMemberFieldViewModel { Status = "Pending" }
            }
                };

                ViewBag.IsTokenAccess = isTokenAccess;
                ViewBag.UserRole = userRole;

                return View("AddBoardMemberDetails", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading AddBoardMemberDetails");
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return RedirectToAction("Index", "Records");
            }
        }        // ================= SAVE BOARD MEMBER DETAILS (POST) =================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveBoardMemberDetails(BoardMemberDetailsViewModel model)
        {
            try
            {
                // CRITICAL: Remove ALL display-only properties from validation
                var propertiesToRemove = new[]
                {
            "HolderName", "BankName", "CardNumber", "Description",
            "AccountantComment", "AccountantFileUrl", "CurrentPhase",
            "HolderFields", "ExistingAuditorFields", "ExistingBoardMemberFields"
        };

                foreach (var prop in propertiesToRemove)
                {
                    if (ModelState.ContainsKey(prop))
                        ModelState.Remove(prop);
                }

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
                var firstField = model.BoardMemberFields?.FirstOrDefault();
                if (firstField == null || string.IsNullOrEmpty(firstField.Status))
                {
                    ModelState.AddModelError("BoardMemberFields[0].Status", "Please select a status.");
                    return await ReloadBoardMemberView(model.RecordId);
                }

                // Validate that comment is provided
                if (string.IsNullOrWhiteSpace(firstField.Comment))
                {
                    ModelState.AddModelError("BoardMemberFields[0].Comment", "Please enter a board member comment.");
                    return await ReloadBoardMemberView(model.RecordId);
                }

                // Filter out empty comments
                var validFields = model.BoardMemberFields?
                    .Where(f => !string.IsNullOrWhiteSpace(f.Comment))
                    .ToList() ?? new List<BoardMemberFieldViewModel>();

                if (!validFields.Any())
                {
                    ModelState.AddModelError("", "Please add at least one board member comment.");
                    return await ReloadBoardMemberView(model.RecordId);
                }

                // Get the selected status
                string selectedStatus = firstField.Status;

                // ========== FORCE BOARD MEMBER ID TO BE 5 ==========
                int forcedBoardMemberId = 5;

                // Get holder ID from session or record
                int? holderId = HttpContext.Session.GetInt32("HolderId") ?? record.HolderId;

                _logger.LogInformation($"SaveBoardMemberDetails - FORCING BoardMemberUserId: {forcedBoardMemberId}, HolderId: {holderId} for Record #{record.RecordsId}");

                // Process each board member field
                foreach (var fieldVM in validFields)
                {
                    string fileUrl = null;

                    // Handle file upload
                    if (fieldVM.BoardMemberFile != null && fieldVM.BoardMemberFile.Length > 0)
                    {
                        // Validate file size (10MB max)
                        if (fieldVM.BoardMemberFile.Length > 10 * 1024 * 1024)
                        {
                            ModelState.AddModelError("", $"File {fieldVM.BoardMemberFile.FileName} exceeds 10MB limit.");
                            return await ReloadBoardMemberView(model.RecordId);
                        }

                        // Validate file extension
                        string[] allowedExtensions = { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx", ".xls", ".xlsx" };
                        string fileExtension = Path.GetExtension(fieldVM.BoardMemberFile.FileName).ToLowerInvariant();

                        if (!allowedExtensions.Contains(fileExtension))
                        {
                            ModelState.AddModelError("", $"File type {fileExtension} is not allowed.");
                            return await ReloadBoardMemberView(model.RecordId);
                        }

                        string folder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "board-member-files");
                        Directory.CreateDirectory(folder);

                        string fileName = $"{Guid.NewGuid()}_{Path.GetFileName(fieldVM.BoardMemberFile.FileName)}";
                        string fullPath = Path.Combine(folder, fileName);

                        using (var stream = new FileStream(fullPath, FileMode.Create))
                        {
                            await fieldVM.BoardMemberFile.CopyToAsync(stream);
                        }

                        fileUrl = "/uploads/board-member-files/" + fileName;
                    }

                    var boardMemberField = new BoardMemberField
                    {
                        RecordId = record.RecordsId,
                        BoardMemberUserId = forcedBoardMemberId, // ALWAYS 5
                        Comment = fieldVM.Comment,
                        BoardMemberFileUrl = fileUrl,
                        Status = selectedStatus,
                        CreatedAt = DateTime.Now
                    };

                    _context.BoardMemberField.Add(boardMemberField);
                    _logger.LogInformation($"Added BoardMemberField for Record #{record.RecordsId} by FORCED BoardMember #{forcedBoardMemberId} (Holder #{holderId} created the record)");
                }

                // Determine next phase and send appropriate emails based on status
                if (selectedStatus == "Redirect")
                {
                    // Send back to auditor for corrections
                    record.CurrentPhase = "Auditor";
                    record.StatusTime = DateTime.Now;

                    // Send email to auditor ONLY
                    await SendEmailToAuditorForCorrection(record, firstField.Comment);

                    TempData["SuccessMessage"] = "Record redirected to auditor for corrections. An email notification has been sent.";
                }
                else if (selectedStatus == "Approved" || selectedStatus == "Rejected")
                {
                    // Update record status to Completed
                    record.CurrentPhase = "Completed";
                    record.StatusTime = DateTime.Now;

                    // Send appropriate emails based on status
                    if (selectedStatus == "Approved")
                    {
                        await SendFinalDecisionEmails(record, selectedStatus);
                        TempData["SuccessMessage"] = "Board member review approved! Record has been finalized and all parties have been notified.";
                    }
                    else // Rejected
                    {
                        // Send rejection email to accountant ONLY
                        await SendBoardMemberRejectionEmailToAccountant(record, firstField.Comment);

                        _logger.LogInformation($"Board member review rejected for record #{record.RecordsId} by FORCED BoardMember #{forcedBoardMemberId}");
                        TempData["SuccessMessage"] = "Board member review rejected. Accountant has been notified.";
                    }
                }
                else // Pending
                {
                    // Keep in board member phase
                    record.CurrentPhase = "BoardMember";
                    record.StatusTime = DateTime.Now;

                    TempData["SuccessMessage"] = "Review saved as pending. You can continue later.";
                }

                await _context.SaveChangesAsync();

                return RedirectToAction("Index", "Records");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error saving board member details");
                var innerException = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", $"Database Error: {innerException}");
                return await ReloadBoardMemberView(model?.RecordId ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving board member details");
                ModelState.AddModelError("", $"Error saving details: {ex.Message}");
                return await ReloadBoardMemberView(model?.RecordId ?? 0);
            }
        }

        // ================= SEND EMAIL TO AUDITOR FOR CORRECTION =================
        private async Task SendEmailToAuditorForCorrection(Records record, string boardMemberComment)
        {
            try
            {
                // Get all auditors
                var auditors = await _context.Users
                    .Where(u => u.Role == "Auditor" || u.Role == "Admin")
                    .ToListAsync();

                if (!auditors.Any())
                {
                    _logger.LogWarning($"No auditors found for record #{record.RecordsId}");
                    return;
                }

                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                string subject = $"↩️ Action Required: Record #{record.RecordsId} Needs Corrections - Board Member Feedback";

                foreach (var auditor in auditors)
                {
                    if (!string.IsNullOrEmpty(auditor?.Email))
                    {
                        // Generate unique token for this auditor
                        string token = TokenHelper.GenerateAccessToken(record.RecordsId, auditor.Id, auditor.Role);
                        string encodedToken = System.Web.HttpUtility.UrlEncode(token);

                        // Create access URL with token
                        var accessUrl = $"{baseUrl}/Auditor/AccessAuditorRecord?token={encodedToken}";

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
                            .details {{ background-color: #e9ecef; padding: 15px; border-radius: 5px; margin: 15px 0; }}
                            .token-info {{ background-color: #fff3cd; border: 1px solid #ffeeba; padding: 10px; border-radius: 5px; margin: 15px 0; font-size: 12px; color: #856404; }}
                            .footer {{ margin-top: 20px; font-size: 12px; color: #6c757d; text-align: center; }}
                        </style>
                    </head>
                    <body>
                        <div class='container'>
                            <div class='header'>
                                <h2>↩️ Record Requires Corrections - Board Member Feedback</h2>
                            </div>
                            <div class='content'>
                                <p>Hello <strong>{auditor.Name}</strong>,</p>
                                <p>The board member has reviewed the audit for <strong>Record #{record.RecordsId}</strong> and identified issues that need to be corrected.</p>
                                
                                <div class='feedback'>
                                    <h3 style='margin-top: 0; color: #856404;'>📝 Board Member Feedback:</h3>
                                    <p style='font-size: 16px;'>{boardMemberComment}</p>
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
                                
                                <div class='token-info'>
                                    <strong>🔐 One-Click Access:</strong> Click the button below to access the record directly - no login required!
                                </div>
                                
                                <div style='text-align: center; margin: 25px 0;'>
                                    <a href='{accessUrl}' class='button' style='padding: 14px 28px; font-size: 16px; background-color: #ffc107; color: #000;'>
                                        ✏️ Click Here to Correct Your Audit (No Login Required)
                                    </a>
                                </div>
                                
                                <p><strong>Please review the board member's feedback and make the necessary corrections to your audit.</strong></p>
                                
                                <p style='margin-top: 20px;'>Once you've made the corrections, the record will be resubmitted for board member review.</p>
                                
                                <p style='color: #666; font-size: 13px; margin-top: 15px;'>
                                    ⚠️ This link is unique to you and will expire in 24 hours. Do not share it with others.
                                </p>
                            </div>
                            <div class='footer'>
                                <p>This is an automated message from the Credit System.</p>
                                <p>Please do not reply directly to this email.</p>
                                <p>© {DateTime.Now.Year} Credit System. All rights reserved.</p>
                            </div>
                        </div>
                    </body>
                    </html>";

                        await _emailService.SendEmailAsync(auditor.Email, subject, body);
                        _logger.LogInformation($"Correction email sent to auditor {auditor.Email} for record #{record.RecordsId} with token access");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send correction email to auditor for record #{record.RecordsId}");
            }
        }
        // ================= SEND FINAL DECISION EMAILS =================
        private async Task SendFinalDecisionEmails(Records record, string status)
        {
            try
            {
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var recordUrl = $"{baseUrl}/Records/Details/{record.RecordsId}";

                string statusEmoji = status == "Approved" ? "✅" : "❌";
                string statusColor = status == "Approved" ? "#28a745" : "#dc3545";

                // Send email to holder
                if (record.Holder != null && !string.IsNullOrEmpty(record.Holder.Email))
                {
                    string holderSubject = $"{statusEmoji} Record #{record.RecordsId} - {status} by Board Member";
                    string holderBody = $@"
                        <!DOCTYPE html>
                        <html>
                        <head>
                            <style>
                                body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                                .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                                .header {{ background-color: {statusColor}; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
                                .content {{ background-color: #f8f9fa; padding: 20px; border: 1px solid #dee2e6; }}
                                .button {{ background-color: {statusColor}; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; margin-top: 15px; }}
                                .footer {{ margin-top: 20px; font-size: 12px; color: #6c757d; text-align: center; }}
                            </style>
                        </head>
                        <body>
                            <div class='container'>
                                <div class='header'>
                                    <h2>{statusEmoji} Record {status}</h2>
                                </div>
                                <div class='content'>
                                    <p>Hello <strong>{record.Holder.Name}</strong>,</p>
                                    <p>Your record <strong>#{record.RecordsId}</strong> has been reviewed by the board member and is now <span style='color: {statusColor}; font-weight: bold;'>{status}</span>.</p>
                                    <p>The entire review process is now complete.</p>
                                    
                                    <div style='text-align: center;'>
                                        <a href='{recordUrl}' class='button'>View Final Record</a>
                                    </div>
                                </div>
                                <div class='footer'>
                                    <p>This is an automated message from the Credit System.</p>
                                    <p>© {DateTime.Now.Year} Credit System. All rights reserved.</p>
                                </div>
                            </div>
                        </body>
                        </html>";

                    await _emailService.SendEmailAsync(record.Holder.Email, holderSubject, holderBody);
                }

                // Send email to accountant
                if (record.AccountantUserId > 0)
                {
                    var accountant = await _context.Users.FindAsync(record.AccountantUserId);
                    if (accountant != null && !string.IsNullOrEmpty(accountant.Email))
                    {
                        string accountantSubject = $"{statusEmoji} Record #{record.RecordsId} - Board Member {status}";
                        string accountantBody = $@"
                            <!DOCTYPE html>
                            <html>
                            <head>
                                <style>
                                    body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                                    .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                                    .header {{ background-color: {statusColor}; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
                                    .content {{ background-color: #f8f9fa; padding: 20px; border: 1px solid #dee2e6; }}
                                    .button {{ background-color: {statusColor}; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; margin-top: 15px; }}
                                    .footer {{ margin-top: 20px; font-size: 12px; color: #6c757d; text-align: center; }}
                                </style>
                            </head>
                            <body>
                                <div class='container'>
                                    <div class='header'>
                                        <h2>{statusEmoji} Board Member Review {status}</h2>
                                    </div>
                                    <div class='content'>
                                        <p>Hello <strong>{accountant.Name}</strong>,</p>
                                        <p>Record <strong>#{record.RecordsId}</strong> has been <span style='color: {statusColor}; font-weight: bold;'>{status}</span> by the board member.</p>
                                        <p>The full review cycle is now complete.</p>
                                        
                                        <div style='text-align: center;'>
                                            <a href='{recordUrl}' class='button'>View Final Record</a>
                                        </div>
                                    </div>
                                    <div class='footer'>
                                        <p>This is an automated message from the Credit System.</p>
                                        <p>© {DateTime.Now.Year} Credit System. All rights reserved.</p>
                                    </div>
                                </div>
                            </body>
                            </html>";

                        await _emailService.SendEmailAsync(accountant.Email, accountantSubject, accountantBody);
                    }
                }

                // Send email to auditor (if any)
                var auditorFields = await _context.AuditorField
                    .Where(a => a.RecordId == record.RecordsId)
                    .Select(a => a.AuditorUserId)
                    .Distinct()
                    .ToListAsync();

                foreach (var auditorId in auditorFields)
                {
                    var auditor = await _context.Users.FindAsync(auditorId);
                    if (auditor != null && !string.IsNullOrEmpty(auditor.Email))
                    {
                        string auditorSubject = $"{statusEmoji} Record #{record.RecordsId} - Board Member {status}";
                        string auditorBody = $@"
                            <!DOCTYPE html>
                            <html>
                            <head>
                                <style>
                                    body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                                    .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                                    .header {{ background-color: {statusColor}; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
                                    .content {{ background-color: #f8f9fa; padding: 20px; border: 1px solid #dee2e6; }}
                                    .button {{ background-color: {statusColor}; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block; margin-top: 15px; }}
                                    .footer {{ margin-top: 20px; font-size: 12px; color: #6c757d; text-align: center; }}
                                </style>
                            </head>
                            <body>
                                <div class='container'>
                                    <div class='header'>
                                        <h2>{statusEmoji} Board Member Review {status}</h2>
                                    </div>
                                    <div class='content'>
                                        <p>Hello <strong>{auditor.Name}</strong>,</p>
                                        <p>The board member has <span style='color: {statusColor}; font-weight: bold;'>{status}</span> the audit for Record #{record.RecordsId}.</p>
                                        
                                        <div style='text-align: center;'>
                                            <a href='{recordUrl}' class='button'>View Final Record</a>
                                        </div>
                                    </div>
                                    <div class='footer'>
                                        <p>This is an automated message from the Credit System.</p>
                                        <p>© {DateTime.Now.Year} Credit System. All rights reserved.</p>
                                    </div>
                                </div>
                            </body>
                            </html>";

                        await _emailService.SendEmailAsync(auditor.Email, auditorSubject, auditorBody);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send final decision emails");
            }
        }

        // ================= HELPER METHOD TO RELOAD VIEW =================
        private async Task<IActionResult> ReloadBoardMemberView(int recordId)
        {
            var record = await _context.Records
                .Include(r => r.Holder)
                .Include(r => r.UserCard)
                .Include(r => r.HolderFields)
                .Include(r => r.AuditorApprovals)
                    .ThenInclude(a => a.AuditorUser)
                .Include(r => r.BoardMemberApprovals)
                    .ThenInclude(b => b.BoardMemberUser)
                .FirstOrDefaultAsync(r => r.RecordsId == recordId);

            if (record == null)
            {
                return RedirectToAction("Index", "Records");
            }

            var viewModel = new BoardMemberDetailsViewModel
            {
                RecordId = record.RecordsId,
                HolderName = record.Holder?.Name ?? "N/A",
                BankName = record.UserCard?.Bank ?? "N/A",
                CardNumber = record.UserCard?.CardNumber ?? "N/A",
                Description = record.Description ?? "N/A",
                StatementDate = record.StatementDate,
                AccountantComment = record.AccountantComment ?? "---",
                AccountantFileUrl = record.AccountantFileUrl,
                CurrentPhase = record.CurrentPhase ?? "BoardMember",
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
                ExistingBoardMemberFields = record.BoardMemberApprovals?.Select(bf => new BoardMemberFieldViewModel
                {
                    BoardMemberFieldId = bf.BoardMemberFieldId,
                    Comment = bf.Comment,
                    BoardMemberFileUrl = bf.BoardMemberFileUrl,
                    Status = bf.Status,
                    CreatedAt = bf.CreatedAt
                }).ToList() ?? new List<BoardMemberFieldViewModel>(),
                BoardMemberFields = new List<BoardMemberFieldViewModel>
                {
                    new BoardMemberFieldViewModel { Status = "" } // Empty string as default
                }
            };

            return View("AddBoardMemberDetails", viewModel);
        }

        // ================= GET CURRENT USER ID =================
        // ================= GET CURRENT USER ID =================
        private async Task<int> GetCurrentUserId()
        {
            // ALWAYS return board member ID 5
            int forcedBoardMemberId = 5;

            // Verify that board member 5 exists
            var boardMember = await _context.Users.FindAsync(forcedBoardMemberId);
            if (boardMember == null)
            {
                _logger.LogError($"Forced board member ID {forcedBoardMemberId} not found in database!");
                throw new Exception($"Board member with ID {forcedBoardMemberId} does not exist. Please run seed to create default board member.");
            }

            _logger.LogInformation($"GetCurrentUserId: Forcing return of board member ID: {forcedBoardMemberId}");
            return forcedBoardMemberId;
        }



        // ================= SEND BOARD MEMBER REJECTION EMAIL TO ACCOUNTANT =================
        // ================= SEND BOARD MEMBER REJECTION EMAIL TO ACCOUNTANT =================
        private async Task SendBoardMemberRejectionEmailToAccountant(Records record, string boardMemberComment)
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

                string subject = $"❌ Record #{record.RecordsId} Has Been Rejected by Board Member";

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
            <h2>❌ Record Rejected by Board Member</h2>
        </div>
        <div class='content'>
            <p>Hello <strong>{accountant.Name}</strong>,</p>
            <p>The board member has <strong style='color: #dc3545;'>rejected</strong> Record <strong>#{record.RecordsId}</strong> that you submitted.</p>
            
            <div class='feedback'>
                <h3 style='margin-top: 0; color: #721c24;'>📝 Board Member's Rejection Reason:</h3>
                <p style='font-size: 16px;'>{boardMemberComment}</p>
            </div>

            <div class='details'>
                <h4 style='margin-top: 0;'>Record Details:</h4>
                <ul style='list-style: none; padding-left: 0;'>
                    <li><strong>Holder:</strong> {record.Holder?.Name}</li>
                    <li><strong>Bank:</strong> {record.UserCard?.Bank}</li>
                    <li><strong>Card Number:</strong> {record.UserCard?.CardNumber}</li>
                    <li><strong>Description:</strong> {record.Description}</li>
                    <li><strong>Statement Date:</strong> {record.StatementDate:dd MMM, yyyy}</li>
                    <li><strong>Audit Status:</strong> {record.AuditorApprovals?.OrderByDescending(a => a.CreatedAt).FirstOrDefault()?.Status ?? "N/A"}</li>
                </ul>
            </div>
            
            <p><strong>What happens next?</strong> The record has been rejected and the review process is now complete. The final decision has been recorded in the system.</p>
            
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
                _logger.LogInformation($"Board member rejection email sent to accountant {accountant.Email} for record #{record.RecordsId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send board member rejection email to accountant for record #{record.RecordsId}");
            }
        }


        // ================= VIEW RECORD DETAILS =================
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
                    .Include(r => r.BoardMemberApprovals)
                        .ThenInclude(b => b.BoardMemberUser)
                    .FirstOrDefaultAsync(r => r.RecordsId == id);

                if (record == null)
                    return NotFound();

                var viewModel = new BoardMemberDetailsViewModel
                {
                    RecordId = record.RecordsId,
                    HolderName = record.Holder?.Name ?? "N/A",
                    BankName = record.UserCard?.Bank ?? "N/A",
                    CardNumber = record.UserCard?.CardNumber ?? "N/A",
                    Description = record.Description ?? "N/A",
                    StatementDate = record.StatementDate,
                    AccountantComment = record.AccountantComment ?? "---",
                    AccountantFileUrl = record.AccountantFileUrl,
                    CurrentPhase = record.CurrentPhase ?? "N/A",
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
                    ExistingBoardMemberFields = record.BoardMemberApprovals?.Select(bf => new BoardMemberFieldViewModel
                    {
                        BoardMemberFieldId = bf.BoardMemberFieldId,
                        Comment = bf.Comment,
                        BoardMemberFileUrl = bf.BoardMemberFileUrl,
                        Status = bf.Status,
                        CreatedAt = bf.CreatedAt
                    }).ToList() ?? new List<BoardMemberFieldViewModel>()
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
    }
}