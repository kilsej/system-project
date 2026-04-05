namespace Software_Engineering.Models
{
    public class ExpenseListViewModel
    {
        public Expense NewExpense { get; set; } = new();

        public List<Expense> Expenses { get; set; } = new();

        public string? SearchTerm { get; set; }
        public int? Year { get; set; }
        public int? Month { get; set; }


    }
}
