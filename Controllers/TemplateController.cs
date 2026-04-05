using Microsoft.AspNetCore.Mvc;
using Software_Engineering.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
namespace Software_Engineering.Controllers
{
    public class TemplateController : Controller
    {
        private readonly ApplicationDbContext _context;

        public TemplateController(ApplicationDbContext context)
        {
            _context = context;
        }
        public IActionResult Index()
        {
            return View();
        }
        public IActionResult DownloadResidentsCsv()
        {
            if (HttpContext.Session.GetString("IsPrimary") != "True" &&
                HttpContext.Session.GetString("CanImportResident") != "True")
            {
                return Unauthorized();
            }

            var residents = _context.ResidentInfo
                .Include(r => r.ResidentAccount)
                .Select(r => new
                {
                    r.Resident_Id,
                    r.ResidentAccount.Username,
                    r.FullName,
                    r.Block,
                    r.Lot,
                    r.Phase_No,
                    r.Year_Of_Residency,
                    r.Contact_No,
                    r.Email
                })
                .ToList();

            var csv = new StringBuilder();

            // CSV Header
            csv.AppendLine(
                "Resident ID,Username,Full Name,Block,Lot,Phase,Year of Residency,Contact No,Email"
            );

            foreach (var r in residents)
            {
                string yearSuffix = (r.Year_Of_Residency % 100).ToString("00");
                string formattedResidentId = $"R{yearSuffix}-{r.Resident_Id:0000}";

                csv.AppendLine(
                    $"{formattedResidentId}," +
                    $"{Escape(r.Username)}," +
                    $"{Escape(r.FullName)}," +
                    $"{Escape(r.Block)}," +
                    $"{Escape(r.Lot)}," +
                    $"{Escape(r.Phase_No)}," +
                    $"{r.Year_Of_Residency}," +
                    $"{Escape(r.Contact_No)}," +
                    $"{Escape(r.Email)}"
                );
            }

            return File(
                Encoding.UTF8.GetBytes(csv.ToString()),
                "text/csv",
                $"Residents_{DateTime.Now:yyyyMMdd}.csv"
            );
        }
        private string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        public async Task<IActionResult> DownloadPaymentHistoryCsv(int residentId)
        {
            var resident = await _context.ResidentInfo
                .FirstOrDefaultAsync(r => r.Resident_Id == residentId);

            if (resident == null)
                return NotFound();

            var payments = await _context.Payment
                .Include(p => p.Invoice)
                .Include(p => p.Admin)
                .Where(p => p.Invoice.Resident_Id == residentId)
                .OrderByDescending(p => p.Date_Issued)
                .ToListAsync();

            var sb = new StringBuilder();

            sb.AppendLine("Invoice No,Billing Period,OR No,Date Paid,Amount,Method,Remarks,Issued By");

            foreach (var p in payments)
            {
                string billingPeriod = p.Invoice != null
    ? p.Invoice.Billing_Period.ToString("MMMM yyyy")
    : "";

                sb.AppendLine(
                    $"INV-{p.Invoice_No:00000}," +
                    $"{billingPeriod}," +
                    $"{p.OR_No}," +
                    $"{p.Date_Issued:MM/dd/yyyy}," +
                    $"{p.Total_Amount:0.00}," +
                    $"{p.Method}," +
                    $"{(p.Remarks ?? "")}," +
                    $"{p.Admin?.FullName ?? "System Administrator"}"
                );
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());

            string safeName = resident.FullName
                .Replace(",", "")
                .Replace(" ", "_");

            return File(
                bytes,
                "text/csv",
                $"PaymentHistory_{safeName}_{DateTime.Now:yyyyMMdd}.csv"
            );
        }

