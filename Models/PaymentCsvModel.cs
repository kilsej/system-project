namespace Software_Engineering.Models
{
    public class PaymentCsvModel
    {
        public DateTime? PaymentDate { get; set; }
        public decimal? Total_Amount { get; set; }
        public string? InvoiceMonth { get; set; }   // "January 2026"
        public string? OR_No { get; set; }
        public string? Method { get; set; }
        public string? Remarks { get; set; }
    }

}
