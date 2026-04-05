using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Software_Engineering.Models
{
    public class Expense
    {
        [Key]
        public int Expense_Id { get; set; }

        [Required(ErrorMessage = "Expense type is required.")]
        [StringLength(100)]
        public string Expense_Type { get; set; }

        [Required(ErrorMessage = "Voucher number is required.")]
        [StringLength(50)]
        public string? Voucher_No { get; set; }

        public short Expense_Year { get; set; }
        public byte Expense_Month { get; set; }
        public byte Expense_Day { get; set; }


        public DateTime Expense_Date { get; set; }

        [Required(ErrorMessage = "Amount is required.")]
        [Range(0.01, 999999999, ErrorMessage = "Amount must be greater than 0.")]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Total { get; set; }

        [ForeignKey(nameof(Admin))]
        public int? Admin_Id { get; set; }

        public Admin? Admin { get; set; }

        public DateTime Created_At { get; set; }


    }
}