        public async Task<IActionResult> DownloadSOACsv(int residentId)
        {
            var resident = await _context.ResidentInfo
                .FirstOrDefaultAsync(r => r.Resident_Id == residentId);

            if (resident == null)
                return NotFound();

            var invoices = await _context.Invoice
                .Include(i => i.Payments)
                .Where(i => i.Resident_Id == residentId)
                .OrderBy(i => i.Billing_Period)
                .ToListAsync();

            var sb = new StringBuilder();

            sb.AppendLine("Invoice No,Billing Period,Date Issued,Description,Receipt No,Debit,Credit,Balance");

            foreach (var i in invoices)
            {
                decimal totalPayments = i.Payments?.Sum(p => p.Total_Amount) ?? 0m;
                decimal balance = i.Total_Amount - totalPayments;

                string receiptNo = i.Payments?
    .FirstOrDefault(p => !string.IsNullOrEmpty(p.OR_No))?
    .OR_No ?? "";
                string dateIssued = i.Date_Issued?.ToString("MM/dd/yyyy") ?? "";

                sb.AppendLine(
                    $"INV-{i.Invoice_No:00000}," +
                    $"{i.Billing_Period:MMMM yyyy}," +
                    $"{dateIssued}," +
                    $"{(i.Description ?? "")}," +
                    $"{receiptNo}," +
                    $"{i.Total_Amount:0.00}," +
                    $"{totalPayments:0.00}," +
                    $"{balance:0.00}"
                );
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());

            string safeName = resident.FullName
                .Replace(",", "")
                .Replace(" ", "_");

            return File(
                bytes,
                "text/csv",
                $"SOA_{safeName}_{DateTime.Now:yyyyMMdd}.csv"
            );
        }

        public async Task<IActionResult> DownloadInvoiceCsv(int residentId)
        {
            var resident = await _context.ResidentInfo
                .FirstOrDefaultAsync(r => r.Resident_Id == residentId);

            if (resident == null)
                return NotFound();

            var invoices = await _context.Invoice
                .Where(i => i.Resident_Id == residentId)
                .OrderByDescending(i => i.Billing_Period)
                .ToListAsync();

            var sb = new StringBuilder();

            sb.AppendLine("Invoice No,Billing Period,Date Issued,Due Date,Description,Total Amount,Status,Age,Issued By");

            foreach (var i in invoices)
            {
                string dateIssued = i.Date_Issued?.ToString("MM/dd/yyyy") ?? "";
                string dueDate = i.Due_Date.ToString("MM/dd/yyyy");

                string age = i.Status == "Paid"
                    ? "---"
                    : (DateTime.Today - i.Due_Date.Date).Days > 0
                        ? (DateTime.Today - i.Due_Date.Date).Days.ToString()
                        : "0";

                sb.AppendLine(
                    $"INV-{i.Invoice_No:00000}," +
                    $"{i.Billing_Period:MMMM yyyy}," +
                    $"{dateIssued}," +
                    $"{dueDate}," +
                    $"{(i.Description ?? "")}," +
                    $"{i.Total_Amount:0.00}," +
                    $"{i.Status}," +
                    $"{age}," +
                    $"{i.Issued_By}"
                );
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());

            string safeName = resident.FullName
                .Replace(",", "")
                .Replace(" ", "_");

            return File(
                bytes,
                "text/csv",
                $"Invoices_{safeName}_{DateTime.Now:yyyyMMdd}.csv"
            );
        }

        public async Task<IActionResult> DownloadExpenseCsv()
        {
            if (HttpContext.Session.GetString("IsPrimary") != "True" &&
                HttpContext.Session.GetString("CanViewExpense") != "True")
            {
                return Unauthorized();
            }

            var expenses = await _context.Expense
                .OrderByDescending(e => e.Expense_Date)
                .ToListAsync();

            var sb = new StringBuilder();

            sb.AppendLine("Voucher No,Expense,Month,Year,Date,Amount");

            foreach (var e in expenses)
            {
                sb.AppendLine(
                    $"{e.Voucher_No}," +
                    $"{Escape(e.Expense_Type)}," +
                    $"{e.Expense_Month}," +
                    $"{e.Expense_Year}," +
                    $"{e.Expense_Date:yyyy-MM-dd}," +
                    $"{e.Total:0.00}"
                );
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());

            return File(
                bytes,
                "text/csv",
                $"Expenses_{DateTime.Now:yyyyMMdd}.csv"
            );
        }







    }
}
