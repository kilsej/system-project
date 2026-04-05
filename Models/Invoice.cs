using System;
using System.Collections.Generic;

namespace Software_Engineering.Models
{
    public class Invoice
    {
        public int Invoice_No { get; set; }
        public int Resident_Id { get; set; }
        public int? Admin_Id { get; set; }  // nullable because of ON DELETE SET NULL

        public string Issued_By { get; set; } = "System"; // ✅ default
        public DateTime Billing_Period { get; set; }
        public DateTime Due_Date { get; set; }
        public string? Description { get; set; }
        public decimal Total_Amount { get; set; }
        public string Status { get; set; } // Paid / Unpaid / Delinquent
        public DateTime? Date_Issued { get; set; }

        public DateTime? Expiry_Date { get; set; }

        // Relationships
        public ResidentInfo ResidentInfo { get; set; }
        public Admin Admin { get; set; }
        public ICollection<Payment> Payments { get; set; }
    }
}
