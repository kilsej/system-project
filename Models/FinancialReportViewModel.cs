namespace Software_Engineering.Models
{
    public class FinancialReportViewModel
    {
        public int Month { get; set; }
        public int Year { get; set; }
        public List<int> Years { get; set; } = new();

        // ===== MONTHLY =====
        public decimal MonthlyTarget { get; set; }
        public decimal PreviousTarget { get; set; }


        public decimal MonthlyGrossCollection { get; set; }   
        public decimal MonthlyExpenses { get; set; }          
        public decimal MonthlyNetCollection { get; set; }     
        public decimal MonthlyDeficit { get; set; }
        public decimal MonthlyRemainingToTarget { get; set; }

        public decimal MonthlyRemaining { get; set; }
        public decimal MonthlyRate { get; set; }

        public int UpdatedResidents { get; set; }
        public int DelinquentResidents { get; set; }

        // ===== YEARLY =====
        public decimal YearlyTarget { get; set; }
        public decimal YearlyGrossCollection { get; set; }
        public decimal YearlyNetCollection { get; set; }
        public decimal YearlyRate { get; set; }

        // ===== YEARLY STATUS =====
        public int YearlyUpdatedResidents { get; set; }
        public int YearlyDelinquentResidents { get; set; }

        public List<MonthlyChartData> MonthlyChart { get; set; } = new();
        public List<YearlyChartData> YearlyChart { get; set; } = new();
        public List<TargetHistoryVM> TargetHistory { get; set; } = new();

    }


    public class MonthlyChartData
    {
        public string Month { get; set; }
        public decimal Collection { get; set; }
        public decimal Target { get; set; }
    }

    public class YearlyChartData
    {
        public int Year { get; set; }
        public decimal Collection { get; set; }
   
    }
    public class TargetHistoryVM
    {
        public int Month { get; set; }
        public string MonthName { get; set; }
        public int Year { get; set; }
        public decimal Amount { get; set; }
    }

}
