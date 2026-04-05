using System.Collections.Generic;

namespace Software_Engineering.Models
{
    public class Admin
    {
        public int Admin_Id { get; set; }
        public string? FullName { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Email { get; set; } 
        public string? Status { get; set; }
        public string? Permissions { get; set; } 

        public bool Is_Primary { get; set; }
        public int FailedLoginAttempts { get; set; }
        public DateTime? LockoutEnd { get; set; }


        public bool CanAddResident { get; set; }
        public bool CanAddPayment { get; set; }
        public bool CanCreateInvoice { get; set; }
        public bool CanEditResident { get; set; }
        public bool CanEditInvoice { get; set; }
        public bool CanImportResident { get; set; }
        public bool CanImportInvoice { get; set; }
        public bool CanImportPayment { get; set; }
        public bool CanManageExpenses { get; set; }
        public bool CanEditExpenses { get; set; }

        public DateTime? LastOtpVerification { get; set; }


        // ✅ NEW: Tracks login frequency for OTP logic
        public int LoginCount { get; set; }
        public ICollection<Invoice> Invoices { get; set; }
        public ICollection<Payment> Payments { get; set; }
        public ICollection<AdminLog> AdminLogs { get; set; }
        public ICollection<Expense> Expenses { get; set; }

    }
}