using Credit2.Core.Models;
using Credit2.Core.ViewModels;
using Credit2.Data;
using Credit2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Credit2.Controllers
{
    public class RecordsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IConfiguration _config;
        private readonly ILogger<RecordsController> _logger;

        public RecordsController(
            ApplicationDbContext context,
            IWebHostEnvironment webHostEnvironment,
            IConfiguration config,
            ILogger<RecordsController> logger) // Add logger
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _config = config;
            _logger = logger;
        }

        public IActionResult Index()
        {
            try
            {
                int currentUserId;
                try
                {
                    currentUserId = GetCurrentUserId();
                }
                catch (UnauthorizedAccessException)
                {
                    TempData["ErrorMessage"] = "Please log in to continue.";
                    return RedirectToPage("/Account/Login", new { area = "Identity" });
                }

                var currentUser = _context.Users.Find(currentUserId);
                if (currentUser == null)
                {
                    return RedirectToPage("/Account/Login", new { area = "Identity" });
                }

                // Get the user's roles
                var userRoles = _context.UserRoles
                    .Where(ur => ur.UserId == currentUserId)
                    .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                    .ToList();

                IQueryable<Records> recordsQuery = _context.Records
                    .Include(r => r.Holder)
                    .Include(r => r.UserCard)
                    .Include(r => r.CreatedByUser)
                    .Include(r => r.AccountantUser)
                    .Include(r => r.AuditorApprovals)
                        .ThenInclude(a => a.AuditorUser)
                    .Include(r => r.BoardMemberApprovals)
                        .ThenInclude(b => b.BoardMemberUser);

                // Apply role-based filtering
                if (userRoles.Contains("Admin"))
                {
                    // Admin can see ALL records
                    // No filtering needed
                }
                else if (userRoles.Contains("Accountant"))
                {
                    // Accountant can see records they created only
                    recordsQuery = recordsQuery.Where(r => r.CreatedByUserId == currentUserId);
                }
                else if (userRoles.Contains("Holder"))
                {
                    // Holder can see their own records only
                    recordsQuery = recordsQuery.Where(r => r.HolderId == currentUserId);
                }
                else if (userRoles.Contains("Auditor"))
                {
                    // Auditors see:
                    // 1. Records in Auditor phase (pending approval)
                    // 2. Records they have approved (for history/reference)
                    recordsQuery = recordsQuery.Where(r =>
                        r.CurrentPhase == "Auditor" ||
                        r.AuditorApprovals.Any(a => a.AuditorUserId == currentUserId));
                }
                else if (userRoles.Contains("BoardMember"))
                {
                    // Board members see:
                    // 1. Records in BoardMember phase (pending approval)
                    // 2. Records they have approved (for history/reference)
                    recordsQuery = recordsQuery.Where(r =>
                        r.CurrentPhase == "BoardMember" ||
                        r.BoardMemberApprovals.Any(b => b.BoardMemberUserId == currentUserId));
                }
                else
                {
                    // If no matching role, return empty list
                    recordsQuery = recordsQuery.Where(r => false);
                }

                var records = recordsQuery
                    .OrderByDescending(r => r.CreatedAt)
                    .ToList();



                // Log for debugging
                _logger.LogInformation($"User {currentUserId} with roles {string.Join(", ", userRoles)} found {records.Count} records");

                var viewModel = records.Select(r => new HolderDetailsViewModel
                {
                    RecordId = r.RecordsId,
                    HolderName = r.Holder?.Name ?? "N/A",
                    BankName = r.UserCard?.Bank ?? "N/A",
                    CardNumber = r.UserCard?.CardNumber ?? "N/A",
                    Description = r.Description ?? "N/A",
                    StatementDate = r.StatementDate,
                    AccountantComment = r.AccountantComment ?? "---",
                    AccountantFileUrl = r.AccountantFileUrl,
                    CurrentPhase = r.CurrentPhase ?? "Accountant",
                    CreatedByName = r.CreatedByUser?.Name ?? "N/A",
                    CreatedAt = r.CreatedAt,

                    // Add auditor information if exists
                    AuditorComment = r.AuditorApprovals?.FirstOrDefault(a => a.AuditorUserId == currentUserId)?.Comment ??
                                    r.AuditorApprovals?.FirstOrDefault()?.Comment ?? null,
                    AuditorFileUrl = r.AuditorApprovals?.FirstOrDefault(a => a.AuditorUserId == currentUserId)?.AuditFileUrl ??
                                    r.AuditorApprovals?.FirstOrDefault()?.AuditFileUrl ?? null,
                    AuditorName = r.AuditorApprovals?.FirstOrDefault(a => a.AuditorUserId == currentUserId)?.AuditorUser?.Name ??
                                 r.AuditorApprovals?.FirstOrDefault()?.AuditorUser?.Name ?? null,
                    AuditDate = r.AuditorApprovals?.FirstOrDefault(a => a.AuditorUserId == currentUserId)?.CreatedAt ??
                               r.AuditorApprovals?.FirstOrDefault()?.CreatedAt ?? null,

                    // Initialize empty collections
                    HolderFields = new List<HolderFieldViewModel>()
                }).ToList();

                // Pass user role to view
                ViewBag.UserRoles = userRoles;
                ViewBag.CurrentUserId = currentUserId;
                ViewBag.CurrentUserName = currentUser?.Name;

                // Pass approval status for each record
                ViewBag.HasAuditorApproved = records.ToDictionary(
                    r => r.RecordsId,
                    r => r.AuditorApprovals?.Any(a => a.AuditorUserId == currentUserId && !string.IsNullOrEmpty(a.Comment)) ?? false
                );

                ViewBag.HasBoardMemberApproved = records.ToDictionary(
                    r => r.RecordsId,
                    r => r.BoardMemberApprovals?.Any(b => b.BoardMemberUserId == currentUserId && !string.IsNullOrEmpty(b.Comment)) ?? false
                );

                // Pass if user is assigned to this record
                ViewBag.IsAssignedToAuditor = records.ToDictionary(
                    r => r.RecordsId,
                    r => r.AuditorApprovals?.Any(a => a.AuditorUserId == currentUserId) ?? false
                );

                ViewBag.IsAssignedToBoardMember = records.ToDictionary(
                    r => r.RecordsId,
                    r => r.BoardMemberApprovals?.Any(b => b.BoardMemberUserId == currentUserId) ?? false
                );

                // Pass approval counts
                ViewBag.AuditorApprovalCounts = records.ToDictionary(
                    r => r.RecordsId,
                    r => r.AuditorApprovals?.Count ?? 0
                );

                ViewBag.BoardMemberApprovalCounts = records.ToDictionary(
                    r => r.RecordsId,
                    r => r.BoardMemberApprovals?.Count ?? 0
                );

                return View("Index", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Records Index");
                TempData["ErrorMessage"] = "An error occurred while loading records.";
                return RedirectToAction("Index", "Home");
            }
        }
        private bool UserIsInRole(string roleName)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                return _context.UserRoles
                    .Where(ur => ur.UserId == currentUserId)
                    .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                    .Contains(roleName);
            }
            catch
            {
                return false;
            }
        }

        // ================= CREATE GET =================
        public IActionResult Create()
        {
            try
            {
                ViewBag.Holders = new SelectList(_context.Users.ToList(), "Id", "Name");

                var viewModel = new CreateRecordViewModel
                {
                    StatementDate = DateTime.Today
                };

                return View("add_record", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Create GET");
                TempData["ErrorMessage"] = "An error occurred while loading the create form.";
                return RedirectToAction("Index");
            }
        }

        // ================= AJAX: GET HOLDER'S DEFAULT CARD =================
        public IActionResult GetHolderCard(int holderId)
        {
            try
            {
                var card = _context.UserCards
                    .Where(c => c.UserId == holderId)
                    .Select(c => new
                    {
                        id = c.CardId,
                        cardNumber = c.CardNumber,
                        bank = c.Bank
                    })
                    .FirstOrDefault();

                return Json(card);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting holder card");
                return Json(new { error = "Failed to load card data" });
            }
        }

        // ================= CREATE POST =================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            CreateRecordViewModel model,
            [FromServices] EmailService emailService)
        {
            try
            {
                // Remove display-only properties from validation
                ModelState.Remove("HolderName");
                ModelState.Remove("CardNumber");
                ModelState.Remove("BankName");
                ModelState.Remove("CurrentPhase");
                ModelState.Remove("AccountantFileUrl");
                ModelState.Remove("UserCardId");

                if (model.StatementDate > DateTime.Today)
                    ModelState.AddModelError("StatementDate", "Statement date cannot be in the future.");

                if (!ModelState.IsValid)
                {
                    ViewBag.Holders = new SelectList(_context.Users, "Id", "Name");
                    return View("add_record", model);
                }

                // Get the holder's default card
                var holderCard = await _context.UserCards
                    .Where(c => c.UserId == model.HolderId)
                    .FirstOrDefaultAsync();

                if (holderCard == null)
                {
                    ModelState.AddModelError("", "Selected holder does not have any cards. Please add a card for this holder first.");
                    ViewBag.Holders = new SelectList(_context.Users, "Id", "Name");
                    return View("add_record", model);
                }

                // Get current logged in accountant's ID
                var currentUserId = GetCurrentUserId();

                var record = new Records
                {
                    HolderId = model.HolderId,
                    UserCardId = holderCard.CardId,
                    Description = model.Description,
                    AccountantComment = model.AccountantComment,
                    StatementDate = model.StatementDate,
                    AccountantUserId = currentUserId,
                    CreatedByUserId = currentUserId,
                    CreatedAt = DateTime.Now,
                    CurrentPhase = "Accountant",
                    StatusTime = DateTime.Now // ADD THIS LINE - track when record was created

                };

                // Save file if provided
                if (model.AccountantFile != null && model.AccountantFile.Length > 0)
                {
                    string folder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
                    Directory.CreateDirectory(folder);

                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(model.AccountantFile.FileName);
                    string fullPath = Path.Combine(folder, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await model.AccountantFile.CopyToAsync(stream);
                    }

                    record.AccountantFileUrl = "/uploads/" + fileName;
                }

                _context.Records.Add(record);
                await _context.SaveChangesAsync();

                // Send email
                await SendNotificationEmail(record, emailService);

                TempData["SuccessMessage"] = "Record added successfully!";
                return RedirectToAction("Index");
            }
            catch (DbUpdateException ex)
            {
                var innerException = ex.InnerException?.Message ?? ex.Message;
                _logger.LogError(ex, "Database error in Create POST");
                ModelState.AddModelError("", $"Database Error: {innerException}");

                ViewBag.Holders = new SelectList(_context.Users, "Id", "Name");
                return View("add_record", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Create POST");
                ModelState.AddModelError("", $"Error: {ex.Message}");
                ViewBag.Holders = new SelectList(_context.Users, "Id", "Name");
                return View("add_record", model);
            }
        }

        private async Task SendNotificationEmail(Records record, EmailService emailService)
        {
            try
            {
                var holder = await _context.Users.FindAsync(record.HolderId);
                if (holder != null && !string.IsNullOrEmpty(holder.Email))
                {
                    var baseUrl = $"{Request.Scheme}://{Request.Host}";

                    // Create a simple encrypted token with timestamp
                    string tokenData = $"{record.RecordsId}|{record.HolderId}|{DateTime.Now.AddHours(24).Ticks}";
                    string token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(tokenData));

                    // URL encode the token
                    token = System.Web.HttpUtility.UrlEncode(token);

                    // Create URL with recordId and encrypted token
                    var accessUrl = $"{baseUrl}/Holders/AccessRecord?token={token}";

                    string subject = "New Record Added - Action Required";
                    string body = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                        .header {{ background-color: #4CAF50; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
                        .content {{ padding: 30px; background-color: #f9f9f9; }}
                        .button {{ display: inline-block; padding: 14px 28px; background-color: #4CAF50; color: white; 
                                 text-decoration: none; border-radius: 5px; margin: 20px 0; font-weight: bold; font-size: 16px; }}
                        .details {{ background-color: white; padding: 20px; border-radius: 5px; border-left: 4px solid #4CAF50; }}
                        .footer {{ text-align: center; padding: 20px; color: #666; font-size: 12px; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h2>📋 New Record Requires Your Attention</h2>
                        </div>
                        <div class='content'>
                            <p>Hello <strong>{holder.Name}</strong>,</p>
                            <p>An accountant has created a new record that requires your input.</p>
                            
                            <div class='details'>
                                <h3 style='margin-top: 0; color: #4CAF50;'>Record Details:</h3>
                                <table style='width: 100%; border-collapse: collapse;'>
                                    <tr>
                                        <td style='padding: 8px 0;'><strong>Statement Date:</strong></td>
                                        <td style='padding: 8px 0;'>{record.StatementDate:dd MMMM yyyy}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px 0;'><strong>Description:</strong></td>
                                        <td style='padding: 8px 0;'>{record.Description}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px 0;'><strong>Accountant Comment:</strong></td>
                                        <td style='padding: 8px 0;'>{record.AccountantComment ?? "N/A"}</td>
                                    </tr>
                                </table>
                            </div>
                            
                            <div style='text-align: center; margin: 30px 0;'>
                                <a href='{accessUrl}' class='button'>
                                    ✏️ Add Your Details (No Login Required)
                                </a>
                            </div>
                            
                            <p style='color: #666; font-size: 13px;'>
                                ⚠️ This link is valid for 24 hours. Do not share it with others.
                            </p>
                        </div>
                        <div class='footer'>
                            <p>This is an automated message from the Credit System.</p>
                            <p>© {DateTime.Now.Year} Credit System. All rights reserved.</p>
                        </div>
                    </div>
                </body>
                </html>";

                    await emailService.SendEmailAsync(holder.Email, subject, body);
                    _logger.LogInformation($"Email sent to holder {holder.Email} for record #{record.RecordsId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email sending failed");
            }
        }




        private int GetCurrentUserId()
        {
            // Try to get from claims first
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out int userId))
            {
                // Store in session for future use
                HttpContext.Session.SetInt32("CurrentUserId", userId);
                return userId;
            }

            // Try to get from session
            var sessionUserId = HttpContext.Session.GetInt32("CurrentUserId");
            if (sessionUserId.HasValue)
            {
                return sessionUserId.Value;
            }

            // If we're in a context where we don't need user ID, throw with clear message
            throw new UnauthorizedAccessException("User ID not found. Please ensure you are logged in.");
        }

        // ================= EDIT GET =================
        public async Task<IActionResult> Edit(int? recordId)
        {
            try
            {
                if (recordId == null)
                    return NotFound();

                var record = await _context.Records
                    .Include(r => r.Holder)
                    .Include(r => r.UserCard)
                    .Include(r => r.AccountantUser)
                    .Include(r => r.CreatedByUser)
                    .FirstOrDefaultAsync(r => r.RecordsId == recordId);

                if (record == null)
                    return NotFound();

                var viewModel = new CreateRecordViewModel
                {
                    RecordId = record.RecordsId,
                    HolderId = record.HolderId,
                    UserCardId = record.UserCardId,
                    StatementDate = record.StatementDate,
                    Description = record.Description,
                    AccountantComment = record.AccountantComment,
                    AccountantFileUrl = record.AccountantFileUrl
                };

                ViewBag.HolderName = record.Holder?.Name ?? "N/A";
                ViewBag.CardNumber = record.UserCard?.CardNumber ?? "N/A";
                ViewBag.BankName = record.UserCard?.Bank ?? "N/A";
                ViewBag.CurrentPhase = record.CurrentPhase ?? "N/A";
                ViewBag.AccountantName = record.AccountantUser?.Name ?? "N/A";
                ViewBag.CreatedByName = record.CreatedByUser?.Name ?? "N/A";
                ViewBag.CreatedAt = record.CreatedAt.ToString("yyyy-MM-dd HH:mm");

                return View("Edit", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Edit GET");
                TempData["ErrorMessage"] = "An error occurred while loading the edit form.";
                return RedirectToAction("Index");
            }
        }

        // ================= EDIT POST =================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int recordId, CreateRecordViewModel model)
        {
            try
            {
                if (recordId != model.RecordId)
                    return NotFound();

                // Remove validation for non-editable properties
                var propertiesToRemove = new[] {
                    "HolderName", "CardNumber", "BankName", "CurrentPhase",
                    "AccountantFileUrl", "HolderId", "UserCardId"
                };

                foreach (var prop in propertiesToRemove)
                {
                    if (ModelState.ContainsKey(prop))
                        ModelState.Remove(prop);
                }

                if (ModelState.ContainsKey("AccountantFile"))
                    ModelState.Remove("AccountantFile");

                if (model.StatementDate > DateTime.Today)
                    ModelState.AddModelError("StatementDate", "Statement date cannot be in the future.");

                if (!ModelState.IsValid)
                {
                    var record = await _context.Records
                        .Include(r => r.Holder)
                        .Include(r => r.UserCard)
                        .Include(r => r.AccountantUser)
                        .FirstOrDefaultAsync(r => r.RecordsId == recordId);

                    ViewBag.HolderName = record?.Holder?.Name ?? "N/A";
                    ViewBag.CardNumber = record?.UserCard?.CardNumber ?? "N/A";
                    ViewBag.BankName = record?.UserCard?.Bank ?? "N/A";
                    ViewBag.CurrentPhase = record?.CurrentPhase ?? "N/A";
                    ViewBag.AccountantName = record?.AccountantUser?.Name ?? "N/A";


                    return View("edit_record", model);
                }

                var recordToUpdate = await _context.Records.FindAsync(recordId);
                if (recordToUpdate == null)
                    return NotFound();

                // Update editable fields only
                recordToUpdate.StatementDate = model.StatementDate;
                recordToUpdate.Description = model.Description;
                recordToUpdate.AccountantComment = model.AccountantComment;

                // Handle file upload
                if (model.AccountantFile != null && model.AccountantFile.Length > 0)
                {
                    // Delete old file
                    if (!string.IsNullOrEmpty(recordToUpdate.AccountantFileUrl))
                    {
                        string oldFilePath = Path.Combine(_webHostEnvironment.WebRootPath,
                            recordToUpdate.AccountantFileUrl.TrimStart('/'));
                        if (System.IO.File.Exists(oldFilePath))
                            System.IO.File.Delete(oldFilePath);
                    }

                    // Save new file
                    string folder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
                    Directory.CreateDirectory(folder);

                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(model.AccountantFile.FileName);
                    string fullPath = Path.Combine(folder, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await model.AccountantFile.CopyToAsync(stream);
                    }

                    recordToUpdate.AccountantFileUrl = "/uploads/" + fileName;
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Record updated successfully!";
                return RedirectToAction("Index");
            }
            catch (DbUpdateException ex)
            {
                var innerException = ex.InnerException?.Message ?? ex.Message;
                _logger.LogError(ex, "Database error in Edit POST");
                ModelState.AddModelError("", $"Database Error: {innerException}");

                return View("edit_record", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Edit POST");
                ModelState.AddModelError("", $"Error: {ex.Message}");
                return View("edit_record", model);
            }
        }

        private bool RecordExists(int id)
        {
            return _context.Records.Any(e => e.RecordsId == id);
        }

        public async Task<IActionResult> Status(int? id)
        {
            try
            {
                if (id == null)
                {
                    return NotFound();
                }

                var record = await _context.Records
                    .Include(r => r.Holder)
                    .Include(r => r.UserCard)
                    .Include(r => r.AccountantUser)
                    .Include(r => r.CreatedByUser)
                    .Include(r => r.HolderFields)  // Make sure this is included!
                    .Include(r => r.AuditorApprovals)
                        .ThenInclude(a => a.AuditorUser)
                    .Include(r => r.BoardMemberApprovals)
                        .ThenInclude(b => b.BoardMemberUser)
                    .FirstOrDefaultAsync(r => r.RecordsId == id);

                if (record == null)
                {
                    return NotFound();
                }

                return View(record);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Status");
                TempData["ErrorMessage"] = "An error occurred while loading the record status.";
                return RedirectToAction("Index");
            }
        }
    }
}