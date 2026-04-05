using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using Software_Engineering.Data;
using Software_Engineering.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using static Software_Engineering.Models.YearlyChartData;

namespace Software_Engineering.Controllers
{
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        // log in (kelsey)
        [HttpGet]
        public IActionResult Index()
        {
            
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (HttpContext.Session.GetInt32("AdminId") != null)
            {
                return RedirectToAction("Dashboard");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string Username, string Password)
        {
            var admin = await _context.Admin.FirstOrDefaultAsync(a => a.Username == Username && a.Status == "Active");

            if (admin != null)
            {
                // Check Lockout
                if (admin.LockoutEnd.HasValue && admin.LockoutEnd.Value > DateTime.Now)
                {
                    var timeRemaining = (int)(admin.LockoutEnd.Value - DateTime.Now).TotalMinutes;
                    timeRemaining = timeRemaining < 1 ? 1 : timeRemaining;
                    TempData["LoginError"] = $"Account locked due to multiple failed attempts. Try again in {timeRemaining} minute(s).";
                    return View();
                }

                // Check Password
                if (admin.Password != Password)
                {
                    admin.FailedLoginAttempts++;
                    if (admin.FailedLoginAttempts >= 5)
                    {
                        admin.LockoutEnd = DateTime.Now.AddMinutes(15);
                        TempData["LoginError"] = "Maximum login attempts exceeded. Account locked for 15 minutes.";
                    }
                    else
                    {
                        int attemptsLeft = 5 - admin.FailedLoginAttempts;
                        TempData["LoginError"] = $"Invalid password. You have {attemptsLeft} attempt(s) left.";
                    }
                    _context.Admin.Update(admin);
                    await _context.SaveChangesAsync();
                    return View();
                }

                // Success - Reset Strikes
                admin.FailedLoginAttempts = 0;
                admin.LockoutEnd = null;
                _context.Admin.Update(admin);
                await _context.SaveChangesAsync();

                // Setup & OTP Logic
                if (string.IsNullOrWhiteSpace(admin.Email))
                {
                    HttpContext.Session.SetInt32("PendingAdminId", admin.Admin_Id);
                    ViewBag.ShowSetupEmailModal = true;
                    return View();
                }

                bool requireAdminOtp = !admin.LastOtpVerification.HasValue || (DateTime.Now - admin.LastOtpVerification.Value).TotalDays >= 5;

                if (requireAdminOtp)

                {
                    Random rand = new Random();
                    string otpCode = rand.Next(0, 10000).ToString("D4");
                    HttpContext.Session.SetString("AdminTempOTP", otpCode);
                    HttpContext.Session.SetInt32("TempAdminId", admin.Admin_Id);
                    HttpContext.Session.SetString("TempAdminName", admin.FullName);
                    HttpContext.Session.SetString("TempAdminUser", admin.Username);
                    HttpContext.Session.SetString("TempIsPrimary", admin.Is_Primary ? "True" : "False");

                    string subject = "Admin Login OTP - Carlton Residences";
                    string body = $"Your Admin OTP code is: {otpCode}";
                    bool emailSent = await SendEmailInternal(admin.Email, subject, body);

                    if (!emailSent)
                    {
                        TempData["LoginError"] = "Failed to send OTP to registered email.";
                        return View();
                    }

                    ViewBag.ShowVerifyLoginModal = true;
                    return View();
                }

                return await PerformLogin(admin);
            }

            // ==========================================
            // 2. RESIDENT LOGIN FLOW
            // ==========================================
            var resident = await _context.ResidentAccount
                .Include(r => r.ResidentInfo)
                .FirstOrDefaultAsync(r => r.Username == Username && r.Status == "Active");

            if (resident != null)
            {
                // Check Lockout
                if (resident.LockoutEnd.HasValue && resident.LockoutEnd.Value > DateTime.Now)
                {
                    var timeRemaining = (int)(resident.LockoutEnd.Value - DateTime.Now).TotalMinutes;
                    timeRemaining = timeRemaining < 1 ? 1 : timeRemaining;
                    TempData["LoginError"] = $"Account locked due to multiple failed attempts. Try again in {timeRemaining} minute(s).";
                    return View();
                }

                // Check Password
                if (resident.Password != Password)
                {
                    resident.FailedLoginAttempts++;
                    if (resident.FailedLoginAttempts >= 5)
                    {
                        resident.LockoutEnd = DateTime.Now.AddMinutes(15);
                        TempData["LoginError"] = "Maximum login attempts exceeded. Account locked for 15 minutes.";
                    }
                    else
                    {
                        int attemptsLeft = 5 - resident.FailedLoginAttempts;
                        TempData["LoginError"] = $"Invalid password. You have {attemptsLeft} attempt(s) left.";
                    }
                    _context.ResidentAccount.Update(resident);
                    await _context.SaveChangesAsync();
                    return View();
                }

                // Success - Reset Strikes
                resident.FailedLoginAttempts = 0;
                resident.LockoutEnd = null;
                _context.ResidentAccount.Update(resident);
                await _context.SaveChangesAsync();

                // RESIDENT OTP & LOGIN LOGIC (UPDATED)
                string email = resident.ResidentInfo?.Email;

                // ONLY trigger OTP if it's the 5th login AND they actually have an email linked
                bool requireResidentOtp = !resident.LastOtpVerification.HasValue || (DateTime.Now - resident.LastOtpVerification.Value).TotalDays >= 5;

                // mat-trigger lang if lumampas na 5 days and may email na linked sa account
                if (requireResidentOtp && !string.IsNullOrWhiteSpace(email))

                {
                    Random rand = new Random();
                    string otpCode = rand.Next(0, 10000).ToString("D4");

                    HttpContext.Session.SetString("TempOTP", otpCode);
                    HttpContext.Session.SetInt32("TempResidentId", resident.Resident_Id);
                    HttpContext.Session.SetString("TempResidentName", resident.ResidentInfo?.FullName ?? "Resident");

                    string subject = "Your Login OTP - Carlton Residences";
                    string body = $"Here is your OTP: {otpCode}.";
                    await SendEmailInternal(email, subject, body);

                    return RedirectToAction("VerifyOTP");
                }
                else
                {
                    // NORMAL LOGIN (Handles regular logins AND 5th-logins for residents with no email)
                    HttpContext.Session.SetInt32("ResidentId", resident.Resident_Id);
                    HttpContext.Session.SetString("ResidentName", resident.ResidentInfo?.FullName ?? "Resident");
                    HttpContext.Session.SetString("UserType", "Resident");

                    // Optional: Give them a friendly nudge to add their email!
                    if (string.IsNullOrWhiteSpace(email))
                    {
                        TempData["Success"] = "Logged in securely. Please update your profile with an email address for better account recovery!";
                    }

                    resident.LoginCount++;
                    await _context.SaveChangesAsync();

                    return RedirectToAction("Index", "Resident");
                }
            }

            // If neither Admin nor Resident is found
            TempData["LoginError"] = "Invalid username or password.";
            return View();
        }



        private async Task<IActionResult> PerformLogin(Admin admin)
        {
            if (admin == null) return RedirectToAction("Login");

            admin.FailedLoginAttempts = 0;
            admin.LockoutEnd = null;

            HttpContext.Session.SetInt32("AdminId", admin.Admin_Id);
            HttpContext.Session.SetString("AdminName", admin.FullName);
            HttpContext.Session.SetString("AdminUsername", admin.Username);
            HttpContext.Session.SetString("IsPrimary", admin.Is_Primary ? "True" : "False");

            HttpContext.Session.SetString("CanAddResident", admin.CanAddResident.ToString());
            HttpContext.Session.SetString("CanEditResident", admin.CanEditResident.ToString());
            HttpContext.Session.SetString("CanImportResident", admin.CanImportResident.ToString());
            HttpContext.Session.SetString("CanCreateInvoice", admin.CanCreateInvoice.ToString());
            HttpContext.Session.SetString("CanEditInvoice", admin.CanEditInvoice.ToString());
            HttpContext.Session.SetString("CanImportInvoice", admin.CanImportInvoice.ToString());
            HttpContext.Session.SetString("CanAddPayment", admin.CanAddPayment.ToString());
            HttpContext.Session.SetString("CanImportPayment", admin.CanImportPayment.ToString());
            HttpContext.Session.SetString("CanManageExpenses", admin.CanManageExpenses.ToString());
            HttpContext.Session.SetString("CanEditExpenses", admin.CanEditExpenses.ToString());


            var loginLog = new AdminLog
            {
                Admin_Id = admin.Admin_Id,
                Activity = $"{admin.FullName} has successfully logged in.",
                Timestamp = DateTime.Now
            };

            _context.AdminLog.Add(loginLog);
            admin.LoginCount++;

            await _context.SaveChangesAsync();

            return RedirectToAction("Dashboard", "Admin");
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            TempData["LoginError"] = "You have been securely logged out.";
            return RedirectToAction("Login", "Admin");
        }


        // dashboard n residents (gela)

        public IActionResult Dashboard(int? month, int? year)
        {
            int selectedMonth = month ?? DateTime.Now.Month;
            int selectedYear = year ?? DateTime.Now.Year;

            // Base resident query (NOT materialized yet)
            var residentQuery = _context.ResidentInfo
                .Where(r => r.Year_Of_Residency <= selectedYear);

            // Invoice query for selected period only
            var invoiceQuery = _context.Invoice
                .Where(i =>
                    i.Billing_Period.Month == selectedMonth &&
                    i.Billing_Period.Year == selectedYear);

            // Join residents with invoice for selected month
            var residentWithInvoice = from r in residentQuery
                                      join i in invoiceQuery
                                      on r.Resident_Id equals i.Resident_Id into invoiceGroup
                                      from i in invoiceGroup.DefaultIfEmpty()
                                      select new
                                      {
                                          r.Resident_Id,
                                          r.Phase_No,
                                          r.Block,
                                          InvoiceStatus = i != null ? i.Status : null
                                      };

            // TOTAL residents
            int totalResidents = residentQuery.Count();

            // PAID residents
            int paidResidents = residentWithInvoice
                .Where(x => x.InvoiceStatus == "Paid")
                .Select(x => x.Resident_Id)
                .Distinct()
                .Count();

            int unpaidResidents = totalResidents - paidResidents;

            decimal paymentRate = totalResidents == 0
                ? 0
                : Math.Round((decimal)paidResidents / totalResidents * 100, 2);

            // Block + Phase Summary (Fully SQL Executed)
            var blockSummaries = residentWithInvoice
                .GroupBy(x => new { x.Phase_No, x.Block })
                .Select(g => new
                {
                    g.Key.Phase_No,
                    g.Key.Block,
                    Total = g.Count(),
                    Paid = g.Count(x => x.InvoiceStatus == "Paid")
                })
                .AsEnumerable() // switch to memory for percentage + status
                .Select(g =>
                {
                    int unpaid = g.Total - g.Paid;

                    decimal paidPercentage = g.Total == 0
                        ? 0
                        : Math.Round((decimal)g.Paid / g.Total * 100, 2);

                    string status =
                        paidPercentage >= 70 ? "Green" :
                        paidPercentage >= 40 ? "Yellow" :
                        "Red";

                    return new BlockPaymentSummary
                    {
                        Phase = g.Phase_No,
                        Block = g.Block,
                        PaidCount = g.Paid,
                        UnpaidCount = unpaid,
                        PaidPercentage = paidPercentage,
                        PaymentStatus = status
                    };
                })
                .OrderBy(b => b.Phase)
                .ThenBy(b => b.Block)
                .ToList();

            // Year filter
            int startYear = 2017;
            int currentYear = DateTime.Now.Year;

            var years = Enumerable.Range(startYear, currentYear - startYear + 1)
                                  .Reverse()
                                  .ToList();

            var vm = new DashboardViewModel
            {
                SelectedMonth = selectedMonth,
                SelectedYear = selectedYear,
                Years = years,
                TotalResidents = totalResidents,
                PaidResidents = paidResidents,
                UnpaidResidents = unpaidResidents,
                PaymentRate = paymentRate,
                BlockSummaries = blockSummaries
            };

            return View(vm);
        }


        public async Task<IActionResult> Residents(string search, int? month, int? year)
        {
            if (HttpContext.Session.GetInt32("AdminId") == null) return RedirectToAction("Login");

            var now = DateTime.Now;

            var expiredInvoices = await _context.Invoice
                .Where(i => i.Expiry_Date != null &&
                            i.Expiry_Date < now &&
                            (i.Status == null || i.Status.Trim().ToLower() == "unpaid"))
                .ToListAsync();

            if (expiredInvoices.Any())
            {
                _context.Invoice.RemoveRange(expiredInvoices);
                await _context.SaveChangesAsync();
            }

            ViewBag.AdminName = HttpContext.Session.GetString("AdminName");


            var residents = await _context.ResidentInfo
                .Include(r => r.ResidentAccount)
                .Include(r => r.Invoices).ThenInclude(i => i.Admin)
                .ToListAsync();

            ViewBag.SelectedMonth = month ?? DateTime.Now.Month;
            ViewBag.SelectedYear = year ?? DateTime.Now.Year;
            ViewBag.Search = search;

            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim().ToLower();
                residents = residents.Where(r =>
                    r.FullName.ToLower().Contains(s) ||
                    (r.ResidentAccount?.Username?.ToLower().Contains(s) ?? false) ||
                    r.Block.ToLower().Contains(s) ||
                    r.Lot.ToLower().Contains(s) ||
                    $"R{(r.Year_Of_Residency % 100):D2}-{r.Resident_Id:D4}".ToLower().Contains(s)
                ).ToList();
            }

            ViewBag.InvoiceList = await _context.Invoice.Select(i => new {
                invoiceNo = i.Invoice_No,
                residentId = i.Resident_Id,
                amount = i.Total_Amount,
                month = i.Billing_Period.Month,
                year = i.Billing_Period.Year,
                status = (i.Status ?? "Unpaid").Trim().ToLower()
            }).ToListAsync();

            return View(residents);
        }

