using Org.BouncyCastle.Pqc.Crypto.Lms;

namespace Software_Engineering.Models
{
    public class DashboardViewModel
    {
        public int SelectedMonth { get; set; }
        public int SelectedYear { get; set; }

        public List<int> Years { get; set; }

        public int TotalResidents { get; set; }
        public int PaidResidents { get; set; }
        public int UnpaidResidents { get; set; }
        public decimal PaymentRate { get; set; }
        public string PaymentMethod { get; set; }
        public List<BlockPaymentSummary> BlockSummaries { get; set; }

    }

    public class BlockPaymentSummary
    {
        public string Phase { get; set; }
        public string Block { get; set; }
        public int PaidCount { get; set; }
        public int UnpaidCount { get; set; }
        public decimal PaidPercentage { get; set; }
        public string PaymentStatus { get;set; }
    }
}

