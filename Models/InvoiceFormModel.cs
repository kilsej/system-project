namespace Software_Engineering.Models
{
    public class InvoiceFormModel
    {
        public int Invoice_Id { get; set; }
        public int Resident_Id { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
        public decimal Rate { get; set; }
        public string? Description { get; set; }
        public DateTime? Date_Issued { get; set; }
        public string Issued_By { get; set; }

        public ResidentInfo ResidentInfo { get; set; }
    }
}

