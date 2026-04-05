using Software_Engineering.Models;

public class Payment
{
    public int Receipt_No { get; set; }      // AUTO INCREMENT
    public int Invoice_No { get; set; }
    public decimal Total_Amount { get; set; }
    public string Method { get; set; }       // Cash / GCash / Bank Transfer
    public int? Admin_Id { get; set; }
    public DateTime Date_Issued { get; set; }

    public string OR_No { get; set; }        // VARCHAR(5)
    public string? Remarks { get; set; }

    // Relationships
    public Invoice Invoice { get; set; }
    public Admin Admin { get; set; }
}
