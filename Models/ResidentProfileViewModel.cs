namespace Software_Engineering.Models
{
    public class ResidentProfileViewModel
    {
        public ResidentInfo Resident { get; set; }

        public List<Invoice> Invoices { get; set; }
        public List<Payment> Payments { get; set; }
        public List<Invoice> Statements { get; set; } // SOA
    }

}
