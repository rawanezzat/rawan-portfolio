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
    public class HoldersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly EmailService _emailService;
        private readonly ILogger<HoldersController> _logger;

        // ================= SINGLE CONSTRUCTOR =================
        public HoldersController(
            ApplicationDbContext context,
            IWebHostEnvironment webHostEnvironment,
            EmailService emailService,
            ILogger<HoldersController> logger)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _emailService = emailService;
            _logger = logger;
        }

        // ================= HOLDER INDEX (VIEW THEIR RECORDS) =================
        // URL: /Holders/Index
        public async Task<IActionResult> Index()
        {
            try
            {
                // Try to get holder ID from session first
                int? holderId = HttpContext.Session.GetInt32("HolderId");

                // If not in session, try to get from claims (if using authentication)
                if (!holderId.HasValue)
                {
                    var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                    if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int authHolderId))
                    {
                        holderId = authHolderId;
                        HttpContext.Session.SetInt32("HolderId", holderId.Value);
                    }
                }

                // If still no holder ID, redirect to login or error page - NOT back to Index
                if (!holderId.HasValue)
                {
                    TempData["ErrorMessage"] = "Please log in to continue.";
                    return RedirectToAction("Login", "Account"); // Or redirect to home
                }

                var holder = await _context.Users.FindAsync(holderId.Value);
                if (holder == null)
                {
                    TempData["ErrorMessage"] = "Holder not found.";
                    return RedirectToAction("Index", "Home"); // Redirect to home, not back to Index
                }

                // Get all records for this holder
                var records = await _context.Records
                    .Include(r => r.UserCard)
                    .Include(r => r.Holder)
                    .Include(r => r.HolderFields)
                    .Where(r => r.HolderId == holderId.Value)
                    .OrderByDescending(r => r.StatementDate)
                    .ToListAsync();

                // Create viewModel list
                var viewModel = new List<HolderDetailsViewModel>();

                foreach (var record in records)
                {
                    var recordVM = new HolderDetailsViewModel
                    {
                        RecordId = record.RecordsId,
                        HolderName = record.Holder?.Name ?? holder.Name,
                        BankName = record.UserCard?.Bank ?? "N/A",
                        CardNumber = record.UserCard?.CardNumber ?? "N/A",
                        Description = record.Description ?? "N/A",
                        StatementDate = record.StatementDate,
                        AccountantComment = record.AccountantComment ?? "---",
                        AccountantFileUrl = record.AccountantFileUrl,
                        CurrentPhase = record.CurrentPhase ?? "Accountant",
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
                        }).ToList() ?? new List<HolderFieldViewModel>()
                    };

                    viewModel.Add(recordVM);
                }

                // Pass statistics via ViewBag
                ViewBag.HolderName = holder.Name;
                ViewBag.HolderEmail = holder.Email;
                ViewBag.HolderId = holderId.Value;
                ViewBag.TotalRecords = viewModel.Count;
                ViewBag.PendingRecords = viewModel.Count(r => r.CurrentPhase == "Accountant" || r.CurrentPhase == "Holder");
                ViewBag.SubmittedRecords = viewModel.Count(r => r.CurrentPhase == "Auditor");
                ViewBag.CompletedRecords = viewModel.Count(r => r.CurrentPhase == "Approved" || r.CurrentPhase == "Completed");

                ViewBag.SuccessMessage = TempData["SuccessMessage"];
                ViewBag.ErrorMessage = TempData["ErrorMessage"];

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading holder dashboard");
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return RedirectToAction("Index", "Home"); // Redirect to home, not back to Index
            }
        }
        // ================= ADD HOLDER DETAILS (GET) =================
        // URL: /Holders/AddHolderDetails?recordId=5
        // ================= ADD HOLDER DETAILS (GET) =================
        // ================= ADD HOLDER DETAILS (GET) =================
        public async Task<IActionResult> AddHolderDetails(int recordId)
        {
            try
            {
                _logger.LogInformation($"AddHolderDetails called for recordId={recordId}");

                // Try to get holder ID from session
                int? holderId = HttpContext.Session.GetInt32("HolderId");
                bool isTokenAccess = TempData["TokenAccess"] != null && (bool)TempData["TokenAccess"];

                // If no session, check if they're authenticated normally
                if (!holderId.HasValue)
                {
                    var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                    if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int authHolderId))
                    {
                        holderId = authHolderId;
                        _logger.LogInformation($"Using authenticated user ID: {authHolderId}");
                    }
                    else
                    {
                        _logger.LogWarning("No holder ID found in session or claims");
                        TempData["ErrorMessage"] = "Please use the link from your email to access this page, or log in.";
                        return RedirectToAction("Login", "Account");
                    }
                }

                var record = await _context.Records
                    .Include(r => r.Holder)
                    .Include(r => r.UserCard)
                    .Include(r => r.AccountantUser)
                    .FirstOrDefaultAsync(r => r.RecordsId == recordId);

                if (record == null)
                {
                    _logger.LogWarning($"Record #{recordId} not found");
                    TempData["ErrorMessage"] = "Record not found.";
                    return RedirectToAction("Index", "Home");
                }

                // Verify the holder matches
                if (record.HolderId != holderId.Value)
                {
                    _logger.LogWarning($"Holder mismatch: Record holder {record.HolderId} vs Session holder {holderId.Value}");
                    TempData["ErrorMessage"] = "You don't have permission to access this record.";
                    return RedirectToAction("Index", "Home");
                }

                // Check for existing fields
                var existingFields = await _context.HolderField
                    .Where(hf => hf.RecordId == recordId)
                    .ToListAsync();

                // Create ViewModel
                var viewModel = new HolderDetailsViewModel
                {
                    RecordId = record.RecordsId,
                    HolderName = record.Holder?.Name ?? "N/A",
                    BankName = record.UserCard?.Bank ?? "N/A",
                    CardNumber = record.UserCard?.CardNumber ?? "N/A",
                    Description = record.Description ?? "N/A",
                    StatementDate = record.StatementDate,
                    AccountantComment = record.AccountantComment ?? "---",
                    AccountantFileUrl = record.AccountantFileUrl,
                    CurrentPhase = record.CurrentPhase ?? "Accountant",
                    CreatedByName = record.AccountantUser?.Name ?? "N/A",
                    CreatedAt = record.CreatedAt,
                    HolderFields = new List<HolderFieldViewModel>()
                };

                if (existingFields.Any())
                {
                    foreach (var field in existingFields)
                    {
                        viewModel.HolderFields.Add(new HolderFieldViewModel
                        {
                            Date = field.Date,
                            Comment = field.Comment,
                            Amount = field.Amount,
                            Currency = field.Currency,
                            CostCenter = field.CostCenter,
                            Expenses = field.Expenses,
                            Invoice = field.Invoice,
                            FileUrl = field.FileUrl
                        });
                    }
                }
                else
                {
                    // Add one empty row
                    viewModel.HolderFields.Add(new HolderFieldViewModel
                    {
                        Date = DateTime.Today,
                        Currency = "EGP"
                    });
                }

                // Pass a flag to the view if this is token-based access
                ViewBag.IsTokenAccess = isTokenAccess;

                // Update phase if needed
                if (record.CurrentPhase == "Accountant")
                {
                    record.CurrentPhase = "Holder";
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Updated record #{recordId} phase to Holder");
                }

                return View("AddHolderDetails", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading AddHolderDetails");
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
                return RedirectToAction("Index", "Home");
            }
        }        // ================= ACCESS RECORD FROM EMAIL (NO LOGIN REQUIRED) =================
        // URL: /Holders/AccessRecord?recordId=5&holderId=3&token=xyz123
        // ================= ACCESS RECORD FROM EMAIL (NO LOGIN REQUIRED) =================
        public async Task<IActionResult> AccessRecord(string token)
        {
            try
            {
                _logger.LogInformation($"AccessRecord called with token: {token}");

                if (string.IsNullOrEmpty(token))
                {
                    TempData["ErrorMessage"] = "Invalid access link.";
                    return RedirectToAction("Index", "Home");
                }

                // Decode and decrypt the token
                string decodedToken = System.Web.HttpUtility.UrlDecode(token);
                byte[] data = Convert.FromBase64String(decodedToken);
                string tokenData = System.Text.Encoding.UTF8.GetString(data);

                // Parse token data
                var parts = tokenData.Split('|');
                if (parts.Length != 3)
                {
                    _logger.LogWarning("Invalid token format");
                    TempData["ErrorMessage"] = "Invalid access link.";
                    return RedirectToAction("Index", "Home");
                }

                int recordId = int.Parse(parts[0]);
                int holderId = int.Parse(parts[1]);
                long expiryTicks = long.Parse(parts[2]);
                DateTime expiryDate = new DateTime(expiryTicks);

                // Check if token has expired
                if (DateTime.Now > expiryDate)
                {
                    _logger.LogWarning($"Expired token for Record #{recordId}, Holder #{holderId}");
                    TempData["ErrorMessage"] = "This access link has expired. Please contact support.";
                    return RedirectToAction("Index", "Home");
                }

                // Verify the record exists and belongs to this holder
                var record = await _context.Records
                    .Include(r => r.Holder)
                    .Include(r => r.UserCard)
                    .Include(r => r.AccountantUser)
                    .FirstOrDefaultAsync(r => r.RecordsId == recordId && r.HolderId == holderId);

                if (record == null)
                {
                    _logger.LogWarning($"Record #{recordId} not found or doesn't belong to holder #{holderId}");
                    TempData["ErrorMessage"] = "Record not found.";
                    return RedirectToAction("Index", "Home");
                }

                // Store in session
                HttpContext.Session.SetInt32("HolderId", holderId);
                HttpContext.Session.SetInt32("RecordId", recordId);
                HttpContext.Session.SetString("AccessToken", token);

                // Set a flag in TempData that this is a token-based access
                TempData["TokenAccess"] = true;
                TempData["SuccessMessage"] = "Access granted! Please fill in your details below.";

                _logger.LogInformation($"Successfully authenticated holder #{holderId} for record #{recordId}");

                return RedirectToAction("AddHolderDetails", new { recordId = recordId });
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Invalid token format");
                TempData["ErrorMessage"] = "Invalid access link.";
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AccessRecord");
                TempData["ErrorMessage"] = "An error occurred. Please try again.";
                return RedirectToAction("Index", "Home");
            }
        }
        // ================= SAVE HOLDER DETAILS (POST) =================
        [HttpPost]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> SaveDetails(HolderDetailsViewModel model)
        {
            try
            {
                // Get the record from database
                var record = await _context.Records
                    .Include(r => r.Holder)
                    .FirstOrDefaultAsync(r => r.RecordsId == model.RecordId);

                if (record == null)
                {
                    TempData["ErrorMessage"] = "Record not found.";
                    return RedirectToAction("Index", "Records"); // Changed to Records Index
                }

                // Store holder ID in session BEFORE any validation or processing
                if (record.HolderId > 0)
                {
                    HttpContext.Session.SetInt32("HolderId", record.HolderId);
                }

                // Remove empty rows
                if (model.HolderFields != null)
                {
                    model.HolderFields = model.HolderFields
                        .Where(f => f.Amount > 0 && !string.IsNullOrWhiteSpace(f.Comment))
                        .ToList();
                }

                // Validate at least one row
                if (model.HolderFields == null || !model.HolderFields.Any())
                {
                    ModelState.AddModelError("", "Please add at least one transaction.");
                    return View("AddHolderDetails", model);
                }

                decimal totalAmount = 0;

                // Process each holder field
                for (int i = 0; i < model.HolderFields.Count; i++)
                {
                    var fieldVM = model.HolderFields[i];

                    // Handle file upload
                    string fileUrl = null;
                    if (fieldVM.File != null && fieldVM.File.Length > 0)
                    {
                        // Validate file size (5MB max)
                        if (fieldVM.File.Length > 5 * 1024 * 1024)
                        {
                            ModelState.AddModelError("", $"File {fieldVM.File.FileName} exceeds 5MB limit.");
                            return View("AddHolderDetails", model);
                        }

                        // Validate file extension
                        string[] allowedExtensions = { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx", ".xls", ".xlsx" };
                        string fileExtension = Path.GetExtension(fieldVM.File.FileName).ToLowerInvariant();

                        if (!allowedExtensions.Contains(fileExtension))
                        {
                            ModelState.AddModelError("", $"File type {fileExtension} is not allowed.");
                            return View("AddHolderDetails", model);
                        }

                        string folder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "holder-files");
                        Directory.CreateDirectory(folder);

                        string fileName = $"{Guid.NewGuid()}_{Path.GetFileName(fieldVM.File.FileName)}";
                        string fullPath = Path.Combine(folder, fileName);

                        using (var stream = new FileStream(fullPath, FileMode.Create))
                        {
                            await fieldVM.File.CopyToAsync(stream);
                        }

                        fileUrl = "/uploads/holder-files/" + fileName;
                    }

                    // Create HolderField entity
                    var holderField = new HolderField
                    {
                        Date = fieldVM.Date,
                        Comment = fieldVM.Comment,
                        Amount = fieldVM.Amount,
                        Currency = fieldVM.Currency,
                        CostCenter = fieldVM.CostCenter,
                        Expenses = fieldVM.Expenses,
                        Invoice = fieldVM.Invoice,
                        FileUrl = fileUrl,
                        RecordId = record.RecordsId,
                        HolderId = record.HolderId
                    };

                    totalAmount += fieldVM.Amount;
                    _context.HolderField.Add(holderField);
                }

                // UPDATE RECORD STATUS TO AUDITOR
                record.CurrentPhase = "Auditor";
                record.StatusTime = DateTime.Now; // <-- ADD THIS LINE

                await _context.SaveChangesAsync();

                // SEND EMAIL TO AUDITORS
                await SendEmailToAuditors(record, model.HolderFields.Count, totalAmount);

                // REDIRECT TO INDEX WITH SUCCESS MESSAGE
                TempData["SuccessMessage"] = "Your details have been submitted successfully! The record has been sent to auditors for review.";

                return RedirectToAction("Index", "Records"); // Changed to Records Index
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving holder details");
                ModelState.AddModelError("", $"Error saving details: {ex.Message}");
                return View("AddHolderDetails", model);
            }
        }
        // ================= SEND EMAIL TO AUDITORS =================
        private async Task SendEmailToAuditors(Records record, int fieldsCount, decimal totalAmount)
        {
            try
            {
                // ALWAYS use auditor ID 4
                int auditorId = 4;

                // Get the specific auditor with ID 4
                var auditor = await _context.Users.FirstOrDefaultAsync(u => u.Id == auditorId);

                if (auditor == null)
                {
                    _logger.LogError($"Auditor with ID {auditorId} not found! Please ensure auditor ID 4 exists in the database.");
                    TempData["ErrorMessage"] = "System error: Default auditor not found. Please contact administrator.";
                    return;
                }

                // Verify the user is actually an auditor
                if (auditor.Role != "Auditor" && auditor.Role != "Admin")
                {
                    _logger.LogError($"User ID {auditorId} exists but is not an auditor. Role: {auditor.Role}");
                    TempData["ErrorMessage"] = "System error: Default user is not an auditor. Please contact administrator.";
                    return;
                }

                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                // Generate token for this specific auditor (ID 4)
                string token = TokenHelper.GenerateAccessToken(record.RecordsId, auditor.Id, auditor.Role);
                string encodedToken = System.Web.HttpUtility.UrlEncode(token);

                // Create access URL with token
                var accessUrl = $"{baseUrl}/Auditor/AccessAuditorRecord?token={encodedToken}";

                string subject = $"⚠️ Action Required: Holder Details Submitted - Record #{record.RecordsId}";

                string body = $@"
            <!DOCTYPE html>
            <html>
            <head>
                <style>
                    body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                    .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                    .header {{ background-color: #dc3545; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
                    .content {{ padding: 30px; background-color: #f9f9f9; }}
                    .button {{ display: inline-block; padding: 14px 28px; background-color: #dc3545; color: white; 
                             text-decoration: none; border-radius: 5px; margin: 20px 0; font-weight: bold; font-size: 16px; }}
                    .summary {{ background-color: white; padding: 20px; border-radius: 5px; border-left: 4px solid #dc3545; }}
                    .token-info {{ background-color: #fff3cd; border: 1px solid #ffeeba; padding: 10px; border-radius: 5px; margin: 15px 0; font-size: 12px; color: #856404; }}
                    .footer {{ text-align: center; padding: 20px; color: #666; font-size: 12px; }}
                    .badge {{ background-color: #ffc107; color: #000; padding: 5px 10px; border-radius: 3px; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h2>⚠️ Holder Details Submitted</h2>
                    </div>
                    <div class='content'>
                        <p>Hello <strong>{auditor.Name}</strong>,</p>
                        <p>The holder has submitted details for <strong>Record #{record.RecordsId}</strong> and it is now ready for your review.</p>
                        
                        <div class='summary'>
                            <h3 style='color: #dc3545; margin-top: 0;'>Submission Summary:</h3>
                            <table style='width: 100%; border-collapse: collapse;'>
                                <tr>
                                    <td style='padding: 8px 0;'><strong>Holder Name:</strong></td>
                                    <td style='padding: 8px 0;'>{record.Holder?.Name}</td>
                                </tr>
                                <tr>
                                    <td style='padding: 8px 0;'><strong>Statement Date:</strong></td>
                                    <td style='padding: 8px 0;'>{record.StatementDate:dd MMMM yyyy}</td>
                                </tr>
                                <tr>
                                    <td style='padding: 8px 0;'><strong>Description:</strong></td>
                                    <td style='padding: 8px 0;'>{record.Description}</td>
                                </tr>
                                <tr>
                                    <td style='padding: 8px 0;'><strong>Number of Transactions:</strong></td>
                                    <td style='padding: 8px 0;'><span class='badge'>{fieldsCount}</span></td>
                                </tr>
                                <tr>
                                    <td style='padding: 8px 0;'><strong>Total Amount:</strong></td>
                                    <td style='padding: 8px 0;'><strong style='color: #28a745;'>{totalAmount:N2} {record.HolderFields?.FirstOrDefault()?.Currency ?? "EGP"}</strong></td>
                                </tr>
                            </table>
                        </div>

                        <div class='token-info'>
                            <strong>🔐 One-Click Access:</strong> Click the button below to access the record directly - no login required!
                        </div>

                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='{accessUrl}' class='button'>
                                🔍 Review Record Now (No Login Required)
                            </a>
                        </div>
                        
                        <p style='color: #666; font-size: 13px;'>
                            ⚠️ This link is unique to you and will expire in 24 hours. Do not share it with others.
                        </p>
                        
                        <p>Please review the submitted details and take appropriate action.</p>
                        
                        <p>Thank you.</p>
                    </div>
                    <div class='footer'>
                        <p>This is an automated message from the Credit System.</p>
                        <p>© {DateTime.Now.Year} Credit System. All rights reserved.</p>
                    </div>
                </div>
            </body>
            </html>
        ";

                // Send email ONLY to auditor ID 4
                await _emailService.SendEmailAsync(auditor.Email, subject, body);

                // Log that we sent to specific auditor
                _logger.LogInformation($"Email sent to auditor: {auditor.Email} (ID: {auditor.Id}) for record #{record.RecordsId}");

                // Store in TempData that this record was assigned to auditor 4
                TempData[$"AssignedAuditor_{record.RecordsId}"] = auditorId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to auditor");
            }
        }
        // ================= SUCCESS PAGE =================
        public IActionResult Success(int recordId)
        {
            ViewBag.RecordId = recordId;
            return View();
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
                    .FirstOrDefaultAsync(r => r.RecordsId == id);

                if (record == null)
                    return NotFound();

                var viewModel = new HolderDetailsViewModel
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
                    }).ToList() ?? new List<HolderFieldViewModel>()
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