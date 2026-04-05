using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Software_Engineering.Data;
using Software_Engineering.Models; 
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class ResidentController : Controller
{
    private readonly ApplicationDbContext _context;

    public ResidentController(ApplicationDbContext context)
    {
        _context = context;
    }
    public IActionResult Index()
    {
        if (HttpContext.Session.GetInt32("ResidentId") != null)
        {
            return RedirectToAction("Dashboard");
        }

        return RedirectToAction("Login", "Admin");
    }

    [HttpGet]
    public IActionResult Login()
    {
        if (HttpContext.Session.GetInt32("ResidentId") != null)
            return RedirectToAction("Dashboard");

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string Username, string Password)
    {
        var account = await _context.ResidentAccount
            .Include(r => r.ResidentInfo)
            .FirstOrDefaultAsync(a =>
                a.Username == Username &&
                a.Password == Password &&
                a.Status == "Active");

        if (account == null)
        {
            TempData["LoginError"] = "Invalid username or password.";
            return RedirectToAction("Login");
        }

        HttpContext.Session.SetInt32("ResidentId", account.Resident_Id);
        HttpContext.Session.SetString("ResidentName", account.ResidentInfo.FullName);
        HttpContext.Session.SetString("ResidentUsername", account.Username);

        return RedirectToAction("Dashboard");
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        TempData["LoginError"] = "You have been securely logged out.";
        return RedirectToAction("Login", "Admin");
    }


    [HttpGet]
    public async Task<IActionResult> Settings()
    {

        int? residentId = HttpContext.Session.GetInt32("ResidentId");
        if (residentId == null) return RedirectToAction("Login", "Admin");

        var resident = await _context.ResidentInfo
            .Include(r => r.ResidentAccount)
            .FirstOrDefaultAsync(r => r.Resident_Id == residentId);

        if (resident == null) return NotFound();

        return View(resident);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateProfile(string Email, string ContactNo)
    {
        int? residentId = HttpContext.Session.GetInt32("ResidentId");
        if (residentId == null) return RedirectToAction("Login", "Admin");

        var resident = await _context.ResidentInfo.FindAsync(residentId);
        if (resident == null) return NotFound();

        resident.Email = Email;
        resident.Contact_No = ContactNo;

        await _context.SaveChangesAsync();

        TempData["SettingsSuccess"] = "Profile details updated successfully.";
        return RedirectToAction("Settings");
    }

    [HttpPost]
    public async Task<IActionResult> ChangePassword(string CurrentPassword, string NewPassword, string ConfirmPassword)
    {
        int? residentId = HttpContext.Session.GetInt32("ResidentId");
        if (residentId == null) return RedirectToAction("Login", "Admin");

        var account = await _context.ResidentAccount.FindAsync(residentId);
        if (account == null) return NotFound();

        if (account.Password != CurrentPassword)
        {
            TempData["PasswordError"] = "Incorrect current password.";
            return RedirectToAction("Settings");
        }

        if (NewPassword != ConfirmPassword)
        {
            TempData["PasswordError"] = "New password and confirmation do not match.";
            return RedirectToAction("Settings");
        }

        account.Password = NewPassword; 
        await _context.SaveChangesAsync();

        TempData["SettingsSuccess"] = "Password changed successfully.";
        return RedirectToAction("Settings");
    }

    public IActionResult Dashboard()
    {
        if (HttpContext.Session.GetInt32("ResidentId") == null)
            return RedirectToAction("Login");

        int residentId = HttpContext.Session.GetInt32("ResidentId").Value;
        DateTime threeMonthsAgo = DateTime.Now.AddMonths(-3);

        var resident = _context.ResidentInfo
            .FirstOrDefault(r => r.Resident_Id == residentId);

        var invoices = _context.Invoice
            .Where(i => i.Resident_Id == residentId)
            .ToList();

        var payments = _context.Payment
            .Where(p => invoices.Select(i => i.Invoice_No).Contains(p.Invoice_No))
            .ToList();

        decimal totalDue = invoices
            .Where(i => i.Status != "Paid")
            .Sum(i =>
                i.Total_Amount -
                payments.Where(p => p.Invoice_No == i.Invoice_No)
                        .Sum(p => p.Total_Amount)
            );

        decimal paymentsReceived = payments.Sum(p => p.Total_Amount);

        bool hasDelinquent = invoices.Any(i =>
            i.Status == "Delinquent" ||
            (i.Status == "Unpaid" && i.Due_Date < threeMonthsAgo)
        );

        bool hasUnpaid = invoices.Any(i => i.Status == "Unpaid");

        string financialStatus = hasDelinquent ? "Red"
                               : hasUnpaid ? "Amber"
                               : "Green";

        int delinquentResidents = _context.Invoice
            .Where(i =>
                i.Status == "Delinquent" ||
                (i.Status == "Unpaid" && i.Due_Date < threeMonthsAgo)
            )
            .Select(i => i.Resident_Id)
            .Distinct()
            .Count();

        int residentsWithInvoices = _context.Invoice
            .Select(i => i.Resident_Id)
            .Distinct()
            .Count();

        int updatedResidents = Math.Max(residentsWithInvoices - delinquentResidents, 0);

        var model = new ResidentDashboardViewModel
        {
            FullName = resident.FullName,
            TotalAmountDues = totalDue,
            PaymentsReceived = paymentsReceived,
            FinancialStatus = financialStatus,
            UpdatedResidents = updatedResidents,
            DelinquentResidents = delinquentResidents
        };

        return View(model);
    }

    public async Task<IActionResult> Invoices(int? payMonth, int? payYear)
    {
        int? residentId = HttpContext.Session.GetInt32("ResidentId");
        if (residentId == null)
            return RedirectToAction("Login");

        var query = _context.Invoice
            .Include(i => i.Admin)
            .Where(i => i.Resident_Id == residentId);

        if (payMonth.HasValue)
            query = query.Where(i => i.Billing_Period.Month == payMonth);

        if (payYear.HasValue)
            query = query.Where(i => i.Billing_Period.Year == payYear);

        var invoices = await query
            .OrderByDescending(i => i.Billing_Period)
            .ToListAsync();

        ViewBag.SelectedPayMonth = payMonth;
        ViewBag.SelectedPayYear = payYear;

        ViewData["PageTitle"] = "Invoices";

        return View(invoices);
    }

    public async Task<IActionResult> PaymentHistory(int? payMonth, int? payYear)
    {
        int? residentId = HttpContext.Session.GetInt32("ResidentId");
        if (residentId == null)
            return RedirectToAction("Login");

        var query = _context.Payment
            .Include(p => p.Invoice)
            .Include(p => p.Admin)
            .Where(p => p.Invoice.Resident_Id == residentId);

        if (payMonth.HasValue)
            query = query.Where(p => p.Date_Issued.Month == payMonth);

        if (payYear.HasValue)
            query = query.Where(p => p.Date_Issued.Year == payYear);

        var payments = await query
            .OrderByDescending(p => p.Date_Issued)
            .ToListAsync();

        ViewBag.SelectedPayMonth = payMonth;
        ViewBag.SelectedPayYear = payYear;

        ViewData["PageTitle"] = "Payment History";

        return View("payments", payments);
    }

    [HttpGet]
    public async Task<IActionResult> Statements(int? payMonth, int? payYear)
    {
        int? residentId = HttpContext.Session.GetInt32("ResidentId");
        if (residentId == null)
            return RedirectToAction("Login");

        var query = _context.Invoice
            .Include(i => i.Payments)
            .Where(i => i.Resident_Id == residentId);

        if (payMonth.HasValue)
            query = query.Where(i => i.Billing_Period.Month == payMonth);

        if (payYear.HasValue)
            query = query.Where(i => i.Billing_Period.Year == payYear);

        var statements = await query
            .OrderBy(i => i.Billing_Period)
            .ToListAsync(); 

        var model = new ResidentStatementVM
        {
            Statements = statements
        };

        ViewBag.SelectedPayMonth = payMonth;
        ViewBag.SelectedPayYear = payYear;

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> GenerateSOAPDF()
    {
        int? residentId = HttpContext.Session.GetInt32("ResidentId");
        if (residentId == null)
            return RedirectToAction("Login");

        var resident = await _context.ResidentInfo
            .Include(r => r.Invoices)
                .ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(r => r.Resident_Id == residentId);

        if (resident == null)
            return NotFound();

       
        var invoices = resident.Invoices
            .OrderBy(i => i.Billing_Period)
            .ToList();

        using (var ms = new MemoryStream())
        {
            Document doc = new Document(PageSize.A4.Rotate(), 40f, 40f, 35f, 40f);
            PdfWriter.GetInstance(doc, ms);
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

     
            PdfPCell logoCell = new PdfPCell(logo);
            logoCell.Border = Rectangle.NO_BORDER;
            logoCell.VerticalAlignment = Element.ALIGN_TOP;
            logoCell.HorizontalAlignment = Element.ALIGN_LEFT;
            right.AddCell(logoCell);

       
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

         
            PdfPTable info = new PdfPTable(1)
            {
                WidthPercentage = 100
            };
            info.DefaultCell.Border = Rectangle.NO_BORDER;

            info.AddCell(new Phrase($"Resident Name: {resident.FullName}", normalFont));
            info.AddCell(new Phrase($"Contact No.: {resident.Contact_No}", normalFont));
            info.AddCell(new Phrase($"Email: {resident.Email}", normalFont));
            info.AddCell(new Phrase(
                $"Address: Block {resident.Block}, Lot {resident.Lot}, Phase {resident.Phase_No}, Carlton Residences, St. Agata Homes Subd., Brgy. Dita Rd, Santa Rosa City, Laguna",
                normalFont));

            doc.Add(info);
            doc.Add(new Paragraph("\n"));

       
            PdfPTable table = new PdfPTable(8)
            {
                WidthPercentage = 100
            };
            table.SetWidths(new float[] { 12f, 15f, 15f, 20f, 12f, 10f, 10f, 10f });

            string[] headers =
            {
                "Invoice No.", "Billing Period", "Date Issued", "Description",
                "Receipt No.", "Debit", "Credit", "Balance"
            };

            foreach (var h in headers)
            {
                table.AddCell(new PdfPCell(new Phrase(h, tableHeader))
                {
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    BackgroundColor = new BaseColor(230, 230, 230),
                    Padding = 6
                });
            }

            
            decimal runningBalance = 0;

            foreach (var inv in invoices)
            {
                var payments = inv.Payments?.ToList() ?? new List<Payment>();

                decimal debit = inv.Total_Amount;
                decimal credit = payments.Sum(p => p.Total_Amount);

                runningBalance += debit;
                runningBalance -= credit;

                string receiptNo = payments.Any()
                    ? payments.OrderByDescending(p => p.Date_Issued).First().OR_No
                    : "---";

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

   
            doc.Add(new Paragraph("\n"));
            PdfPTable total = new PdfPTable(1) { WidthPercentage = 100 };
            total.AddCell(new PdfPCell(
                new Phrase($"Total Amount Due: PHP {runningBalance:N2}", tableHeader))
            {
                Border = Rectangle.NO_BORDER,
                HorizontalAlignment = Element.ALIGN_RIGHT
            });

            doc.Add(total);

            doc.Close();

           
            var lastName = resident.FullName.Split(',')[0];
            var fileName = $"{lastName}_SOA.pdf";

            return File(ms.ToArray(), "application/pdf", fileName);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GenerateInvoicePDF()
    {
     
        int? residentId = HttpContext.Session.GetInt32("ResidentId");
        if (residentId == null)
            return RedirectToAction("Login");

        var resident = await _context.ResidentInfo
            .Include(r => r.Invoices)
                .ThenInclude(i => i.Admin)
            .FirstOrDefaultAsync(r => r.Resident_Id == residentId);

        if (resident == null)
            return NotFound();

        var invoices = resident.Invoices
            .OrderByDescending(i => i.Billing_Period)
            .ToList();

        decimal totalDue = invoices
            .Where(i => i.Status == "Unpaid")
            .Sum(i => i.Total_Amount);


        using (var ms = new MemoryStream())
        {
            Document doc = new Document(PageSize.A4.Rotate(), 40f, 40f, 35f, 40f);
            PdfWriter.GetInstance(doc, ms);
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

     
            PdfPCell logoCell = new PdfPCell(logo);
            logoCell.Border = Rectangle.NO_BORDER;
            logoCell.VerticalAlignment = Element.ALIGN_TOP;
            logoCell.HorizontalAlignment = Element.ALIGN_LEFT;
            right.AddCell(logoCell);

           
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

          
            PdfPTable info = new PdfPTable(1)
            {
                WidthPercentage = 100
            };
            info.DefaultCell.Border = Rectangle.NO_BORDER;

            info.AddCell(new Phrase($"Resident Name: {resident.FullName}", normalFont));
            info.AddCell(new Phrase($"Contact No.: {resident.Contact_No}", normalFont));
            info.AddCell(new Phrase($"Email: {resident.Email}", normalFont));
            info.AddCell(new Phrase(
                $"Address: Block {resident.Block}, Lot {resident.Lot}, Phase {resident.Phase_No}, Carlton Residences, St. Agata Homes Subd., Brgy. Dita Rd, Santa Rosa City, Laguna",
                normalFont));

            doc.Add(info);
            doc.Add(new Paragraph("\n"));

    
            PdfPTable table = new PdfPTable(8);
            table.WidthPercentage = 100;
            table.SetWidths(new float[]
            {
                12f, 15f, 15f, 15f, 22f, 12f, 10f, 15f
            });

            string[] headers =
            {
                "Invoice No.", "Billing Period", "Date Issued", "Due Date",
                "Description", "Amount", "Status", "Issued By"
            };

            foreach (var h in headers)
            {
                table.AddCell(new PdfPCell(new Phrase(h, tableHeader))
                {
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    BackgroundColor = new BaseColor(230, 230, 230),
                    Padding = 6
                });
            }

            foreach (var inv in invoices)
            {
                table.AddCell(new Phrase($"INV-{inv.Invoice_No:00000}", tableText));
                table.AddCell(new Phrase(inv.Billing_Period.ToString("MMMM yyyy"), tableText));
                table.AddCell(new Phrase(inv.Date_Issued?.ToString("MM/dd/yyyy") ?? "---", tableText));
                table.AddCell(new Phrase(inv.Due_Date.ToString("MM/dd/yyyy"), tableText));
                table.AddCell(new Phrase(inv.Description ?? "Monthly Dues", tableText));
                table.AddCell(new Phrase($"PHP {inv.Total_Amount:N2}", tableText));
                table.AddCell(new Phrase(inv.Status, tableText));
                table.AddCell(new Phrase(inv.Admin?.FullName ?? "---", tableText));
            }

            doc.Add(table);

            doc.Add(new Paragraph("\n"));

            PdfPTable totalTable = new PdfPTable(1)
            {
                WidthPercentage = 100
            };

            PdfPCell totalCell = new PdfPCell(
                new Phrase($"Total Amount Due: PHP {totalDue:N2}", tableHeader)
            )
            {
                Border = Rectangle.NO_BORDER,
                HorizontalAlignment = Element.ALIGN_RIGHT,
                PaddingTop = 10
            };

            totalTable.AddCell(totalCell);
            doc.Add(totalTable);


            doc.Close();



            var lastName = resident.FullName.Split(',')[0];
            var fileName = $"{lastName}_Invoices.pdf";

            return File(ms.ToArray(), "application/pdf", fileName);
        }
    }

    public async Task<IActionResult> GeneratePaymentPDF()
    {
 
        int? residentId = HttpContext.Session.GetInt32("ResidentId");
        if (residentId == null)
            return RedirectToAction("Login");

        var resident = await _context.ResidentInfo
            .Include(r => r.Invoices)
                .ThenInclude(i => i.Payments)
            .Include(r => r.Invoices)
                .ThenInclude(i => i.Admin)
            .FirstOrDefaultAsync(r => r.Resident_Id == residentId);

        if (resident == null)
            return NotFound();

        var payments = resident.Invoices
            .SelectMany(i => i.Payments)
            .OrderByDescending(p => p.Date_Issued)
            .ToList();

        using (var ms = new MemoryStream())
        {
            Document doc = new Document(PageSize.A4.Rotate(), 40f, 40f, 35f, 40f);
            PdfWriter.GetInstance(doc, ms);
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

       
            PdfPCell logoCell = new PdfPCell(logo);
            logoCell.Border = Rectangle.NO_BORDER;
            logoCell.VerticalAlignment = Element.ALIGN_TOP;
            logoCell.HorizontalAlignment = Element.ALIGN_LEFT;
            right.AddCell(logoCell);

       
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


            header.AddCell(new PdfPCell(right) { Border = Rectangle.NO_BORDER });

            doc.Add(header);
            doc.Add(new Paragraph("\n"));

            PdfPTable info = new PdfPTable(1)
            {
                WidthPercentage = 100
            };
            info.DefaultCell.Border = Rectangle.NO_BORDER;

            info.AddCell(new Phrase($"Resident Name: {resident.FullName}", normalFont));
            info.AddCell(new Phrase($"Contact No.: {resident.Contact_No}", normalFont));
            info.AddCell(new Phrase($"Email: {resident.Email}", normalFont));
            info.AddCell(new Phrase(
                $"Address: Block {resident.Block}, Lot {resident.Lot}, Phase {resident.Phase_No}, Carlton Residences, St. Agata Homes Subd., Brgy. Dita Rd, Santa Rosa City, Laguna",
                normalFont));

            doc.Add(info);
            doc.Add(new Paragraph("\n"));

            PdfPTable table = new PdfPTable(8);
            table.WidthPercentage = 100;
            table.SetWidths(new float[]
            {
            10f, // Invoice
            15f, // Billing Period
            10f, // OR No
            12f, // Date Paid
            12f, // Amount
            10f, // Method
            16f, // Remarks
            15f  // Issued By
            });

            string[] headers =
            {
            "Invoice No.",
            "Billing Period",
            "OR No.",
            "Date Paid",
            "Amount",
            "Method",
            "Remarks",
            "Issued By"
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

            foreach (var p in payments)
            {
                table.AddCell(new Phrase($"INV-{p.Invoice_No:00000}", tableText));
                table.AddCell(new Phrase(p.Invoice?.Billing_Period.ToString("MMMM yyyy") ?? "---", tableText));
                table.AddCell(new Phrase(p.OR_No ?? "---", tableText));
                table.AddCell(new Phrase(p.Date_Issued.ToString("MM/dd/yyyy"), tableText));
                table.AddCell(new Phrase($"PHP {p.Total_Amount:N2}", tableText));
                table.AddCell(new Phrase(p.Method ?? "", tableText));
                table.AddCell(new Phrase(p.Remarks ?? "", tableText));
                table.AddCell(new Phrase(p.Admin?.FullName ?? "", tableText));
            }

            doc.Add(table);

            doc.Add(new Paragraph("\n"));

            decimal totalPayments = payments.Sum(p => p.Total_Amount);

            PdfPTable total = new PdfPTable(1) { WidthPercentage = 100 };
            PdfPCell totalCell = new PdfPCell(
                new Phrase($"Total Amount Paid: PHP {totalPayments:N2}", tableHeader)
            )
            {
                Border = Rectangle.NO_BORDER,
                HorizontalAlignment = Element.ALIGN_RIGHT
            };

            total.AddCell(totalCell);
            doc.Add(total);

            doc.Close();

            var lastName = resident.FullName.Split(',')[0].Trim();
            var fileName = $"{lastName}_PaymentHistory.pdf";

            return File(ms.ToArray(), "application/pdf", fileName);
        }
    }
}