        //resident profile & email (gela kelsey)

        public async Task<IActionResult> ResidentProfile(
    int id,
    int? payMonth, int? payYear,
    int? soaMonth, int? soaYear,
    int? invMonth, int? invYear,
    string activeTab)
        {
            var resident = await _context.ResidentInfo
                .Include(r => r.Invoices).ThenInclude(i => i.Payments)
                .Include(r => r.Invoices).ThenInclude(i => i.Admin)
                .FirstOrDefaultAsync(r => r.Resident_Id == id);

            if (resident == null) return NotFound();

            // ================= ACTIVE TAB =================
            ViewBag.ActiveTab = activeTab ?? "payments";

            // ================= PAYMENTS FILTER =================
            var paymentsQuery = _context.Payment
                .Include(p => p.Invoice).ThenInclude(i => i.Payments)
                .Include(p => p.Admin)
                .Where(p => p.Invoice.Resident_Id == id);

            if (payMonth != null)
                paymentsQuery = paymentsQuery.Where(p => p.Invoice.Billing_Period.Month == payMonth);

            if (payYear != null)
                paymentsQuery = paymentsQuery.Where(p => p.Invoice.Billing_Period.Year == payYear);

            var payments = await paymentsQuery
                .OrderBy(p => p.Date_Issued)
                .ToListAsync();

            ViewBag.SelectedPayMonth = payMonth;
            ViewBag.SelectedPayYear = payYear;

            // ================= INVOICES FILTER =================
            var invoicesQuery = resident.Invoices.AsQueryable();

            if (invMonth != null)
                invoicesQuery = invoicesQuery.Where(i => i.Billing_Period.Month == invMonth);

            if (invYear != null)
                invoicesQuery = invoicesQuery.Where(i => i.Billing_Period.Year == invYear);

            var invoices = invoicesQuery
                .OrderByDescending(i => i.Billing_Period)
                .ToList();

            ViewBag.SelectedInvMonth = invMonth;
            ViewBag.SelectedInvYear = invYear;

            // ================= STATEMENTS FILTER =================
            var statementsQuery = resident.Invoices.AsQueryable();

            if (soaMonth != null)
                statementsQuery = statementsQuery.Where(i => i.Billing_Period.Month == soaMonth);

            if (soaYear != null)
                statementsQuery = statementsQuery.Where(i => i.Billing_Period.Year == soaYear);

            var statements = statementsQuery
                .OrderBy(i => i.Billing_Period)
                .ToList();

            ViewBag.SelectedSoaMonth = soaMonth;
            ViewBag.SelectedSoaYear = soaYear;

            // ================= EMAIL TEMPLATE (UNCHANGED) =================
            decimal totalOutstanding = resident.Invoices
                .Where(i => i.Status == "Unpaid")
                .Sum(i => i.Total_Amount);

            decimal monthlyDue = resident.Invoices
                .OrderByDescending(i => i.Billing_Period)
                .FirstOrDefault()?.Total_Amount ?? 0;

            string emailTemplate =
        $@"Good day! {resident.FullName}.

We hope this message finds you well. This is a gentle reminder regarding your outstanding balance for the monthly dues at Carlton Residences Homeowners Association.

Billing Summary:
• Monthly Due: ₱{monthlyDue:N2}
• Total Outstanding Balance: ₱{totalOutstanding:N2}

We kindly encourage you to settle your payment on or before [DUE DATE] to avoid any inconvenience.

Payments may be made at the Carlton Residences Homeowners Association Office. For your reference, you may also view your detailed Statement of Account and monitor your dues by logging in to our official website:

👉 https://carltonresidences.com/resident

Should you have any questions or require clarification, please do not hesitate to contact us.

Thank you for your continued cooperation and support in keeping our community well-maintained.

Warm regards,
Carlton Residences Homeowners Association, Inc.
admin@carltonresidences.com";

            ViewBag.EmailTemplate = emailTemplate;

            return View(new ResidentProfileViewModel
            {
                Resident = resident,
                Payments = payments,
                Invoices = invoices,
                Statements = statements
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetSubAdmins()
        {
            var admins = await _context.Admin
                .Where(a => a.Is_Primary == false && a.Status == "Active")
                .Select(a => new {
                    a.Admin_Id,
                    a.Username,
                    a.FullName,

                    Permissions = (a.CanAddResident ? "Add Res, " : "") +
                                  (a.CanEditResident ? "Edit Res, " : "") +
                                  (a.CanImportResident ? "Imp Res, " : "") +

                                  (a.CanCreateInvoice ? "Add Inv, " : "") +
                                  (a.CanEditInvoice ? "Edit Inv, " : "") +
                                  (a.CanImportInvoice ? "Imp Inv, " : "") +

                                  (a.CanAddPayment ? "Add Pay, " : "") +
                                  (a.CanImportPayment ? "Imp Pay" : "")
                })
                .ToListAsync();

            var cleanedAdmins = admins.Select(a => new {
                a.Admin_Id,
                a.Username,
                a.FullName,
                permissions = a.Permissions.TrimEnd(',', ' ')
            });

            return Json(new { success = true, data = cleanedAdmins });
        }

        // 4. UPDATE EXISTING SUB-ADMIN
        [HttpPost]
        public async Task<IActionResult> UpdateSubAdmin(
int AdminId, string FullName, string Username, string Password,
List<string> SelectedPermissions,
bool CanEditResident, bool CanEditInvoice,
bool CanImportResident, bool CanImportInvoice, bool CanImportPayment, bool CanManageExpenses, bool CanEditExpenses)
        {
            var admin = await _context.Admin.FindAsync(AdminId);
            if (admin == null) return NotFound();

            admin.FullName = FullName;
            admin.Username = Username;

            if (!string.IsNullOrWhiteSpace(Password))
            {
                admin.Password = Password;
            }

            admin.CanAddResident = SelectedPermissions?.Contains("AddResident") ?? false;
            admin.CanCreateInvoice = SelectedPermissions?.Contains("CreateInvoice") ?? false;
            admin.CanAddPayment = SelectedPermissions?.Contains("AddPayment") ?? false;

            admin.CanEditResident = CanEditResident;
            admin.CanEditInvoice = CanEditInvoice;

            admin.CanManageExpenses = CanManageExpenses;
            admin.CanEditExpenses = CanEditExpenses;

            admin.CanImportResident = admin.CanAddResident && CanImportResident;
            admin.CanImportInvoice = admin.CanCreateInvoice && CanImportInvoice;
            admin.CanImportPayment = admin.CanAddPayment && CanImportPayment;

            _context.Admin.Update(admin);
            await _context.SaveChangesAsync();

            string adminName = HttpContext.Session.GetString("AdminName") ?? "System Administrator";
            string detail = !string.IsNullOrWhiteSpace(Password) ? "password and permissions" : "permissions";
            await LogActivity($"{adminName} updated {admin.Username}'s {detail}");

            TempData["Success"] = "Admin updated successfully.";
            return RedirectToAction("Dashboard");
        }



        [HttpGet]
        public async Task<IActionResult> GetSubAdminDetails(int id)
        {
            var admin = await _context.Admin.FindAsync(id);
            if (admin == null) return Json(new { success = false, message = "Admin not found" });

            return Json(new
            {
                success = true,
                id = admin.Admin_Id,
                username = admin.Username,
                fullName = admin.FullName,

                // Resident Module
                // Resident Module
                canAddResident = admin.CanAddResident,
                canEditResident = admin.CanEditResident,     // <-- FIXED
                canImportResident = admin.CanImportResident,

                // Invoice Module
                canCreateInvoice = admin.CanCreateInvoice,
                canEditInvoice = admin.CanEditInvoice,       // <-- FIXED
                canImportInvoice = admin.CanImportInvoice,

                // Payment Module
                canAddPayment = admin.CanAddPayment,
                canImportPayment = admin.CanImportPayment,

                canManageExpenses = admin.CanManageExpenses,
                canEditExpense = admin.CanEditExpenses

            });
        }

        // ==========================================
        // 4. DELETE ADMIN
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> DeleteSubAdmin(int id)
        {
            // 1. Check Session & Security
            var currentUserId = HttpContext.Session.GetInt32("AdminId");
            var isPrimary = HttpContext.Session.GetString("IsPrimary");

            if (currentUserId == null || isPrimary != "True")
            {
                return Json(new { success = false, message = "Access Denied: Only the Primary Admin can perform this action." });
            }

            // 2. Prevent Self-Deletion
            if (id == currentUserId)
            {
                return Json(new { success = false, message = "You cannot delete your own account." });
            }

            // 3. Perform the Delete
            try
            {
                var adminToDelete = await _context.Admin.FindAsync(id);

                if (adminToDelete == null)
                {
                    return Json(new { success = false, message = "Admin not found." });
                }

                // Prevent deleting the Primary Admin via this method
                if (adminToDelete.Is_Primary)
                {
                    return Json(new { success = false, message = "The Primary Admin account cannot be deleted." });
                }

                // Capture data before deletion
                string deleterName = HttpContext.Session.GetString("AdminName") ?? "System Administrator";
                string deletedUser = adminToDelete.Username ?? "Unknown Admin";

                // 4. Remove and Log
                _context.Admin.Remove(adminToDelete);

                // Pass the full sentence as one string to match your LogActivity(string act)
                await LogActivity($"{deleterName} deleted {deletedUser}'s account");

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Admin successfully deleted." });
            }
            catch (Exception ex)
            {
                // Helpful for debugging DBNull or casting errors
                return Json(new { success = false, message = "Database Error: " + ex.Message });
            }
        }

        // invoice email
        [HttpPost]
        public async Task<IActionResult> SendInvoiceEmail(int ResidentId, string Subject, string EmailBody)
        {
            var resident = await _context.ResidentInfo
                .FirstOrDefaultAsync(r => r.Resident_Id == ResidentId);

            if (resident == null || string.IsNullOrWhiteSpace(resident.Email))
            {
                TempData["EmailError"] = "Resident email not found.";
                return RedirectToAction("ResidentProfile", new
                {
                    id = ResidentId,
                    activeTab = "invoices"
                });
            }

            try
            {
                var message = new MailMessage();
                message.To.Add(resident.Email);
                message.Subject = Subject;
                message.Body = EmailBody;
                message.IsBodyHtml = false;
                message.From = new MailAddress("itsarathearmy@gmail.com", "Carlton Residences HOA");

                using (var smtp = new SmtpClient("smtp.gmail.com", 587))
                {
                    smtp.Credentials = new NetworkCredential(
                        "itsarathearmy@gmail.com",
                        "fmks zcrr azjk pmfx\r\n"
                    );
                    smtp.EnableSsl = true;
                    await smtp.SendMailAsync(message);
                }

                TempData["EmailSuccess"] = "Email sent successfully.";
            }
            catch
            {
                TempData["EmailError"] = "Failed to send email.";
            }

            return RedirectToAction("ResidentProfile", new
            {
                id = ResidentId,
                activeTab = "invoices"
            });

        }

        // crud
        private async Task CreateCurrentMonthInvoiceIfMissing(
        int residentId,
        int? adminId,
        string adminName)
        {
            int month = DateTime.Now.Month;
            int year = DateTime.Now.Year;

            bool exists = await _context.Invoice.AnyAsync(i =>
                i.Resident_Id == residentId &&
                i.Billing_Period.Month == month &&
                i.Billing_Period.Year == year);

            if (exists) return;

            var billingPeriod = new DateTime(year, month, 1);

            var invoice = new Invoice
            {
                Resident_Id = residentId,
                Admin_Id = adminId,
                Billing_Period = billingPeriod,
                Due_Date = billingPeriod.AddMonths(1).AddDays(-1),
                Total_Amount = 150,
                Status = "Unpaid",
                Date_Issued = DateTime.Now,
                Description = "Monthly HOA Dues"
            };

            _context.Invoice.Add(invoice);
            await _context.SaveChangesAsync();

            // email resident
            var resident = await _context.ResidentInfo
                .FirstOrDefaultAsync(r => r.Resident_Id == residentId);

            if (resident == null || string.IsNullOrWhiteSpace(resident.Email))
                return; 

            try
            {
                string subject = $"Carlton Residences Monthly Dues Invoice: {billingPeriod:MMMM yyyy}";
                string body = $@"
Good day! {resident.FullName},

Your monthly dues invoice for {billingPeriod:MMMM yyyy} has been created.

Amount Due: ₱150.00
Due Date: {invoice.Due_Date:MMMM dd, yyyy}
                
Kindly settle your payment on or before the due date at the Control Point Office. Thank you!

Regards,
{adminName}
Carlton Residences HOA
";

                var message = new MailMessage
                {
                    From = new MailAddress("itsarathearmy@gmail.com", "Carlton Residences HOA"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = false
                };

                message.To.Add(resident.Email);

                using (var smtp = new SmtpClient("smtp.gmail.com", 587))
                {
                    smtp.Credentials = new NetworkCredential(
                        "itsarathearmy@gmail.com",
                        "fmks zcrr azjk pmfx\r\n" 
                    );
                    smtp.EnableSsl = true;
                    await smtp.SendMailAsync(message);
                }
            }
            catch
            {
                
            }
        }


        [HttpPost]
        public async Task<IActionResult> AddResident(
            string FirstName,
            string LastName,
            string MiddleName,
            string Block,
            string Lot,
            string PhaseNo,
            int YearOfResidency,
            string ContactNo,
            string Email)
        {
            // 1️ CHECK FOR DUPLICATE BLOCK + LOT + PHASE BEFORE ANYTHING ELSE
            bool exists = await _context.ResidentInfo
                .AnyAsync(r => r.Block == Block && r.Lot == Lot && r.Phase_No == PhaseNo);
            ViewBag.AdminName = HttpContext.Session.GetString("AdminName");
            var admin = await _context.Admin
                .FirstOrDefaultAsync(a => a.Username == User.Identity.Name);
            int? adminId = HttpContext.Session.GetInt32("AdminId");


            if (exists)
            {
                TempData["ResidentErrorAlert"] = $"A resident already exists for Block {Block}, Lot {Lot}, Phase {PhaseNo}.";
                return RedirectToAction("Residents");
            }


            // 2️ Combine full name
            string fullName = $"{LastName}, {FirstName} {MiddleName}".Trim();

            // 3️ Save ResidentInfo
            var resident = new ResidentInfo
            {
                FullName = fullName,
                Block = Block,
                Lot = Lot,
                Phase_No = PhaseNo,
                Year_Of_Residency = YearOfResidency,
                Contact_No = ContactNo,
                Email = Email
            };

            _context.ResidentInfo.Add(resident);
            await _context.SaveChangesAsync();   

            // 4️ Username + Password generation
            string cleanLast = LastName.Replace(" ", "").ToLower();
            string properLast = char.ToUpper(cleanLast[0]) + cleanLast.Substring(1);

            string username = $"{cleanLast}{resident.Resident_Id}{Block}{Lot}";

            var random = new Random();
            string digits = random.Next(0, 99999).ToString("D5");

            string specialChars = "!@#$%^&*";
            char special = specialChars[random.Next(specialChars.Length)];

            string password = $"{properLast}{digits}{special}";

            // 5️ Save account
            var account = new ResidentAccount
            {
                Resident_Id = resident.Resident_Id,
                Username = username,
                Password = password,
                Status = "Active"
            };

            _context.ResidentAccount.Add(account);
            await _context.SaveChangesAsync();

            TempData["NewUsername"] = username;
            TempData["NewPassword"] = password;
            await LogActivity($"Added new resident: {resident.FullName} [(Block {resident.Block} Lot {resident.Lot} Phase {resident.Phase_No})]");

            // TRIGGER IMMEDIATE BILLING
            await CreateCurrentMonthInvoiceIfMissing(
                resident.Resident_Id,
                HttpContext.Session.GetInt32("AdminId"),
                HttpContext.Session.GetString("AdminName")
            );

            TempData["Success"] = "Resident added and billed successfully.";
            return RedirectToAction("Residents");


        }

        public async Task<IActionResult> ImportResidentsCsv(IFormFile csvFile)
        {
            List<string> errorMessages = new List<string>();
            List<string> successMessages = new List<string>();

            int rowNumber = 1;
            int? adminId = HttpContext.Session.GetInt32("AdminId");

            if (adminId == null)
            {
                TempData["ImportErrors"] = System.Text.Json.JsonSerializer.Serialize(
                    new List<string> { "Session expired. Please log in again." });

                return RedirectToAction("Login", "Account");
            }


            if (csvFile == null || csvFile.Length == 0)
            {
                TempData["ImportError"] = "Please upload a valid CSV file.";
                return RedirectToAction("Residents");
            }

            using var reader = new StreamReader(csvFile.OpenReadStream());
            using var csv = new CsvHelper.CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture);

            var records = csv.GetRecords<ResidentCsvModel>().ToList();

            int success = 0;
            int failed = 0;

            foreach (var r in records)
            {
                rowNumber++;

                if (string.IsNullOrWhiteSpace(r.FirstName) ||
                string.IsNullOrWhiteSpace(r.LastName) ||
                string.IsNullOrWhiteSpace(r.Block) ||
                string.IsNullOrWhiteSpace(r.Lot) ||
                string.IsNullOrWhiteSpace(r.Phase) ||
                !r.YearOfResidency.HasValue ||
                r.YearOfResidency <= 1950 ||
                r.YearOfResidency > DateTime.Now.Year)
                {
                    failed++;
                    errorMessages.Add($"Row {rowNumber}: Missing or invalid required fields.");
                    continue;
                }


                bool exists = await _context.ResidentInfo.AnyAsync(x =>
                    x.Block == r.Block &&
                    x.Lot == r.Lot &&
                    x.Phase_No == r.Phase);

                if (exists)
                {
                    failed++;
                    errorMessages.Add($"Row {rowNumber}: Block {r.Block} Lot {r.Lot} Phase {r.Phase} already exists.");
                    continue;
                }

                try
                {

                    string baseUsername = GenerateUsername(r.Block, r.Lot, r.LastName);
                    string finalUsername = baseUsername;
                    int counter = 1;

                    while (await _context.ResidentAccount.AnyAsync(x => x.Username == finalUsername))
                    {
                        finalUsername = baseUsername + counter;
                        counter++;
                    }

                    string generatedPassword = GeneratePassword();

                    var resident = new ResidentInfo
                    {
                        FullName = $"{r.LastName}, {r.FirstName} {r.MiddleName}",
                        Block = r.Block,
                        Lot = r.Lot,
                        Phase_No = r.Phase,
                        Year_Of_Residency = r.YearOfResidency.Value,
                        Email = r.Email,
                        Contact_No = r.ContactNo
                    };

                    _context.ResidentInfo.Add(resident);
                    await _context.SaveChangesAsync(); // to get Resident_Id

                    var account = new ResidentAccount
                    {
                        Resident_Id = resident.Resident_Id,
                        Username = finalUsername,
                        Password = generatedPassword,
                        Status = "Active"
                    };

                    _context.ResidentAccount.Add(account);
                    // TRIGGER IMMEDIATE BILLING
                    await CreateCurrentMonthInvoiceIfMissing(
                        resident.Resident_Id,
                        HttpContext.Session.GetInt32("AdminId"),
                        HttpContext.Session.GetString("AdminName")
                    );

                    success++;
                    successMessages.Add($"Row {rowNumber}: Account → {finalUsername} / {generatedPassword}");
                }
                catch (Exception ex)
                {
                    failed++;
                    errorMessages.Add($"Row {rowNumber}: System error - {ex.Message}");
                }
            }

            await _context.SaveChangesAsync();

            TempData["ImportResult"] = $"Imported: {success}, Failed: {failed}";
            TempData["ImportErrors"] = System.Text.Json.JsonSerializer.Serialize(errorMessages);
            TempData["ImportSuccessDetails"] = System.Text.Json.JsonSerializer.Serialize(successMessages);

            return RedirectToAction("Residents");
        }


        private string GenerateUsername(string block, string lot, string lastName)
        {
            return $"{lastName}{block}{lot}".Replace(" ", "").ToLower();
        }

        private string GeneratePassword()
        {
            return Guid.NewGuid().ToString().Substring(0, 8);
        }

        [HttpPost]
        public async Task<IActionResult> ImportInvoicesCsv(IFormFile csvFile, int residentId)
        {
            List<string> errors = new();
            int success = 0;
            int failed = 0;
            int rowNumber = 1;

            int? adminId = HttpContext.Session.GetInt32("AdminId");

            if (adminId == null)
            {
                TempData["ImportErrors"] = System.Text.Json.JsonSerializer.Serialize(
                    new List<string> { "Session expired. Please log in again." });

                return RedirectToAction("Login", "Account");
            }


            if (csvFile == null || csvFile.Length == 0)
            {
                TempData["ImportErrors"] = System.Text.Json.JsonSerializer.Serialize(
                    new List<string> { "Please upload a valid CSV file." });
                return RedirectToAction("ResidentProfile", new { id = residentId });
            }

            using var reader = new StreamReader(csvFile.OpenReadStream());
            using var csv = new CsvHelper.CsvReader(reader, CultureInfo.InvariantCulture);

            csv.Context.TypeConverterOptionsCache
                .GetOptions<DateTime?>()
                .Formats = new[] { "MM-dd-yyyy" };

            var records = csv.GetRecords<InvoiceCsvModel>().ToList();

            foreach (var r in records)
            {
                rowNumber++;

                if (!r.DateIssued.HasValue ||
                r.DateIssued.Value == DateTime.MinValue ||
                !r.Total_Amount.HasValue ||
                r.Total_Amount.Value <= 0)
                {
                    failed++;
                    errors.Add($"Row {rowNumber}: Invalid Date Issued or Amount.");
                    continue;
                }


                var firstDayOfMonth = new DateTime(
                r.DateIssued.Value.Year,
                r.DateIssued.Value.Month,
                1);

                var firstDayNextMonth = firstDayOfMonth.AddMonths(1);

                bool alreadyExists = await _context.Invoice.AnyAsync(x =>
                    x.Resident_Id == residentId &&
                    x.Date_Issued >= firstDayOfMonth &&
                    x.Date_Issued < firstDayNextMonth);


                if (alreadyExists)
                {
                    failed++;
                    errors.Add($"Row {rowNumber}: Invoice for this billing month already exists.");

                    continue;
                }

                try
                {
                    var issued = r.DateIssued.Value;

                    var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

                    var invoice = new Invoice
                    {
                        Resident_Id = residentId,
                        Date_Issued = issued,
                        Billing_Period = firstDayOfMonth,
                        Due_Date = lastDayOfMonth,
                        Description = r.Description,
                        Total_Amount = r.Total_Amount.Value,
                        Status = "Unpaid",
                        Admin_Id = adminId.Value
                    };


                    _context.Invoice.Add(invoice);
                    success++;
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");
                }
            }

            await _context.SaveChangesAsync();

            TempData["ImportResult"] = $"Invoices Imported: {success}, Failed: {failed}";
            TempData["ImportErrors"] = System.Text.Json.JsonSerializer.Serialize(errors);

            return RedirectToAction("ResidentProfile", new
            {
                id = residentId,
                activeTab = "invoices"
            });

        }


        [HttpPost]
        public async Task<IActionResult> ImportPaymentsCsv(IFormFile csvFile, int residentId)
        {
            List<string> errors = new();
            int success = 0;
            int failed = 0;
            int rowNumber = 1;

            int? adminId = HttpContext.Session.GetInt32("AdminId");

            if (adminId == null)
            {
                TempData["ImportErrors"] = System.Text.Json.JsonSerializer.Serialize(
                    new List<string> { "Session expired. Please log in again." });

                return RedirectToAction("Login", "Account");
            }


            if (csvFile == null || csvFile.Length == 0)
            {
                TempData["ImportErrors"] = System.Text.Json.JsonSerializer.Serialize(
                    new List<string> { "Please upload a valid CSV file." });
                return RedirectToAction("ResidentProfile", new { id = residentId });
            }

            using var reader = new StreamReader(csvFile.OpenReadStream());
            using var csv = new CsvHelper.CsvReader(reader, CultureInfo.InvariantCulture);

            csv.Context.TypeConverterOptionsCache
                .GetOptions<DateTime?>()
                .Formats = new[] { "MM-dd-yyyy" };

            var records = csv.GetRecords<PaymentCsvModel>().ToList();

            foreach (var r in records)
            {
                rowNumber++;

                // Trim messy CSV inputs
                r.OR_No = r.OR_No?.Trim();
                r.Method = r.Method?.Trim();

                // Required fields
                if (!r.PaymentDate.HasValue ||
                    !r.Total_Amount.HasValue || r.Total_Amount.Value <= 0 ||
                    string.IsNullOrWhiteSpace(r.InvoiceMonth) ||
                    string.IsNullOrWhiteSpace(r.OR_No) ||
                    string.IsNullOrWhiteSpace(r.Method))
                {
                    failed++;
                    errors.Add($"Row {rowNumber}: Missing required fields.");
                    continue;
                }

                // OR must be exactly 5 digits
                if (!System.Text.RegularExpressions.Regex.IsMatch(r.OR_No, @"^\d{5}$"))
                {
                    failed++;
                    errors.Add($"Row {rowNumber}: OR Number must be exactly 5 digits.");
                    continue;
                }

                try
                {
                    // Parse "January 2026"
                    var parts = r.InvoiceMonth.Split(' ');
                    int month = DateTime.ParseExact(parts[0], "MMMM", CultureInfo.InvariantCulture).Month;
                    int year = int.Parse(parts[1]);

                    var firstDay = new DateTime(year, month, 1);
                    var nextMonth = firstDay.AddMonths(1);

                    var invoice = await _context.Invoice.FirstOrDefaultAsync(x =>
                        x.Resident_Id == residentId &&
                        x.Billing_Period >= firstDay &&
                        x.Billing_Period < nextMonth);

                    if (invoice == null)
                    {
                        failed++;
                        errors.Add($"Row {rowNumber}: Invoice for {r.InvoiceMonth} not found.");
                        continue;
                    }

                    // Check current payments for this invoice
                    var totalPaidSoFar = await _context.Payment
                        .Where(p => p.Invoice_No == invoice.Invoice_No)
                        .SumAsync(p => (decimal?)p.Total_Amount) ?? 0;

                    // Already fully paid
                    if (totalPaidSoFar >= invoice.Total_Amount)
                    {
                        failed++;
                        errors.Add($"Row {rowNumber}: Invoice for {r.InvoiceMonth} is already fully paid.");
                        continue;
                    }

                    // Overpayment
                    if (totalPaidSoFar + r.Total_Amount.Value > invoice.Total_Amount)
                    {
                        failed++;
                        errors.Add($"Row {rowNumber}: Payment exceeds remaining balance for {r.InvoiceMonth}.");
                        continue;
                    }

                    // Create payment
                    var payment = new Payment
                    {
                        Invoice_No = invoice.Invoice_No,
                        Total_Amount = r.Total_Amount.Value,
                        Method = r.Method,
                        Date_Issued = r.PaymentDate.Value,
                        OR_No = r.OR_No,
                        Remarks = r.Remarks,
                        Admin_Id = adminId.Value // set if you track logged admin
                    };

                    _context.Payment.Add(payment);

                    if (totalPaidSoFar + r.Total_Amount.Value == invoice.Total_Amount)
                    {
                        invoice.Status = "Paid";
                    }

                    success++;
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"Row {rowNumber}: {ex.Message}");
                }
            }

            await _context.SaveChangesAsync();

            TempData["ImportResult"] = $"Payments Imported: {success}, Failed: {failed}";
            TempData["ImportErrors"] = System.Text.Json.JsonSerializer.Serialize(errors);

            return RedirectToAction("ResidentProfile", new
            {
                id = residentId,
                activeTab = "payments"
            });

        }

        [HttpGet]
        public async Task<int> GetNextResidentId()
        {
            var maxId = await _context.ResidentInfo.OrderByDescending(r => r.Resident_Id).Select(r => r.Resident_Id).FirstOrDefaultAsync();
            return maxId + 1;
        }


        [HttpPost]
        public async Task<IActionResult> CreateInvoice(
            string Block,
            string Lot,
            string Phase_No,
            int Year,
            List<int> Months,
            decimal Rate,
            int? DurationValue,
            string DurationUnit,
            bool UseDuration,
            string Description)
        {
            // MONTHS REQUIRED
            if (Months == null || Months.Count == 0)
            {
                TempData["InvoiceError"] = "Please select at least one billing month.";
                return RedirectToAction("Residents");
            }

            // Find RESIDENT using Block + Lot
            var resident = await _context.ResidentInfo
            .Include(r => r.ResidentAccount)
            .FirstOrDefaultAsync(r =>
            r.Block == Block &&
            r.Lot == Lot &&
            r.Phase_No == Phase_No);


            if (resident.ResidentAccount?.Status == "Inactive")
            {
                TempData["InvoiceError"] = "This resident is inactive. You cannot create invoices for inactive residents.";
                return RedirectToAction("Residents");
            }


            if (resident == null)
            {
                TempData["InvoiceError"] = "No resident found for this Block & Lot.";
                return RedirectToAction("Residents");
            }

            
            if (string.IsNullOrWhiteSpace(resident.FullName))
            {
                TempData["InvoiceError"] = "Resident name must be available before submitting.";
                return RedirectToAction("Residents");
            }

            // Find CURRENT ADMIN
            ViewBag.AdminName = HttpContext.Session.GetString("AdminName");

            var admin = await _context.Admin
                .FirstOrDefaultAsync(a => a.Username == User.Identity.Name);
            int? adminId = HttpContext.Session.GetInt32("AdminId");


            // CHECK ALL MONTHS FOR DUPLICATION FIRST
            List<string> duplicateMonths = new List<string>();


            foreach (int month in Months)
            {
                bool duplicateExists = await _context.Invoice.AnyAsync(i =>
                    i.Resident_Id == resident.Resident_Id &&
                    i.Billing_Period.Month == month &&
                    i.Billing_Period.Year == Year
                );

                if (duplicateExists)
                {
                    duplicateMonths.Add(new DateTime(Year, month, 1).ToString("MMMM yyyy"));
                }
            }

            if (duplicateMonths.Count > 0)
            {
                string msg = "Invoice already exists for: " + string.Join(", ", duplicateMonths);
                TempData["InvoiceError"] = msg;
                return RedirectToAction("Residents");
            }

            var now = DateTime.Now;

            foreach (int month in Months)
            {
                var billingPeriod = new DateTime(Year, month, 1);
                var dueDate = billingPeriod.AddMonths(1).AddDays(-1);
                DateTime? expiryDate = null;

                bool isFuture =
                    Year > now.Year ||
                    (Year == now.Year && month > now.Month);

                if (UseDuration && isFuture && (DurationValue ?? 0) > 0)
                {
                    int value = DurationValue.Value;

                    switch (DurationUnit ?? "days")
                    {
                        case "weeks":
                            expiryDate = now.AddDays(value * 7);
                            break;

                        case "months":
                            expiryDate = now.AddMonths(value);
                            break;

                        default:
                            expiryDate = now.AddDays(value);
                            break;
                    }
                }

                var invoice = new Invoice
                {
                    Resident_Id = resident.Resident_Id,
                    Admin_Id = adminId,
                    Issued_By = HttpContext.Session.GetString("AdminName"),
                    Billing_Period = billingPeriod,
                    Due_Date = dueDate,
                    Description = Description ?? "",
                    Total_Amount = Rate,
                    Date_Issued = now,
                    Expiry_Date = expiryDate,
                    Status = "Unpaid"
                };

                _context.Invoice.Add(invoice);
            }

            await _context.SaveChangesAsync(); 

            await LogActivity($"Created invoice(s) for Resident {resident.FullName} " +
                              $"for {string.Join(", ", Months.Select(m => new DateTime(Year, m, 1).ToString("MMMM yyyy")))}");

            TempData["InvoiceSuccess"] = "Invoice(s) created successfully!";
            return RedirectToAction("Residents");
        }

        [HttpGet]
        public async Task<JsonResult> GetNextAdminData()
        {
            // 1. Security Check: Only Primary Admin can do this
            if (HttpContext.Session.GetString("IsPrimary") != "True")
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            // 2. Generate Username (Count admins + 1)
            int count = await _context.Admin.CountAsync();
            string nextUsername = $"Admin{(count + 1):D2}"; // Formats as Admin02, Admin03...

            // 3. Generate Random Password
            string password = Guid.NewGuid().ToString().Substring(0, 8); // 8-char random string

            return Json(new { success = true, username = nextUsername, password = password });
        }

        [HttpPost]
        public async Task<IActionResult> CreateSubAdmin( // Apply same to CreateSubAdmin
    int AdminId, string FullName, string Username, string Password,
    List<string> SelectedPermissions, // Catches Add/Create
    bool CanEditResident, bool CanEditInvoice,
    bool CanImportResident, bool CanImportInvoice, bool CanImportPayment)
        {
            // 1. Security Check
            if (HttpContext.Session.GetString("IsPrimary") != "True")
            {
                TempData["Error"] = "Only the Primary Admin can create accounts.";
                return RedirectToAction("Dashboard");
            }
            // 2. Validate Username
            if (await _context.Admin.AnyAsync(a => a.Username == Username))
            {
                TempData["Error"] = "Username already exists.";
                return RedirectToAction("Dashboard");
            }

            // 3. Handle the OLD permissions (List based)
            bool addRes = SelectedPermissions != null && SelectedPermissions.Contains("AddResident");
            bool addPay = SelectedPermissions != null && SelectedPermissions.Contains("AddPayment");
            bool addInv = SelectedPermissions != null && SelectedPermissions.Contains("CreateInvoice");
                
            // 4. Create the Admin Object
            var newAdmin = new Admin
            {
                FullName = FullName,
                Username = Username,
                Password = Password, // Consider hashing this for security!
                Email = "",
                Status = "Active",
                Is_Primary = false,
                LoginCount = 0,

                // OLD Permissions
                CanAddResident = addRes,
                CanAddPayment = addPay,
                CanCreateInvoice = addInv,

                // NEW Permissions (Direct from parameters)
                CanEditResident = CanEditResident,
                CanEditInvoice = CanEditInvoice,

                // PROFESSOR'S SECURITY RULE: Force Import to false if Add is false
                CanImportResident = addRes ? CanImportResident : false,
                CanImportInvoice = addInv ? CanImportInvoice : false,
                CanImportPayment = addPay ? CanImportPayment : false
            };

            // 5. Save to Database
            _context.Admin.Add(newAdmin);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Admin {Username} created successfully!";
            // Log the creation
            string creatorName = HttpContext.Session.GetString("AdminName") ?? "System Administrator";
            // The 'act' string will be the full sentence saved to the 'Activity' column
            await LogActivity($"{HttpContext.Session.GetString("AdminName")} created a new Admin \"{newAdmin.Username}\"");
            return RedirectToAction("Dashboard");
        }


        // 🔎 Find resident by Block + Lot + Phase
        [HttpGet]
        public async Task<IActionResult> GetResidentByAddress(string block, string lot, string phase)
        {
            if (string.IsNullOrWhiteSpace(block) ||
                string.IsNullOrWhiteSpace(lot) ||
                string.IsNullOrWhiteSpace(phase))
                return Json(null);

            var resident = await _context.ResidentInfo
                .Where(r =>
                    r.Block == block &&
                    r.Lot == lot &&
                    r.Phase_No == phase)
                .Select(r => new
                {
                    fullName = r.FullName,
                    email = r.Email,
                    residentId = r.Resident_Id
                })
                .FirstOrDefaultAsync();

            return Json(resident);
        }
       
        [HttpGet]
        public async Task<IActionResult> GetExistingInvoiceMonths(string block, string lot, string phase, int year)
        {
            var residentId = await _context.ResidentInfo
                .Where(r => r.Block == block && r.Lot == lot && r.Phase_No == phase)
                .Select(r => r.Resident_Id)
                .FirstOrDefaultAsync();

            if (residentId == 0)
                return Json(new List<int>());

            var months = await _context.Invoice
                .Where(i => i.Resident_Id == residentId && i.Billing_Period.Year == year)
                .Select(i => i.Billing_Period.Month)
                .ToListAsync();

            return Json(months);
        }

        [HttpPost]
        public async Task<IActionResult> AddPayment(
    string Block,
    string Lot,
    List<int> InvoiceNos,
    string Method,
    string DatePaid,
    string OR_No,
    string Remarks)
        {
            if (InvoiceNos == null || !InvoiceNos.Any())
            {
                TempData["PaymentError"] = "Please select at least one invoice.";
                return RedirectToAction("Residents");
            }

            var invoices = await _context.Invoice
                .Include(i => i.ResidentInfo)
                    .ThenInclude(r => r.ResidentAccount)
                .Where(i => InvoiceNos.Contains(i.Invoice_No))
                .ToListAsync();

            if (!invoices.Any())
            {
                TempData["PaymentError"] = "Selected invoices not found.";
                return RedirectToAction("Residents");
            }

            var resident = invoices.First().ResidentInfo;

            if (resident?.ResidentAccount?.Status == "Inactive")
            {
                TempData["PaymentError"] = "This resident is inactive. Payments are not allowed.";
                return RedirectToAction("Residents");
            }

            // ✅ CHECK FIRST
            if (invoices.Any(i => i.Status == "Paid"))
            {
                TempData["PaymentError"] = "One or more selected invoices are already paid.";
                return RedirectToAction("Residents");
            }

            int? adminId = HttpContext.Session.GetInt32("AdminId");
            DateTime paidDate = DateTime.Parse(DatePaid);

            // CHECK OR NUMBER UNIQUENESS
            bool orExists = await _context.Payment
                .AnyAsync(p => p.OR_No == OR_No);

            if (orExists)
            {
                TempData["PaymentError"] = $"OR Number '{OR_No}' already exists.";
                return RedirectToAction("Residents");
            }


            foreach (var inv in invoices)
            {
                var payment = new Payment
                {
                    Invoice_No = inv.Invoice_No,
                    Total_Amount = inv.Total_Amount,
                    Method = Method,
                    Admin_Id = adminId,
                    Date_Issued = paidDate,
                    OR_No = OR_No,
                    Remarks = Remarks
                };

                _context.Payment.Add(payment);
                inv.Status = "Paid";
            }

            await _context.SaveChangesAsync();

            await LogActivity($"Added payment for invoices: {string.Join(", ", InvoiceNos)} " +
                              $"for Resident {resident.FullName}");

            TempData["PaymentSuccess"] = "Payment(s) has been successfully saved.";

            return RedirectToAction("Residents");
        }
        public async Task<IActionResult> GetUnpaidInvoices(int residentId)
        {
            var invoices = await _context.Invoice
                .Where(i => i.Resident_Id == residentId && i.Status == "Unpaid")
                .OrderBy(i => i.Billing_Period)
                .Select(i => new {
                    invoiceNo = i.Invoice_No,
                    amount = i.Total_Amount,
                    month = i.Billing_Period.ToString("MMMM"),
                    year = i.Billing_Period.Year
                })
                .ToListAsync();

            return Json(invoices);
        }

        [HttpPost]
        public async Task<IActionResult> EditInvoice(
            int Invoice_No,
            decimal Total_Amount,
            string Description,
            int ResidentId
        )
        {
            var invoice = await _context.Invoice
                .FirstOrDefaultAsync(i => i.Invoice_No == Invoice_No);

            if (invoice == null)
                return NotFound();

            invoice.Total_Amount = Total_Amount;
            invoice.Description = Description;

            await _context.SaveChangesAsync();
            await LogActivity($"Edited invoice #{Invoice_No} for {ResidentId}");

            TempData["InvoiceEditSuccess"] = "Invoice updated successfully.";

            return RedirectToAction("ResidentProfile", new
            {
                id = ResidentId,
                activeTab = "invoices"
            });

        }

        [HttpPost]
        public async Task<IActionResult> DeleteInvoice(int id, int residentId)
        {
            if (HttpContext.Session.GetString("IsPrimary") != "True")
            {
                TempData["InvoiceError"] = "Access Denied: Only the Super Admin can delete invoices.";
                return RedirectToAction("ResidentProfile", new
                {
                    id = residentId,
                    activeTab = "invoices"
                });
            }

            var invoice = await _context.Invoice
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Invoice_No == id);

            if (invoice == null)
                return NotFound();

            if (invoice.Status == "Paid" || invoice.Payments.Any())
            {
                TempData["InvoiceError"] = "Paid invoices cannot be deleted.";
                return RedirectToAction("ResidentProfile", new
                {
                    id = residentId,
                    activeTab = "invoices"
                });
            }

            _context.Invoice.Remove(invoice);
            await _context.SaveChangesAsync();

            string adminName = HttpContext.Session.GetString("AdminName") ?? "System Administrator";
            await LogActivity($"{adminName} deleted Invoice #{id} for resident {residentId}");

            TempData["InvoiceDeleteSuccess"] = "Invoice deleted successfully.";
            return RedirectToAction("ResidentProfile", new
            {
                id = residentId,
                activeTab = "invoices"
            });
        }


        [HttpPost]
       public async Task<IActionResult> EditResident(
            int Resident_Id,
            string FirstName,
            string MiddleName,
            string LastName,
            string Block,
            string Lot,
            int Phase_No,
            int Year_Of_Residency,
            string Status,
            string Email,
            string ContactNo)
        {
            var resident = await _context.ResidentInfo
                .Include(r => r.ResidentAccount)
                .FirstOrDefaultAsync(r => r.Resident_Id == Resident_Id);

            if (resident == null)
                return NotFound();

            Status = (Status ?? "").Trim();
            if (Status != "Active" && Status != "Inactive")
                Status = "Active";

            // DUPLICATE CHECK
            bool duplicateExists = await _context.ResidentInfo.AnyAsync(r =>
                r.Resident_Id != Resident_Id &&
                r.Block == Block &&
                r.Lot == Lot &&
                r.Phase_No == Phase_No.ToString()
            );

            if (duplicateExists)
            {
                TempData["ResidentErrorAlert2"] =
                    $"A resident already exists at Block {Block}, Lot {Lot}, Phase {Phase_No}.";
                return RedirectToAction("Residents");
            }

            // UPDATE NAME
            resident.FullName = string.Join(" ",
                new[] { LastName + ",", FirstName, MiddleName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            resident.Block = Block;
            resident.Lot = Lot;
            resident.Phase_No = Phase_No.ToString();
            resident.Year_Of_Residency = Year_Of_Residency;
            resident.Email = Email;
            resident.Contact_No = ContactNo;

            if (resident.ResidentAccount != null)
                resident.ResidentAccount.Status = Status;

            await _context.SaveChangesAsync();
            await LogActivity($"Edited resident: {resident.FullName}");

            TempData["ResidentEditSuccess"] = "true";
            return RedirectToAction("Residents");
        }

        public async Task<IActionResult> ToggleResidentStatus(int id)
        {
            var resident = await _context.ResidentInfo
                .Include(r => r.ResidentAccount)
                .FirstOrDefaultAsync(r => r.Resident_Id == id);

            if (resident == null)
                return NotFound();

            if (resident.ResidentAccount == null)
                return BadRequest("Resident account not found.");

            // Toggle status
            resident.ResidentAccount.Status =
                resident.ResidentAccount.Status == "Inactive"
                ? "Active"
                : "Inactive";

            await _context.SaveChangesAsync();
            await LogActivity($"Deactivated resident: {resident.FullName}");

            TempData["ResidentStatusSuccess"] =
                resident.ResidentAccount.Status == "Active"
                ? "Resident account has been enabled."
                : "Resident account has been disabled.";

            return RedirectToAction("Residents");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateResidentEmail(int Resident_Id, string NewEmail)
        {
            var r = await _context.ResidentInfo.FindAsync(Resident_Id);
            if (r != null)
            {
                r.Email = NewEmail;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("Residents");
        }

        //FINANCIAL REPORTS & PUPPETEER
        private FinancialReportViewModel BuildFinancialReportVM(int month, int year)
        {
            // ================= NORMALIZE INPUT =================
            month = Math.Clamp(month, 1, 12);
            year = Math.Clamp(year, 2017, DateTime.Now.Year);

            DateTime monthStart = new DateTime(year, month, 1);
            DateTime monthEnd = monthStart.AddMonths(1);

            DateTime yearStart = new DateTime(year, 1, 1);
            DateTime yearEnd = yearStart.AddYears(1);

            // ================= VALID RESIDENTS =================
            var validResidents = _context.ResidentInfo
                .AsNoTracking()
                .Where(r => r.Year_Of_Residency <= year)
                .Select(r => r.Resident_Id)
                .ToList();

            // ================= PRELOAD DATA (YEAR SCOPE) =================
            var targets = _context.CollectionTarget
                .AsNoTracking()
                .Where(t => t.Year == year)
                .ToList();

            var expenses = _context.Expense
                .AsNoTracking()
                .Where(e => e.Expense_Year == year)
                .ToList();

            var invoices = _context.Invoice
                .AsNoTracking()
                .Where(i => validResidents.Contains(i.Resident_Id))
                .ToList();

            var validInvoiceNumbers = invoices
                .Select(i => i.Invoice_No)
                .ToHashSet();

            var payments = _context.Payment
    .AsNoTracking()
    .Where(p => validInvoiceNumbers.Contains(p.Invoice_No))
    .ToList();

            var invoicesUpToYear = _context.Invoice
    .AsNoTracking()
    .Where(i => validResidents.Contains(i.Resident_Id))
    .Where(i => i.Billing_Period < yearEnd)
    .ToList();
            // ============================================================
            // ================= MONTHLY SUMMARY CARDS ====================
            // ============================================================

            decimal target = targets
    .Where(t => t.Month == month)
    .OrderByDescending(t => t.created_at)
    .Select(t => t.Target_Amount)
    .FirstOrDefault();

            decimal collected = payments
    .Where(p => p.Date_Issued.Year == year &&
                p.Date_Issued.Month == month)
    .Sum(p => p.Total_Amount);

            decimal totalExpenses = expenses
                .Where(e => e.Expense_Month == month)
                .Sum(e => e.Total);

            decimal netCollected = collected - totalExpenses;

            // DEFICIT (positive number when net is negative)
            decimal deficit = netCollected < 0 ? Math.Abs(netCollected) : 0;

            // REMAINING 
            decimal remainingToTarget = Math.Max(0, target - collected);

            // RATE must be based on GROSS, not NET
            double rate = target > 0
                ? Math.Round((double)(collected / target * 100), 2)
                : 0;
            rate = Math.Min(100.0, rate);

            decimal previousTarget = _context.CollectionTarget
                .AsNoTracking()
                .Where(t => t.Year < year || (t.Year == year && t.Month < month))
                .OrderByDescending(t => t.Year)
                .ThenByDescending(t => t.Month)
                .Select(t => (decimal?)t.Target_Amount)
                .FirstOrDefault() ?? 0;

            // ============================================================
            // ================= MONTHLY CHART =============================
            // ============================================================

            var monthlyChart = new List<MonthlyChartData>();

            for (int m = 1; m <= 12; m++)
            {
                decimal mGrossCollected = payments
                    .Where(p => p.Date_Issued.Year == year &&
                                p.Date_Issued.Month == m)
                    .Sum(p => p.Total_Amount);

                decimal mExpenses = expenses
                    .Where(e => e.Expense_Month == m &&
                                e.Expense_Year == year) // MUST MATCH 2026
                    .Sum(e => e.Total);

                decimal mNetCollected = Math.Max(0, mGrossCollected - mExpenses);

                monthlyChart.Add(new MonthlyChartData
                {
                    Month = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m),
                    Collection = mNetCollected,
                    Target = targets
                        .Where(t => t.Month == m)
                        .Select(t => t.Target_Amount)
                        .FirstOrDefault()
                });
            }

            // ============================================================
            // ================= YEARLY SECTION ============================
            // ============================================================

            int startYear = 2017;
            int endYear = year;

            var yearlyChart = new List<YearlyChartData>();

            decimal yearlyTarget = targets.Sum(t => t.Target_Amount);

            decimal yearlyGrossCollected = payments.Sum(p => p.Total_Amount);

            decimal yearlyNetCollected = yearlyGrossCollected - expenses.Sum(e => e.Total);

            double yearlyRate = yearlyTarget > 0
                ? Math.Round((double)(yearlyGrossCollected / yearlyTarget * 100), 2)
                : 0;

            yearlyRate = Math.Min(100.0, yearlyRate);

            // ===== YEARLY CHART (NET per year is OK)
            for (int y = startYear; y <= endYear; y++)
            {
                decimal yPayments = _context.Payment
                    .AsNoTracking()
                    .Where(p => p.Date_Issued.Year == y)
                    .Where(p => _context.Invoice
                        .Where(i => validResidents.Contains(i.Resident_Id))
                        .Select(i => i.Invoice_No)
                        .Contains(p.Invoice_No))
                    .Sum(p => (decimal?)p.Total_Amount) ?? 0m;

                decimal yExpenses = _context.Expense
                    .AsNoTracking()
                    .Where(e => e.Expense_Year == y)
                    .Sum(e => (decimal?)e.Total) ?? 0m;

                yearlyChart.Add(new YearlyChartData
                {
                    Year = y,
                    // NET per year (can be negative if you want)
                    Collection = yPayments - yExpenses
                });
            }


            // ============================================================
            // ================= DELINQUENCY (UNCHANGED) ==================
            // ============================================================

            DateTime delinquentCutoff = monthStart.AddMonths(-3);

            var invoicesUpToMonth = invoices
                .Where(i => i.Billing_Period < monthEnd)
                .ToList();

            int delinquentResidents = invoicesUpToMonth
                .GroupBy(i => i.Resident_Id)
                .Count(g =>
                    g.Any(i =>
                        i.Status == "Delinquent" ||
                        (i.Status == "Unpaid" && i.Billing_Period < delinquentCutoff)
                    )
                );

            int updatedResidents = invoicesUpToMonth
                .GroupBy(i => i.Resident_Id)
                .Count(g =>
                    !g.Any(i =>
                        i.Status == "Delinquent" ||
                        (i.Status == "Unpaid" && i.Billing_Period < delinquentCutoff)
                    )
                );
            int yearlyDelinquentResidents = invoicesUpToYear
    .GroupBy(i => i.Resident_Id)
    .Count(g => g.Any(i =>
        i.Status == "Delinquent" ||
        (i.Status == "Unpaid" && i.Billing_Period < delinquentCutoff)
    ));

            // Count updated residents
            int yearlyUpdatedResidents = invoicesUpToYear
                .GroupBy(i => i.Resident_Id)
                .Count(g => !g.Any(i =>
                    i.Status == "Delinquent" ||
                    (i.Status == "Unpaid" && i.Billing_Period < delinquentCutoff)
                ));
            // ============================================================
            // ================= RETURN VIEWMODEL ==========================
            // ============================================================

            return new FinancialReportViewModel
            {
                Month = month,
                Year = year,

                MonthlyTarget = target,
                PreviousTarget = previousTarget,

                MonthlyGrossCollection = collected,
                MonthlyExpenses = totalExpenses,
                MonthlyNetCollection = netCollected,
                MonthlyDeficit = deficit,
                MonthlyRemainingToTarget = remainingToTarget,
                MonthlyRate = (decimal)rate,

                UpdatedResidents = updatedResidents,
                DelinquentResidents = delinquentResidents,

                YearlyTarget = yearlyTarget,
                YearlyGrossCollection = yearlyGrossCollected,
                YearlyNetCollection = yearlyNetCollected,
                YearlyRate = (decimal)yearlyRate,

                MonthlyChart = monthlyChart,
                YearlyChart = yearlyChart,

                YearlyUpdatedResidents = yearlyUpdatedResidents,
                YearlyDelinquentResidents = yearlyDelinquentResidents
            };
        }

        // ================= MAIN PAGE =================
        public IActionResult FinancialReport(int? month, int? year, int? historyMonth, int? historyYear)
        {
            int selectedYear = Math.Clamp(year ?? DateTime.Now.Year, 2017, DateTime.Now.Year);
            int selectedMonth = Math.Clamp(month ?? DateTime.Now.Month, 1, 12);

            var vm = BuildFinancialReportVM(selectedMonth, selectedYear);

            vm.Years = Enumerable.Range(2017, DateTime.Now.Year - 2017 + 1)
                                 .OrderByDescending(y => y)
                                 .ToList();

            vm.TargetHistory = GetTargetHistory(
                month: historyMonth == 0 ? null : historyMonth,
                year: historyYear == 0 ? null : historyYear
            );

            ViewBag.HistoryMonth = historyMonth ?? 0;
            ViewBag.HistoryYear = historyYear ?? 0;

            return View(vm);
        }

        private List<TargetHistoryVM> GetTargetHistory(int? month, int? year)
        {
            var query = _context.CollectionTarget.AsQueryable();

            if (year.HasValue && year != 0)
                query = query.Where(x => x.Year == year);

            if (month.HasValue && month != 0)
                query = query.Where(x => x.Month == month);

            return query
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.Month)
                .ThenByDescending(x => x.created_at)
                .Select(x => new TargetHistoryVM
                {
                    Year = x.Year,
                    Month = x.Month,
                    MonthName = new DateTime(x.Year, x.Month, 1).ToString("MMMM"),
                    Amount = x.Target_Amount
                })
                .ToList();
        }


        public IActionResult Expense(string? search, int? month, int? year)
        {
            var expenseQuery = _context.Expense.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLower();
                expenseQuery = expenseQuery.Where(e =>
                    e.Expense_Type.ToLower().Contains(search) ||
                    (e.Voucher_No != null && e.Voucher_No.ToLower().Contains(search)));
            }

            if (year.HasValue && year > 0)
                expenseQuery = expenseQuery.Where(e => e.Expense_Date.Year == year);

            if (month.HasValue && month > 0)
                expenseQuery = expenseQuery.Where(e => e.Expense_Date.Month == month);

            var vm = new ExpenseListViewModel
            {
                Expenses = expenseQuery
                    .OrderByDescending(e => e.Expense_Date)
                    .ToList(),

                Month = month ?? 0,
                Year = year ?? 0
            };

            return View(vm);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditExpense(Expense model)
        {
            if (!ModelState.IsValid)
            {
                return RedirectToAction("Expense");
            }

            var expense = _context.Expense.Find(model.Expense_Id);
            if (expense == null)
                return NotFound();

            expense.Voucher_No = model.Voucher_No;
            expense.Expense_Type = model.Expense_Type;
            expense.Expense_Date = model.Expense_Date;
            expense.Total = model.Total;

            _context.SaveChanges();

            TempData["ExpenseEditSuccess"] = true;
            return RedirectToAction("Expense"); 
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteExpense(int id)
        {
            var expense = _context.Expense.Find(id);
            if (expense != null)
            {
                _context.Expense.Remove(expense);
                _context.SaveChanges();
            }

            TempData["ExpenseDeleteSuccess"] = true;
            return RedirectToAction("Expense");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddExpense(ExpenseListViewModel model)
        {
            var vm = model.NewExpense;

            // Total validation
            if (vm.Total <= 0)
                ModelState.AddModelError("NewExpense.Total", "Amount must be greater than 0.");

            // Prevent duplicate voucher number
            if (!string.IsNullOrWhiteSpace(vm.Voucher_No))
            {
                bool exists = _context.Expense.Any(e => e.Voucher_No == vm.Voucher_No);
                if (exists)
                {
                    ModelState.AddModelError("NewExpense.Voucher_No", "Voucher number already exists.");
                    TempData["VoucherExists"] = "This voucher number already exists. Please use a different one.";
                }
            }

            if (!ModelState.IsValid)
            {
                var expenses = _context.Expense.OrderByDescending(e => e.Expense_Date).ToList();
                var listVm = new ExpenseListViewModel
                {
                    Expenses = expenses,
                    Year = DateTime.Now.Year,
                    Month = DateTime.Now.Month,
                    NewExpense = vm
                };

                TempData["ShowExpense"] = true; 
                return View("Expense", listVm);
            }

            // Auto-fill system fields
            vm.Created_At = DateTime.Now;
            vm.Expense_Year = (short)vm.Expense_Date.Year;
            vm.Expense_Month = (byte)vm.Expense_Date.Month;
            vm.Expense_Day = (byte)vm.Expense_Date.Day;


            _context.Expense.Add(vm);
            _context.SaveChanges();
            TempData["AddExpenseSuccess"] = "Expense added successfully.";

            return RedirectToAction("Expense", new { year = vm.Expense_Year, month = vm.Expense_Month });
        }

        public async Task<IActionResult> ImportExpenseCsv(IFormFile csvFile)
        {
            List<string> errorMessages = new();
            List<string> successMessages = new();

            int rowNumber = 1;
            int? adminId = HttpContext.Session.GetInt32("AdminId");

            if (adminId == null)
            {
                TempData["ImportErrors"] = System.Text.Json.JsonSerializer.Serialize(
                    new List<string> { "Session expired. Please log in again." });

                return RedirectToAction("Login", "Account");
            }

            if (csvFile == null || csvFile.Length == 0)
            {
                TempData["ImportError"] = "Please upload a valid CSV file.";
                return RedirectToAction("Expense");
            }

            using var reader = new StreamReader(csvFile.OpenReadStream());
            using var csv = new CsvHelper.CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture);

            var records = csv.GetRecords<ExpenseCsvModel>().ToList();

            int success = 0;
            int failed = 0;

            foreach (var e in records)
            {
                rowNumber++;

                // VALIDATION 
                if (string.IsNullOrWhiteSpace(e.Voucher_No) ||
                    string.IsNullOrWhiteSpace(e.Expense_Type) ||
                    !e.Expense_Month.HasValue ||
                    e.Expense_Month < 1 || e.Expense_Month > 12 ||
                    !e.Expense_Year.HasValue ||
                    e.Expense_Year < 1950 || e.Expense_Year > DateTime.Now.Year ||
                    !e.Expense_Date.HasValue ||
                    !e.Total.HasValue || e.Total <= 0)
                {
                    failed++;
                    errorMessages.Add($"Row {rowNumber}: Missing or invalid required fields.");
                    continue;
                }

                // Prevent duplicate voucher number
                bool voucherExists = await _context.Expense
                    .AnyAsync(x => x.Voucher_No == e.Voucher_No);

                if (voucherExists)
                {
                    failed++;
                    errorMessages.Add($"Row {rowNumber}: Voucher No {e.Voucher_No} already exists.");
                    continue;
                }

                try
                {
                    var expense = new Expense
                    {
                        Voucher_No = e.Voucher_No,
                        Expense_Type = e.Expense_Type,
                        Expense_Month = e.Expense_Month.Value,
                        Expense_Year = e.Expense_Year.Value,
                        Expense_Day = (byte)e.Expense_Date.Value.Day,
                        Expense_Date = e.Expense_Date.Value,
                        Total = e.Total.Value,
                        Admin_Id = adminId,
                        Created_At = DateTime.Now
                    };

                    _context.Expense.Add(expense);

                    success++;
                    successMessages.Add($"Row {rowNumber}: Expense '{e.Expense_Type}' imported.");
                }
                catch (Exception ex)
                {
                    failed++;
                    errorMessages.Add($"Row {rowNumber}: System error - {ex.Message}");
                }
            }

            await _context.SaveChangesAsync();

            TempData["ImportResult"] = $"Imported: {success}, Failed: {failed}";
            TempData["ImportErrors"] = System.Text.Json.JsonSerializer.Serialize(errorMessages);
            TempData["ImportSuccessDetails"] = System.Text.Json.JsonSerializer.Serialize(successMessages);

            return RedirectToAction("Expense");
        }



        // ================= UPDATE TARGET =================
        [HttpPost]
        public IActionResult UpdateTarget(int month, int year, decimal targetAmount)
        {
            var existingTarget = _context.CollectionTarget
                .FirstOrDefault(t => t.Month == month && t.Year == year);

            if (existingTarget != null)
            {
                // ✅ UPDATE
                existingTarget.Target_Amount = targetAmount;
                existingTarget.created_at = DateTime.Now;

                _context.CollectionTarget.Update(existingTarget);
            }
            else
            {
                // ✅ INSERT (first time only)
                var target = new CollectionTarget
                {
                    Month = month,
                    Year = year,
                    Target_Amount = targetAmount,
                    created_at = DateTime.Now
                };

                _context.CollectionTarget.Add(target);
            }

            _context.SaveChanges();

            return RedirectToAction(nameof(FinancialReport), new { month, year });
        }




        // ================= GENERATE PDF =================
        private async Task<FileResult> GeneratePdfFromUrl(string url, string fileName)
        {
            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                Args = new[] { "--no-sandbox" }
            });

            using var page = await browser.NewPageAsync();

            await page.SetViewportAsync(new ViewPortOptions
            {
                Width = 1600,
                Height = 1200,
                DeviceScaleFactor = 2
            });

            await page.GoToAsync(url, WaitUntilNavigation.Networkidle0);
            await page.WaitForSelectorAsync("canvas");
            await page.EvaluateExpressionAsync(@"
    for (let id in Chart.instances) {
        Chart.instances[id].resize();
        Chart.instances[id].update('none');
    }
");
            await Task.Delay(1000);

            var pdf = await page.PdfStreamAsync(new PdfOptions
            {
                Format = PaperFormat.A4,
                Landscape = true,
                PrintBackground = true,
                DisplayHeaderFooter = true,
                HeaderTemplate = "<div style='height:0;'></div>",
                FooterTemplate = @"<div style='width:100%; font-size:10px; padding:0 20px;'> 
        <span style='float:left;'>Carlton Residences HOA Financial Report</span> 
        <span style='float:right;'>Page <span class='pageNumber'></span> of <span class='totalPages'></span></span> 
        </div>",
                MarginOptions = new MarginOptions
                {
                    Top = "15mm",
                    Bottom = "20mm",
                    Left = "10mm",
                    Right = "10mm"
                },
                PreferCSSPageSize = true,
            });
            return File(pdf, "application/pdf", fileName);
        }

        public async Task<IActionResult> GenerateMonthlyPdf(int month, int year)
        {
            var url = Url.Action("FinancialReport", "Admin",
                new { month, year, tab = "monthly", isPdf = true }, Request.Scheme);

            return await GeneratePdfFromUrl(url, $"MonthlyReport_{year}_{month:D2}.pdf");
        }

        public async Task<IActionResult> GenerateYearlyPdf(int year)
        {
            var url = Url.Action("FinancialReport", "Admin",
                new { year, tab = "yearly", isPdf = true }, Request.Scheme);

            return await GeneratePdfFromUrl(url, $"YearlyReport_{year}.pdf");
        }



        // OTP, Logs, Account Mgmt

        [HttpPost]
        public async Task<IActionResult> AdminSendSetupOtp(string Email)
        {
            int? adminId = HttpContext.Session.GetInt32("PendingAdminId");
            if (adminId == null) return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(Email))
            {
                ViewBag.SetupError = "Email is required.";
                ViewBag.ShowSetupEmailModal = true;
                return View("Login");
            }

            Random rand = new Random();
            string otp = rand.Next(0, 10000).ToString("D4");
            string subject = "Verify Admin Email - Carlton Residences";
            string body = $"Your verification code is: {otp}";

            bool sent = await SendEmailInternal(Email, subject, body);

            if (!sent)
            {
                ViewBag.SetupError = "Could not send email.";
                ViewBag.ShowSetupEmailModal = true;
                return View("Login");
            }

            HttpContext.Session.SetString("SetupEmailAddress", Email);
            HttpContext.Session.SetString("SetupEmailOTP", otp);
            ViewBag.ShowVerifySetupModal = true;
            return View("Login");
        }

        [HttpPost]
        public async Task<IActionResult> AdminVerifySetupOtp(string Code)
        {
            string correctOtp = HttpContext.Session.GetString("SetupEmailOTP");
            string email = HttpContext.Session.GetString("SetupEmailAddress");
            int? adminId = HttpContext.Session.GetInt32("PendingAdminId");

            if (!string.IsNullOrEmpty(correctOtp) && Code == correctOtp && adminId != null)
            {
                var admin = await _context.Admin.FindAsync(adminId);
                if (admin != null)
                {
                    admin.Email = email;
                    await _context.SaveChangesAsync();
                    HttpContext.Session.Remove("PendingAdminId");
                    HttpContext.Session.Remove("SetupEmailOTP");
                    HttpContext.Session.Remove("SetupEmailAddress");
                    return await PerformLogin(admin);
                }
            }

            ViewBag.VerifySetupError = "Invalid Code.";
            ViewBag.ShowVerifySetupModal = true;
            return View("Login");
        }

        [HttpPost]
        public async Task<IActionResult> AdminVerifyLoginOtp(string Code)
        {
            string correctOtp = HttpContext.Session.GetString("AdminTempOTP");
            int? adminId = HttpContext.Session.GetInt32("TempAdminId");

            if (!string.IsNullOrEmpty(correctOtp) && Code == correctOtp && adminId != null)
            {
                var admin = await _context.Admin.FindAsync(adminId);
                admin.LastOtpVerification = DateTime.Now;
                await _context.SaveChangesAsync();
                HttpContext.Session.Remove("AdminTempOTP");
                HttpContext.Session.Remove("TempAdminId");
                return await PerformLogin(admin);
            }

            ViewBag.VerifyLoginError = "Invalid OTP.";
            ViewBag.ShowVerifyLoginModal = true;
            return View("Login");
        }

        [HttpPost]
        public async Task<JsonResult> SendOtp(string username)
        {
            string emailToSend = null;
            string userType = null;

            var admin = await _context.Admin.FirstOrDefaultAsync(a => a.Username == username);
            if (admin != null && !string.IsNullOrWhiteSpace(admin.Email))
            {
                emailToSend = admin.Email;
                userType = "Admin";
            }
            else
            {
                var resident = await _context.ResidentAccount.Include(ra => ra.ResidentInfo).FirstOrDefaultAsync(ra => ra.Username == username);
                if (resident != null && resident.ResidentInfo != null && !string.IsNullOrWhiteSpace(resident.ResidentInfo.Email))
                {
                    emailToSend = resident.ResidentInfo.Email;
                    userType = "Resident";
                }
            }

            if (string.IsNullOrEmpty(emailToSend))
                return Json(new { success = false, message = "Username not found or no email linked." });

            Random generator = new Random();
            string otp = generator.Next(0, 10000).ToString("D4");

            HttpContext.Session.SetString("ResetOTP", otp);
            HttpContext.Session.SetString("ResetUsername", username);
            HttpContext.Session.SetString("ResetUserType", userType);

            string subject = "Password Reset Code - Carlton Residences";
            string body = $"Your verification code is: {otp}";

            bool sent = await SendEmailInternal(emailToSend, subject, body);
            return sent ? Json(new { success = true }) : Json(new { success = false, message = "System could not send the email." });
        }

        [HttpPost]
        public JsonResult VerifyResetOtp(string code)
        {
            var sessionOtp = HttpContext.Session.GetString("ResetOTP");
            return (!string.IsNullOrEmpty(sessionOtp) && sessionOtp == code)
                ? Json(new { success = true })
                : Json(new { success = false, message = "Invalid Code." });
        }

        [HttpPost]
        public async Task<JsonResult> ResetPassword(string newPassword)
        {
            var targetUsername = HttpContext.Session.GetString("ResetUsername");
            var targetType = HttpContext.Session.GetString("ResetUserType");

            if (string.IsNullOrEmpty(targetUsername)) return Json(new { success = false, message = "Session expired." });

            if (targetType == "Admin")
            {
                var admin = await _context.Admin.FirstOrDefaultAsync(u => u.Username == targetUsername);
                if (admin != null) { admin.Password = newPassword; await _context.SaveChangesAsync(); }
            }
            else
            {
                var resident = await _context.ResidentAccount.FirstOrDefaultAsync(u => u.Username == targetUsername);
                if (resident != null) { resident.Password = newPassword; await _context.SaveChangesAsync(); }
            }

            HttpContext.Session.Remove("ResetOTP");
            HttpContext.Session.Remove("ResetUsername");
            HttpContext.Session.Remove("ResetUserType");
            return Json(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> ActivityLogs()
        {
            var logs = await _context.AdminLog
                .Include(l => l.Admin)
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();

            return View(logs);
        }


        [HttpGet] public IActionResult VerifyOTP() => HttpContext.Session.GetString("TempOTP") == null ? RedirectToAction("Login") : View();

        [HttpPost]
        public async Task<IActionResult> VerifyOTP(string Code)
        {
            if (Code == HttpContext.Session.GetString("TempOTP"))
            {
                var rid = HttpContext.Session.GetInt32("TempResidentId").Value;
                var r = await _context.ResidentAccount.FindAsync(rid);

                r.LoginCount++;
                r.LastOtpVerification = DateTime.Now;
                await _context.SaveChangesAsync();

                HttpContext.Session.SetInt32("ResidentId", rid);
                return RedirectToAction("Index", "Resident");
            }

            ViewBag.Error = "Invalid Code";
            return View();
        }


        [HttpGet] public async Task<IActionResult> Profile() { var id = HttpContext.Session.GetInt32("AdminId"); return id == null ? RedirectToAction("Login") : View(await _context.Admin.FindAsync(id)); }
        [HttpPost] public async Task<IActionResult> ChangeAdminPassword(string CurrentPassword, string NewPassword, string ConfirmPassword) { var a = await _context.Admin.FindAsync(HttpContext.Session.GetInt32("AdminId")); if (a.Password == CurrentPassword && NewPassword == ConfirmPassword) { a.Password = NewPassword; await _context.SaveChangesAsync(); TempData["ProfileSuccess"] = "Updated"; } return RedirectToAction("Profile"); }

        // HELPER: Send Email
        private async Task<bool> SendEmailInternal(string to, string subject, string body)
        {
            try
            {
                using (var s = new SmtpClient("smtp.gmail.com", 587) { Credentials = new NetworkCredential("itsarathearmy@gmail.com", "fmks zcrr azjk pmfx"), EnableSsl = true })
                    await s.SendMailAsync(new MailMessage("itsarathearmy@gmail.com", to, subject, body));
                return true;
            }
            catch { return false; }
        }

        private async Task LogActivity(string act)
        {
            try
            {
                int? id = HttpContext.Session.GetInt32("AdminId");
                if (id != null)
                {
                    var log = new AdminLog
                    {
                        Admin_Id = id,
                        Activity = act,
                        Timestamp = DateTime.Now
                    };
                    _context.AdminLog.Add(log);
                    _context.SaveChanges();
                }
            }
            catch (Exception)
            {
                
            }
        }


        // iTextSharp PDF GENERATION 
        public async Task<IActionResult> GeneratePaymentPDF(int id, int? payMonth, int? payYear)
        {
            var resident = await _context.ResidentInfo
                .Include(r => r.Invoices).ThenInclude(i => i.Payments)
                .Include(r => r.Invoices).ThenInclude(i => i.Admin)
                .FirstOrDefaultAsync(r => r.Resident_Id == id);

            if (resident == null) return NotFound();

            var paymentsQuery = resident.Invoices
    .SelectMany(i => i.Payments)
    .AsQueryable();

            if (payMonth != null)
                paymentsQuery = paymentsQuery
                    .Where(p => p.Invoice.Billing_Period.Month == payMonth);

            if (payYear != null)
                paymentsQuery = paymentsQuery
                    .Where(p => p.Invoice.Billing_Period.Year == payYear);

            var payments = paymentsQuery
                .OrderByDescending(p => p.Date_Issued)
                .ToList();



            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4.Rotate(), 40f, 40f, 35f, 40f);
                PdfWriter writer = PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont = FontFactory.GetFont(FontFactory.TIMES_BOLD, 20);
                var sectionFont = FontFactory.GetFont(FontFactory.TIMES_BOLD, 12);
                var normalFont = FontFactory.GetFont(FontFactory.TIMES, 11);
                var tableHeader = FontFactory.GetFont(FontFactory.TIMES_BOLD, 10);
                var tableText = FontFactory.GetFont(FontFactory.TIMES, 10);
                var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "NewFolder", "carlton-logo.jpg");
                Image logo = Image.GetInstance(logoPath);
                logo.ScaleToFit(60f, 60f);  
                logo.Alignment = Element.ALIGN_LEFT;


                // HEADER
                PdfPTable header = new PdfPTable(2) { WidthPercentage = 100 };
                header.SetWidths(new float[] { 60f, 40f });

                PdfPTable left = new PdfPTable(1);
                left.DefaultCell.Border = Rectangle.NO_BORDER;
                left.AddCell(new Phrase("Payment History", titleFont));
                left.AddCell(new Phrase($"Date: {DateTime.Now:MMMM dd, yyyy}", normalFont));
                header.AddCell(new PdfPCell(left) { Border = Rectangle.NO_BORDER });

                PdfPTable right = new PdfPTable(2);
                right.SetWidths(new float[] { 20f, 80f });
                right.WidthPercentage = 100;
                right.DefaultCell.Border = Rectangle.NO_BORDER;

                // LOGO CELL
                PdfPCell logoCell = new PdfPCell(logo);
                logoCell.Border = Rectangle.NO_BORDER;
                logoCell.VerticalAlignment = Element.ALIGN_TOP;
                logoCell.HorizontalAlignment = Element.ALIGN_LEFT;
                right.AddCell(logoCell);

                // ADDRESS CELL
                PdfPTable address = new PdfPTable(1);
                address.DefaultCell.Border = Rectangle.NO_BORDER;
                address.AddCell(new Phrase("Carlton Residence Home Owner's Association", sectionFont));
                address.AddCell(new Phrase("B44 L1 Ph 1 Carlton Residences,", normalFont));
                address.AddCell(new Phrase("Barangay Dita, City of Santa Rosa,", normalFont));
                address.AddCell(new Phrase("Laguna, Philippines", normalFont));

                PdfPCell addressCell = new PdfPCell(address);
                addressCell.Border = Rectangle.NO_BORDER;
                right.AddCell(addressCell);

                header.AddCell(new PdfPCell(right) { Border = Rectangle.NO_BORDER });


                doc.Add(header);
                doc.Add(new Paragraph("\n"));

                // RESIDENT INFO
                PdfPTable info = new PdfPTable(1) { WidthPercentage = 100 };
                info.DefaultCell.Border = Rectangle.NO_BORDER;
                info.AddCell(new Phrase($"Resident Name: {resident.FullName}", normalFont));
                info.AddCell(new Phrase($"Contact No.: {resident.Contact_No}", normalFont));
                info.AddCell(new Phrase($"E-mail: {resident.Email}", normalFont));
                info.AddCell(new Phrase($"Address: Block {resident.Block}, Lot {resident.Lot}, Phase {resident.Phase_No}, Carlton Residences, Santa Rosa, Laguna", normalFont));
                doc.Add(info);
                doc.Add(new Paragraph("\n"));

                // TABLE
                PdfPTable table = new PdfPTable(8);
                table.WidthPercentage = 100;
                table.SetWidths(new float[] { 10f, 15f, 10f, 12f, 12f, 10f, 14f, 10f });

                string[] headers = { "Invoice No.", "Billing Period", "Receipt No.", "Date Paid", "Amount", "Method", "Remarks", "Issued By" };
                foreach (var h in headers)
                {
                    PdfPCell cell = new PdfPCell(new Phrase(h, tableHeader));
                    cell.HorizontalAlignment = Element.ALIGN_CENTER;
                    cell.BackgroundColor = new BaseColor(230, 230, 230);
                    cell.Padding = 6;
                    table.AddCell(cell);
                }
                decimal totalReceived = 0;
                foreach (var p in payments)
                {
                    totalReceived += p.Total_Amount;
                    table.AddCell(new Phrase($"INV-{p.Invoice_No:00000}", tableText));
                    table.AddCell(new Phrase(p.Invoice.Billing_Period.ToString("MMMM yyyy").ToUpper(), tableText));
                    table.AddCell(new Phrase($"{p.OR_No}", tableText));
                    table.AddCell(new Phrase(p.Date_Issued.ToString("MMMM d, yyyy"), tableText));
                    table.AddCell(new Phrase($"PHP {p.Total_Amount:N2}", tableText));
                    table.AddCell(new Phrase(p.Method.ToUpper(), tableText));
                    table.AddCell(new Phrase(p.Remarks ?? "", tableText));
                    table.AddCell(new Phrase(p.Admin?.FullName ?? "", tableText));
                }
                doc.Add(table);

                // TOTAL RECEIVED
                doc.Add(new Paragraph("\n"));
                PdfPTable total = new PdfPTable(1) { WidthPercentage = 100 };

                PdfPCell tcell = new PdfPCell(
                    new Phrase($"Total Amount Received: PHP {totalReceived:N2}", tableHeader))
                {
                    Border = Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT,
                    PaddingTop = 5
                };

                total.AddCell(tcell);
                doc.Add(total);

                // FOOTER
                doc.Add(new Paragraph("\n\n"));
                PdfPTable footer = new PdfPTable(2);
                footer.WidthPercentage = 100;
                PdfPCell prep = new PdfPCell(new Phrase($"Prepared By: {HttpContext.Session.GetString("AdminName") ?? "Admin"}", normalFont)) { Border = Rectangle.NO_BORDER };
                PdfPCell app = new PdfPCell(new Phrase("Approved By:", normalFont)) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT };
                footer.AddCell(prep);
                footer.AddCell(app);
                doc.Add(footer);

                doc.Close();
                var lastName = resident.FullName.Split(' ').First();
                return File(ms.ToArray(), "application/pdf", $"{lastName}_PaymentHistory.pdf");
            }
        }

