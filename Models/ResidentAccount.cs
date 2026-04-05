using Software_Engineering.Models;

public class ResidentAccount
{
    public int Resident_Id { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string Status { get; set; }

    // ✅ NEW: Tracks number of logins for OTP logic
    public int LoginCount { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEnd { get; set; }

    public DateTime? LastOtpVerification { get; set; }
    public ResidentInfo ResidentInfo { get; set; }
}