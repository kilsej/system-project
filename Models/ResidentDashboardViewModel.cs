namespace Software_Engineering.Models
{
    public class ResidentDashboardViewModel
    {
        public string FullName { get; set; }

        public decimal TotalAmountDues { get; set; }
        public decimal PaymentsReceived { get; set; }

        public string FinancialStatus { get; set; }

        public int UpdatedResidents { get; set; }
        public int DelinquentResidents { get; set; }
    }
}
