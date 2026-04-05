using System;
using System.Collections.Generic;

namespace Software_Engineering.Models
{
    public class Invoice
    {
        public int Invoice_No { get; set; }
        public int Resident_Id { get; set; }
        public int? Admin_Id { get; set; }  

        public string Issued_By { get; set; } = "System"; 
        public DateTime Billing_Period { get; set; }
        public DateTime Due_Date { get; set; }
        public string? Description { get; set; }
        public decimal Total_Amount { get; set; }
        public string Status { get; set; } 
        public DateTime? Date_Issued { get; set; }

        public DateTime? Expiry_Date { get; set; }

        public ResidentInfo ResidentInfo { get; set; }
        public Admin Admin { get; set; }
        public ICollection<Payment> Payments { get; set; }
    }
}
