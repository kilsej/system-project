namespace Software_Engineering.Models
{
    public class CollectionTarget
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal Target_Amount { get; set; }
        public DateTime created_at { get; set; } = DateTime.Now;

    }
}
