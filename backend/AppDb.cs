using Microsoft.EntityFrameworkCore;

namespace Kalkulace.Api;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>().HasIndex(i => i.Number).IsUnique();
        modelBuilder.Entity<Invoice>().HasOne<Project>().WithMany().HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

public class Invoice
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Number { get; set; } = "";
    public DateOnly IssuedOn { get; set; }
    public DateOnly DueOn { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerAddress { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string SupplierAddress { get; set; } = "";
    public string SupplierIco { get; set; } = "";
    public string SupplierDic { get; set; } = "";
    public string BankAccount { get; set; } = "";
    public string Note { get; set; } = "";
    public string Status { get; set; } = "vystavená";
    public string ProjectName { get; set; } = "";
    public string Snapshot { get; set; } = "{}";
    public decimal Total { get; set; }
}
