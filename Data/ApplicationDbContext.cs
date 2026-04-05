using Microsoft.EntityFrameworkCore;
using Software_Engineering.Models;

namespace Software_Engineering.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<ResidentInfo> ResidentInfo { get; set; }
        public DbSet<ResidentAccount> ResidentAccount { get; set; }
        public DbSet<Admin> Admin { get; set; }
        public DbSet<Invoice> Invoice { get; set; }
        public DbSet<Payment> Payment { get; set; }
        public DbSet<AdminLog> AdminLog { get; set; }
        public DbSet<CollectionTarget> CollectionTarget { get; set; }
        public DbSet<Expense> Expense { get; set; }
        public DbSet<SystemRun> SystemRun { get; set; }

        public DbSet<TargetHistoryVM> MonthlyTargets { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);


            modelBuilder.Entity<CollectionTarget>(entity =>
            {
                entity.ToTable("collection_target");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Target_Amount).HasColumnName("target_amount").HasColumnType("decimal(10,2)");
                entity.Property(e => e.Year).HasColumnName("year");
                entity.Property(e => e.Month).HasColumnName("month");
            });

            modelBuilder.Entity<Expense>(entity =>
            {
                entity.ToTable("expense");

                entity.HasKey(e => e.Expense_Id);

                entity.HasKey(e => e.Expense_Id);

                entity.Property(e => e.Admin_Id)
                      .HasColumnName("admin_id");

                entity.HasOne(e => e.Admin)
                      .WithMany(a => a.Expenses)
                      .HasForeignKey(e => e.Admin_Id)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.Property(e => e.Expense_Year)
                      .HasColumnName("expense_year")
                      .IsRequired();

                entity.Property(e => e.Expense_Month)
                      .HasColumnName("expense_month")
                      .IsRequired();

                entity.Property(e => e.Expense_Day)
                      .HasColumnName("expense_day")
                      .IsRequired();

                entity.Property(e => e.Total)
                      .HasColumnType("decimal(10,2)")
                      .IsRequired();
            });

            modelBuilder.Entity<Admin>(entity =>
            {
                entity.ToTable("admin");

                entity.HasKey(a => a.Admin_Id);
            });
            modelBuilder.Entity<TargetHistoryVM>(entity =>
            {
                entity.HasNoKey();
                entity.ToView(null);

                entity.Property(e => e.Month);
                entity.Property(e => e.Year);
                entity.Property(e => e.Amount);
            });



            modelBuilder.Entity<ResidentInfo>()
                .HasKey(r => r.Resident_Id);

            modelBuilder.Entity<ResidentAccount>()
                .HasKey(a => a.Resident_Id);

            modelBuilder.Entity<Admin>()
                .HasKey(a => a.Admin_Id);

            modelBuilder.Entity<Invoice>()
                .HasKey(i => i.Invoice_No);

            modelBuilder.Entity<Payment>()
                .HasKey(p => p.Receipt_No);

            modelBuilder.Entity<AdminLog>()
                .HasKey(l => l.Log_Id);

     
            modelBuilder.Entity<ResidentAccount>()
                .HasOne(a => a.ResidentInfo)
                .WithOne(r => r.ResidentAccount)
                .HasForeignKey<ResidentAccount>(a => a.Resident_Id)
                .OnDelete(DeleteBehavior.Cascade);

 
            modelBuilder.Entity<Invoice>()
                .HasOne(i => i.ResidentInfo)
                .WithMany(r => r.Invoices)
                .HasForeignKey(i => i.Resident_Id)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Invoice>()
                .HasOne(i => i.Admin)
                .WithMany(a => a.Invoices)
                .HasForeignKey(i => i.Admin_Id)
                .OnDelete(DeleteBehavior.Restrict);

      
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Invoice)
                .WithMany(i => i.Payments)
                .HasForeignKey(p => p.Invoice_No)
                .OnDelete(DeleteBehavior.Cascade);

      
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Admin)
                .WithMany(a => a.Payments)
                .HasForeignKey(p => p.Admin_Id)
                .OnDelete(DeleteBehavior.Restrict);

          
            modelBuilder.Entity<AdminLog>()
                .HasOne(l => l.Admin)
                .WithMany(a => a.AdminLogs)
                .HasForeignKey(l => l.Admin_Id)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
