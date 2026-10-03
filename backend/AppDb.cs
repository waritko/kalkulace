using Microsoft.EntityFrameworkCore;

namespace Kalkulace.Api;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<CatalogState> CatalogStates => Set<CatalogState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectDetails>().HasKey(d => d.ProjectId);
        modelBuilder.Entity<Project>().HasOne(p => p.Details).WithOne().HasForeignKey<ProjectDetails>(d => d.ProjectId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Project>().HasMany(p => p.WoodParts).WithOne().HasForeignKey(p => p.ProjectId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Project>().HasMany(p => p.Lines).WithOne().HasForeignKey(p => p.ProjectId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ProjectWoodPart>().HasKey(p => new { p.ProjectId, p.Position });
        modelBuilder.Entity<ProjectCostLine>().HasKey(p => new { p.ProjectId, p.Position });

        modelBuilder.Entity<Invoice>().HasIndex(i => i.Number).IsUnique();
        modelBuilder.Entity<InvoiceCalculation>().HasKey(c => c.InvoiceId);
        modelBuilder.Entity<Invoice>().HasOne<Project>().WithMany().HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Invoice>().HasOne(i => i.Calculation).WithOne().HasForeignKey<InvoiceCalculation>(c => c.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Invoice>().HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Invoice>().HasMany(i => i.Vat).WithOne().HasForeignKey(v => v.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<InvoiceCalculatedLine>().HasKey(l => new { l.InvoiceId, l.Position });
        modelBuilder.Entity<InvoiceVatSummary>().HasKey(v => new { v.InvoiceId, v.Position });

        modelBuilder.Entity<CatalogState>().HasMany(c => c.Wood).WithOne().HasForeignKey(w => w.CatalogStateId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CatalogState>().HasMany(c => c.Items).WithOne().HasForeignKey(i => i.CatalogStateId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CatalogWood>().HasKey(w => new { w.CatalogStateId, w.Position });
        modelBuilder.Entity<CatalogItemRow>().HasKey(i => new { i.CatalogStateId, i.Position });
    }
}

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public ProjectDetails Details { get; set; } = null!;
    public List<ProjectWoodPart> WoodParts { get; set; } = [];
    public List<ProjectCostLine> Lines { get; set; } = [];
}

public class ProjectDetails
{
    public int ProjectId { get; set; }
    public decimal BudgetLimit { get; set; }
    public bool NonVatPayer { get; set; }
    public decimal WoodReservePercent { get; set; }
    public decimal MaterialOverheadPercent { get; set; }
    public decimal MaterialMarginPercent { get; set; }
    public decimal LaborMarginPercent { get; set; }
    public decimal ServiceMarginPercent { get; set; }
    public decimal FinanceMarginPercent { get; set; }
    public decimal DiscountPercent { get; set; }
}

public class ProjectWoodPart
{
    public int ProjectId { get; set; }
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public string WoodType { get; set; } = "";
    public decimal WidthMm { get; set; }
    public decimal LengthMm { get; set; }
    public decimal ThicknessMm { get; set; }
    public decimal Quantity { get; set; }
    public decimal PricePerM3 { get; set; }
    public string? Finish { get; set; }
}

public class ProjectCostLine
{
    public int ProjectId { get; set; }
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public int VatRate { get; set; }
    public string? MaterialType { get; set; }
    public string? ServiceCategory { get; set; }
    public bool UsesExtraction { get; set; }
    public bool UsesVacuum { get; set; }
    public string? AutomaticMachineryCharge { get; set; }
}

public class CatalogState
{
    public int Id { get; set; }
    public List<CatalogWood> Wood { get; set; } = [];
    public List<CatalogItemRow> Items { get; set; } = [];
}

public class CatalogWood
{
    public int CatalogStateId { get; set; }
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public decimal Price32 { get; set; }
    public decimal Price50 { get; set; }
}

public class CatalogItemRow
{
    public int CatalogStateId { get; set; }
    public int Position { get; set; }
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int VatRate { get; set; }
    public string? MaterialType { get; set; }
    public string? ServiceCategory { get; set; }
    public bool UsesExtraction { get; set; }
    public bool UsesVacuum { get; set; }
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
    public decimal Total { get; set; }
    public InvoiceCalculation Calculation { get; set; } = null!;
    public List<InvoiceCalculatedLine> Lines { get; set; } = [];
    public List<InvoiceVatSummary> Vat { get; set; } = [];
}

public class InvoiceCalculation
{
    public int InvoiceId { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal ServiceCost { get; set; }
    public decimal FinanceCost { get; set; }
    public decimal WoodCost { get; set; }
    public decimal WoodVolumeM3 { get; set; }
    public decimal Overhead { get; set; }
    public decimal Profit { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalWithoutVat { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalWithVat { get; set; }
    public decimal BudgetDifference { get; set; }
    public decimal BudgetUsagePercent { get; set; }
}

public class InvoiceCalculatedLine
{
    public int InvoiceId { get; set; }
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Cost { get; set; }
    public int VatRate { get; set; }
    public decimal AreaM2 { get; set; }
    public decimal VolumeM3 { get; set; }
    public decimal? BoardThicknessMm { get; set; }
}

public class InvoiceVatSummary
{
    public int InvoiceId { get; set; }
    public int Position { get; set; }
    public int Rate { get; set; }
    public decimal Base { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
}
