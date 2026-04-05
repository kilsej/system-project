namespace Software_Engineering.Models
{
    public class ExpenseCsvModel
    {
        public string Voucher_No { get; set; }
        public string Expense_Type { get; set; }
        public byte? Expense_Month { get; set; }
        public short? Expense_Year { get; set; }
        public DateTime? Expense_Date { get; set; }
        public decimal? Total { get; set; }
    }
}