        public async Task<IActionResult> GenerateSOAPDF(
        int id,
        int? soaMonth,
        int? soaYear)
        {
            var resident = await _context.ResidentInfo
                .Include(r => r.Invoices).ThenInclude(i => i.Payments)
                .Include(r => r.Invoices).ThenInclude(i => i.Admin)
                .FirstOrDefaultAsync(r => r.Resident_Id == id);

            if (resident == null) return NotFound();

            var invoicesQuery = resident.Invoices.AsQueryable();

            if (soaMonth != null)
                invoicesQuery = invoicesQuery.Where(i => i.Billing_Period.Month == soaMonth);

            if (soaYear != null)
                invoicesQuery = invoicesQuery.Where(i => i.Billing_Period.Year == soaYear);

            var invoices = invoicesQuery
                .OrderBy(i => i.Billing_Period)
                .ToList();


            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4.Rotate(), 40f, 40f, 35f, 40f);
                PdfWriter writer = PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont = FontFactory.GetFont(FontFactory.TIMES_BOLD, 20);
                var sectionFont = FontFactory.GetFont(FontFactory.TIMES_BOLD, 12);
                var normalFont = FontFactory.GetFont(FontFactory.TIMES, 11);
                var tableHeader = FontFactory.GetFont(FontFactory.TIMES_BOLD, 10);
                var tableText = FontFactory.GetFont(FontFactory.TIMES, 10);
                var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "NewFolder", "carlton-logo.jpg");
                Image logo = Image.GetInstance(logoPath);
                logo.ScaleToFit(60f, 60f);
                logo.Alignment = Element.ALIGN_LEFT;

                // HEADER
                PdfPTable header = new PdfPTable(2) { WidthPercentage = 100 };
                header.SetWidths(new float[] { 60f, 40f });

                PdfPTable left = new PdfPTable(1);
                left.DefaultCell.Border = Rectangle.NO_BORDER;
                left.AddCell(new Phrase("Statement of Account", titleFont));
                left.AddCell(new Phrase($"Date: {DateTime.Now:MMMM dd, yyyy}", normalFont));
                header.AddCell(new PdfPCell(left) { Border = Rectangle.NO_BORDER });

                PdfPTable right = new PdfPTable(2);
                right.SetWidths(new float[] { 20f, 80f });
                right.WidthPercentage = 100;
                right.DefaultCell.Border = Rectangle.NO_BORDER;

                // LOGO CELL
                PdfPCell logoCell = new PdfPCell(logo);
                logoCell.Border = Rectangle.NO_BORDER;
                logoCell.VerticalAlignment = Element.ALIGN_TOP;
                logoCell.HorizontalAlignment = Element.ALIGN_LEFT;
                right.AddCell(logoCell);

                // ADDRESS CELL
                PdfPTable address = new PdfPTable(1);
                address.DefaultCell.Border = Rectangle.NO_BORDER;
                address.AddCell(new Phrase("Carlton Residence Home Owner's Association", sectionFont));
                address.AddCell(new Phrase("B44 L1 Ph 1 Carlton Residences,", normalFont));
                address.AddCell(new Phrase("Barangay Dita, City of Santa Rosa,", normalFont));
                address.AddCell(new Phrase("Laguna, Philippines", normalFont));

                PdfPCell addressCell = new PdfPCell(address);
                addressCell.Border = Rectangle.NO_BORDER;
                right.AddCell(addressCell);

                header.AddCell(new PdfPCell(right) { Border = Rectangle.NO_BORDER });


                doc.Add(header);
                doc.Add(new Paragraph("\n"));

                // RESIDENT INFO
                PdfPTable info = new PdfPTable(1) { WidthPercentage = 100 };
                info.DefaultCell.Border = Rectangle.NO_BORDER;
                info.AddCell(new Phrase($"Resident Name: {resident.FullName}", normalFont));
                info.AddCell(new Phrase($"Contact No.: {resident.Contact_No}", normalFont));
                info.AddCell(new Phrase($"Email: {resident.Email}", normalFont));
                info.AddCell(new Phrase($"Address: Block {resident.Block}, Lot {resident.Lot}, Phase {resident.Phase_No}", normalFont));
                doc.Add(info);
                doc.Add(new Paragraph("\n"));

                // TABLE
                PdfPTable table = new PdfPTable(8);
                table.WidthPercentage = 100;
                table.SetWidths(new float[] { 12f, 15f, 15f, 20f, 12f, 10f, 10f, 10f });

                string[] headers = { "Invoice No.", "Billing Period", "Date Issued", "Description", "Receipt No.", "Debit", "Credit", "Balance" };
                foreach (var h in headers)
                {
                    PdfPCell cell = new PdfPCell(new Phrase(h, tableHeader));
                    cell.HorizontalAlignment = Element.ALIGN_CENTER;
                    cell.BackgroundColor = new BaseColor(230, 230, 230);
                    cell.Padding = 6;
                    table.AddCell(cell);
                }

                decimal runningBalance = 0;
                foreach (var inv in invoices)
                {
                    var payments = inv.Payments.ToList();
                    decimal debit = inv.Total_Amount;
                    decimal credit = payments.Sum(p => p.Total_Amount);
                    runningBalance += (debit - credit);

                    string receiptNo = payments.Any() ? payments.OrderByDescending(p => p.Date_Issued).First().OR_No : "---";

                    table.AddCell(new Phrase($"INV-{inv.Invoice_No:00000}", tableText));
                    table.AddCell(new Phrase(inv.Billing_Period.ToString("MMMM yyyy"), tableText));
                    table.AddCell(new Phrase(inv.Date_Issued?.ToString("MM/dd/yyyy") ?? "---", tableText));
                    table.AddCell(new Phrase(inv.Description ?? "Monthly Dues", tableText));
                    table.AddCell(new Phrase(receiptNo, tableText));
                    table.AddCell(new Phrase($"PHP {debit:N2}", tableText));
                    table.AddCell(new Phrase(credit > 0 ? $"PHP {credit:N2}" : "---", tableText));
                    table.AddCell(new Phrase($"PHP {runningBalance:N2}", tableText));
                }
                doc.Add(table);

                // TOTAL
                doc.Add(new Paragraph("\n"));
                PdfPTable total = new PdfPTable(1) { WidthPercentage = 100 };
                PdfPCell tcell = new PdfPCell(new Phrase($"Total Amount Due: PHP {runningBalance:N2}", tableHeader))
                {
                    Border = Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                };
                total.AddCell(tcell);
                doc.Add(total);

                // FOOTER
                doc.Add(new Paragraph("\n\n"));
                PdfPTable footer = new PdfPTable(2);
                footer.WidthPercentage = 100;
                PdfPCell prep = new PdfPCell(new Phrase($"Prepared By: {HttpContext.Session.GetString("AdminName") ?? "Admin"}", normalFont)) { Border = Rectangle.NO_BORDER };
                PdfPCell app = new PdfPCell(new Phrase("Approved By:", normalFont)) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT };
                footer.AddCell(prep);
                footer.AddCell(app);
                doc.Add(footer);

                doc.Close();
                var lastName = resident.FullName.Split(' ').First();
                return File(ms.ToArray(), "application/pdf", $"{lastName}_SOA.pdf");
            }
        }

        public async Task<IActionResult> GenerateInvoicePDF(int id, int? invMonth, int? invYear)
        {
            var resident = await _context.ResidentInfo
                .Include(r => r.Invoices).ThenInclude(i => i.Payments)
                .Include(r => r.Invoices).ThenInclude(i => i.Admin)
                .FirstOrDefaultAsync(r => r.Resident_Id == id);

            if (resident == null) return NotFound();

            var invoicesQuery = resident.Invoices.AsQueryable();

            if (invMonth != null)
                invoicesQuery = invoicesQuery.Where(i => i.Billing_Period.Month == invMonth);

            if (invYear != null)
                invoicesQuery = invoicesQuery.Where(i => i.Billing_Period.Year == invYear);

            var invoices = invoicesQuery
                .OrderByDescending(i => i.Billing_Period)
                .ToList();


            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4.Rotate(), 40f, 40f, 35f, 40f);
                PdfWriter writer = PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont = FontFactory.GetFont(FontFactory.TIMES_BOLD, 20);
                var sectionFont = FontFactory.GetFont(FontFactory.TIMES_BOLD, 12);
                var normalFont = FontFactory.GetFont(FontFactory.TIMES, 11);
                var tableHeader = FontFactory.GetFont(FontFactory.TIMES_BOLD, 10);
                var tableText = FontFactory.GetFont(FontFactory.TIMES, 10);
                var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "NewFolder", "carlton-logo.jpg");
                Image logo = Image.GetInstance(logoPath);
                logo.ScaleToFit(60f, 60f);
                logo.Alignment = Element.ALIGN_LEFT;

                // HEADER
                PdfPTable header = new PdfPTable(2) { WidthPercentage = 100 };
                header.SetWidths(new float[] { 60f, 40f });

                PdfPTable left = new PdfPTable(1);
                left.DefaultCell.Border = Rectangle.NO_BORDER;
                left.AddCell(new Phrase("Invoices", titleFont));
                left.AddCell(new Phrase($"Date: {DateTime.Now:MMMM dd, yyyy}", normalFont));
                header.AddCell(new PdfPCell(left) { Border = Rectangle.NO_BORDER });

                PdfPTable right = new PdfPTable(2);
                right.SetWidths(new float[] { 20f, 80f });
                right.WidthPercentage = 100;
                right.DefaultCell.Border = Rectangle.NO_BORDER;

                // LOGO CELL
                PdfPCell logoCell = new PdfPCell(logo);
                logoCell.Border = Rectangle.NO_BORDER;
                logoCell.VerticalAlignment = Element.ALIGN_TOP;
                logoCell.HorizontalAlignment = Element.ALIGN_LEFT;
                right.AddCell(logoCell);

                // ADDRESS CELL
                PdfPTable address = new PdfPTable(1);
                address.DefaultCell.Border = Rectangle.NO_BORDER;
                address.AddCell(new Phrase("Carlton Residence Home Owner's Association", sectionFont));
                address.AddCell(new Phrase("B44 L1 Ph 1 Carlton Residences,", normalFont));
                address.AddCell(new Phrase("Barangay Dita, City of Santa Rosa,", normalFont));
                address.AddCell(new Phrase("Laguna, Philippines", normalFont));

                PdfPCell addressCell = new PdfPCell(address);
                addressCell.Border = Rectangle.NO_BORDER;
                right.AddCell(addressCell);

                header.AddCell(new PdfPCell(right) { Border = Rectangle.NO_BORDER });


                doc.Add(header);
                doc.Add(new Paragraph("\n"));

                // RESIDENT INFO
                PdfPTable info = new PdfPTable(1) { WidthPercentage = 100 };
                info.DefaultCell.Border = Rectangle.NO_BORDER;
                info.AddCell(new Phrase($"Resident Name: {resident.FullName}", normalFont));
                info.AddCell(new Phrase($"Contact No.: {resident.Contact_No}", normalFont));
                info.AddCell(new Phrase($"Email: {resident.Email}", normalFont));
                info.AddCell(new Phrase($"Address: Block {resident.Block}, Lot {resident.Lot}, Phase {resident.Phase_No}", normalFont));
                doc.Add(info);
                doc.Add(new Paragraph("\n"));

                // TABLE
                PdfPTable table = new PdfPTable(8);
                table.WidthPercentage = 100;
                table.SetWidths(new float[] { 12f, 15f, 12f, 20f, 10f, 8f, 8f, 15f });

                string[] headers = { "Invoice No.", "Billing Period", "Due Date", "Description", "Total Amount", "Status", "Age", "Issued By" };
                foreach (var h in headers)
                {
                    PdfPCell cell = new PdfPCell(new Phrase(h, tableHeader));
                    cell.HorizontalAlignment = Element.ALIGN_CENTER;
                    cell.BackgroundColor = new BaseColor(230, 230, 230);
                    cell.Padding = 6;
                    table.AddCell(cell);
                }

                decimal totalInvoiceAmount = 0;
                foreach (var inv in invoices)
                {
                    totalInvoiceAmount += inv.Total_Amount;
                    string issuedBy = inv.Admin?.FullName ?? "---";
                    string age = "---";
                    if (inv.Status == "Unpaid")
                    {
                        int daysLate = (DateTime.Now - inv.Due_Date).Days;
                        age = daysLate > 0 ? $"{daysLate} days" : "---";
                    }

                    table.AddCell(new Phrase($"INV-{inv.Invoice_No:00000}", tableText));
                    table.AddCell(new Phrase(inv.Billing_Period.ToString("MMMM yyyy"), tableText));
                    table.AddCell(new Phrase(inv.Due_Date.ToString("MM/dd/yyyy"), tableText));
                    table.AddCell(new Phrase(inv.Description ?? "Monthly Dues", tableText));
                    table.AddCell(new Phrase($"PHP {inv.Total_Amount:N2}", tableText));
                    table.AddCell(new Phrase(inv.Status, tableText));
                    table.AddCell(new Phrase(age, tableText));
                    table.AddCell(new Phrase(issuedBy, tableText));
                }
                doc.Add(table);

                // TOTAL
                doc.Add(new Paragraph("\n"));
                PdfPTable total = new PdfPTable(1) { WidthPercentage = 100 };

                PdfPCell tcell = new PdfPCell(
                    new Phrase($"Total Invoice Amount: PHP {totalInvoiceAmount:N2}", tableHeader))
                {
                    Border = Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT,
                    PaddingTop = 5
                };

                total.AddCell(tcell);
                doc.Add(total);

                // FOOTER
                doc.Add(new Paragraph("\n\n"));
                PdfPTable footer = new PdfPTable(2);
                footer.WidthPercentage = 100;
                PdfPCell prep = new PdfPCell(new Phrase($"Prepared By: {HttpContext.Session.GetString("AdminName") ?? "Admin"}", normalFont)) { Border = Rectangle.NO_BORDER };
                PdfPCell app = new PdfPCell(new Phrase("Approved By:", normalFont)) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT };
                footer.AddCell(prep);
                footer.AddCell(app);
                doc.Add(footer);

                doc.Close();
                var lastName = resident.FullName.Split(' ').First();
                return File(ms.ToArray(), "application/pdf", $"{lastName}_Invoices.pdf");
            }
        }

        public async Task<IActionResult> GenerateExpensePDF(int? month, int? year)
        {
            var expensesQuery = _context.Expense.AsQueryable();

            if (month.HasValue && month > 0)
                expensesQuery = expensesQuery.Where(e => e.Expense_Date.Month == month);

            if (year.HasValue && year > 0)
                expensesQuery = expensesQuery.Where(e => e.Expense_Date.Year == year);

            var expenses = expensesQuery
                .OrderByDescending(e => e.Expense_Date)
                .ToList();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4.Rotate(), 40f, 40f, 35f, 40f);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont = FontFactory.GetFont(FontFactory.TIMES_BOLD, 20);
                var sectionFont = FontFactory.GetFont(FontFactory.TIMES_BOLD, 12);
                var normalFont = FontFactory.GetFont(FontFactory.TIMES, 11);
                var tableHeader = FontFactory.GetFont(FontFactory.TIMES_BOLD, 10);
                var tableText = FontFactory.GetFont(FontFactory.TIMES, 10);

                // LOGO
                var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "NewFolder", "carlton-logo.jpg");
                Image logo = Image.GetInstance(logoPath);
                logo.ScaleToFit(60f, 60f);
                logo.Alignment = Element.ALIGN_LEFT;

                // HEADER
                PdfPTable header = new PdfPTable(2) { WidthPercentage = 100 };
                header.SetWidths(new float[] { 60f, 40f });

                PdfPTable left = new PdfPTable(1);
                left.DefaultCell.Border = Rectangle.NO_BORDER;
                left.AddCell(new Phrase("Expenses", titleFont));
                left.AddCell(new Phrase($"Date: {DateTime.Now:MMMM dd, yyyy}", normalFont));
                header.AddCell(new PdfPCell(left) { Border = Rectangle.NO_BORDER });

                PdfPTable right = new PdfPTable(2);
                right.SetWidths(new float[] { 20f, 80f });
                right.DefaultCell.Border = Rectangle.NO_BORDER;

                right.AddCell(new PdfPCell(logo) { Border = Rectangle.NO_BORDER });

                PdfPTable address = new PdfPTable(1);
                address.DefaultCell.Border = Rectangle.NO_BORDER;
                address.AddCell(new Phrase("Carlton Residence Home Owner's Association", sectionFont));
                address.AddCell(new Phrase("B44 L1 Ph 1 Carlton Residences,", normalFont));
                address.AddCell(new Phrase("Barangay Dita, City of Santa Rosa,", normalFont));
                address.AddCell(new Phrase("Laguna, Philippines", normalFont));

                right.AddCell(new PdfPCell(address) { Border = Rectangle.NO_BORDER });
                header.AddCell(new PdfPCell(right) { Border = Rectangle.NO_BORDER });

                doc.Add(header);
                doc.Add(new Paragraph("\n"));

                // TABLE
                PdfPTable table = new PdfPTable(6);
                table.WidthPercentage = 100;
                table.SetWidths(new float[] { 15f, 25f, 10f, 10f, 15f, 15f });

                string[] headers = {
                    "Voucher No.",
                    "Expense Type",
                    "Month",
                    "Year",
                    "Date",
                    "Amount"
                };

                foreach (var h in headers)
                {
                    PdfPCell cell = new PdfPCell(new Phrase(h, tableHeader))
                    {
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        BackgroundColor = new BaseColor(230, 230, 230),
                        Padding = 6
                    };
                    table.AddCell(cell);
                }

                decimal totalAmount = 0;

                foreach (var exp in expenses)
                {
                    totalAmount += exp.Total;

                    table.AddCell(new Phrase($"{exp.Voucher_No:00000}", tableText));
                    table.AddCell(new Phrase(exp.Expense_Type, tableText));
                    table.AddCell(new Phrase(exp.Expense_Date.ToString("MMMM"), tableText));
                    table.AddCell(new Phrase(exp.Expense_Date.Year.ToString(), tableText));
                    table.AddCell(new Phrase(exp.Expense_Date.ToString("MM/dd/yyyy"), tableText));
                    table.AddCell(new Phrase($"PHP {exp.Total:N2}", tableText));
                }

                doc.Add(table);

                // TOTAL
                doc.Add(new Paragraph("\n"));
                PdfPTable total = new PdfPTable(1) { WidthPercentage = 100 };
                PdfPCell tcell = new PdfPCell(
                    new Phrase($"Total Expenses: PHP {totalAmount:N2}", tableHeader))
                {
                    Border = Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                };
                total.AddCell(tcell);
                doc.Add(total);

                // FOOTER
                doc.Add(new Paragraph("\n\n"));
                PdfPTable footer = new PdfPTable(2) { WidthPercentage = 100 };
                footer.AddCell(new PdfPCell(
                    new Phrase($"Prepared By: {HttpContext.Session.GetString("AdminName") ?? "Admin"}", normalFont))
                { Border = Rectangle.NO_BORDER });

                footer.AddCell(new PdfPCell(
                    new Phrase("Approved By:", normalFont))
                {
                    Border = Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                });

                doc.Add(footer);

                doc.Close();

                return File(
                    ms.ToArray(),
                    "application/pdf",
                    $"Expenses_{DateTime.Now:yyyyMMdd}.pdf"
                );
            }
        }



    }
}

