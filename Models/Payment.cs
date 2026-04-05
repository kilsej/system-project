using Software_Engineering.Models;

public class Payment
{
    public int Receipt_No { get; set; }      
    public int Invoice_No { get; set; }
    public decimal Total_Amount { get; set; }
    public string Method { get; set; }       
    public int? Admin_Id { get; set; }
    public DateTime Date_Issued { get; set; }

    public string OR_No { get; set; }        
    public string? Remarks { get; set; }

    public Invoice Invoice { get; set; }
    public Admin Admin { get; set; }
}
