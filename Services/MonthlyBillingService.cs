using iTextSharp.text;
using Microsoft.EntityFrameworkCore;
using Software_Engineering.Data;
using Software_Engineering.Models;
using System.Net;
using System.Net.Mail;

public class MonthlyBillingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public MonthlyBillingService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckAndRunMonthlyBilling();
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task CheckAndRunMonthlyBilling()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        int month = DateTime.Now.Month;
        int year = DateTime.Now.Year;

        bool alreadyProcessed = await context.Invoice
            .AnyAsync(i =>
                i.Description == "Monthly HOA Dues" &&
                i.Billing_Period.Month == month &&
                i.Billing_Period.Year == year);

        if (!alreadyProcessed)
        {
            await ProcessMonthlyBilling();
            // RECORD THAT SYSTEM RAN
            context.SystemRun.Add(new SystemRun
            {
                Run_Month = month,
                Run_Year = year,
                Run_Date = DateTime.Now
            });

            await context.SaveChangesAsync();
        }
    }


    private async Task ProcessMonthlyBilling()
    {

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();


        int month = DateTime.Now.Month;
        int year = DateTime.Now.Year;

        var residents = await context.ResidentInfo
            .Include(r => r.ResidentAccount)
            .Where(r => r.ResidentAccount.Status == "Active")
            .ToListAsync();

        foreach (var r in residents)
        {
            // Check if invoice already exists
            var invoice = await context.Invoice
                .FirstOrDefaultAsync(i =>
                    i.Resident_Id == r.Resident_Id &&
                    i.Billing_Period.Month == month &&
                    i.Billing_Period.Year == year);
            
            // Create if not exists
            if (invoice == null)
            {
                invoice = new Invoice
                {
                    Resident_Id = r.Resident_Id,
                    Billing_Period = new DateTime(year, month, 1),
                    Issued_By = "System",
                    Due_Date = new DateTime(year, month, 1).AddMonths(1).AddDays(-1),
                    Total_Amount = 150,
                    Status = "Unpaid",
                    Date_Issued = DateTime.Now,
                    Description = "Monthly HOA Dues"
                };

                context.Invoice.Add(invoice);
            }

            // EMAIL ONLY IF CURRENT MONTH IS UNPAID
            if (invoice.Status == "Unpaid" &&
                !string.IsNullOrWhiteSpace(r.Email))
            {
                SendReminderEmail(r.FullName, r.Email, invoice.Total_Amount);
            }
        }

        await context.SaveChangesAsync();
    }


    private void SendReminderEmail(string name, string email, decimal balance)
    {
        var fromAddress = new MailAddress("itsarathearmy@gmail.com", "Carlton Residences HOA Inc.");
        var toAddress = new MailAddress(email);
        var monthName = DateTime.Now.ToString("MMMM yyyy");

        const string fromPassword = "fmks zcrr azjk pmfx";

        var subject = $"Carlton Residences HOA: {monthName.ToUpper()} Monthly Due Reminder";

        var messageId = Guid.NewGuid().ToString(); // unique every email

        var body = $@"
<html>
<body>

<!-- Invisible unique marker to prevent Gmail collapsing -->

<h3>Monthly Due Reminder for {monthName}</h3>

<p>Good day, <strong>{name}</strong>!</p>

<p>This is a reminder regarding your outstanding balance for <b>{monthName}</b>.</p>

<hr/>

<h4>Billing Summary</h4>
<ul>
    <li>Monthly Due: ₱150.00</li>
    <li>Total Outstanding Balance: <strong>₱{balance:N2}</strong></li>
</ul>

<p>Please settle your dues on or before the due date.</p>

<br/>
<p>Warm regards,<br/>
Carlton Residences HOA Office</p>

</body>
</html>";

        var smtp = new SmtpClient
        {
            Host = "smtp.gmail.com",
            Port = 587,
            EnableSsl = true,
            Credentials = new NetworkCredential(fromAddress.Address, fromPassword)
        };

        var message = new MailMessage();
        message.From = fromAddress;
        message.To.Add(toAddress);
        message.Subject = subject;
        message.Body = body;
        message.IsBodyHtml = true; // VERY IMPORTANT

        smtp.Send(message);


    }
}
