using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Software_Engineering.Models
{
    public class AdminLog
    {
        public int Log_Id { get; set; }
        public int? Admin_Id { get; set; }
        public string Activity { get; set; }
        public DateTime Timestamp { get; set; }

        // ✅ This property MUST be here for the fix to work

        [ForeignKey("Admin_Id")]
        public virtual Admin Admin { get; set; }
    }
}