using System.Collections.Generic;

namespace Software_Engineering.Models
{
    public class ResidentInfo
    {
        public int Resident_Id { get; set; }
        public string FullName { get; set; }
        public string Block { get; set; }
        public string Lot { get; set; }
        public string Phase_No { get; set; }
        public int Year_Of_Residency { get; set; }
        public string? Contact_No { get; set; }
        public string? Email { get; set; }

        public ResidentAccount ResidentAccount { get; set; }
        public ICollection<Invoice> Invoices { get; set; }
    }
}
