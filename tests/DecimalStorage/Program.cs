using System.Text.Json;
using Kalkulace.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

static void Equal(decimal expected, decimal actual, string field)
{
    if (expected != actual) throw new Exception($"{field}: expected {expected}, got {actual}");
}

// Validate the production model without requiring a live MySQL server.
using (var mysql = new AppDb(new DbContextOptionsBuilder<AppDb>()
    .UseMySql("Server=localhost;Database=precision;User=test", new MySqlServerVersion(new Version(8, 0, 36))).Options))
{
    var script = mysql.Database.GenerateCreateScript();
    foreach (var entity in mysql.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
            if (DecimalStorage.Scale(property) is int scale)
            {
                if (property.GetPrecision() != 18 || property.GetScale() != scale)
                    throw new Exception($"Incorrect precision for {entity.Name}.{property.Name}");
                if (!script.Contains($"`{property.Name}` decimal(18,{scale})"))
                    throw new Exception($"Missing MySQL column definition for {property.Name}");
            }
}

await using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite("Data Source=:memory:").Options);
await db.Database.OpenConnectionAsync();
await db.Database.EnsureCreatedAsync();
var project = new Project
{
    Name = "Precision", UpdatedAt = DateTimeOffset.UtcNow,
    Details = new ProjectDetails(),
    WoodParts = [new ProjectWoodPart { WidthMm = 123.45m, LengthMm = 987.65m,
        ThicknessMm = 25.05m, PricePerM3 = 21900.125m, Quantity = 1.23456789m }],
    Lines = [new ProjectCostLine { UnitPrice = 12.345m, Quantity = 0.123456789m }]
};
db.Projects.Add(project);
db.PricingDefaults.Add(new PricingDefaults { WoodReservePercent = 7.85m });
db.CatalogStates.Add(new CatalogState { Id = 1,
    Wood = [new CatalogWood { Price32 = 1234.565m, Price50 = 2345.675m }],
    Items = [new CatalogItemRow { UnitPrice = 98.765m }] });
await db.SaveChangesAsync();
Equal(123.5m, project.WoodParts[0].WidthMm, "new width");
Equal(987.7m, project.WoodParts[0].LengthMm, "new length");
Equal(25.1m, project.WoodParts[0].ThicknessMm, "new thickness");
Equal(21900.13m, project.WoodParts[0].PricePerM3, "new wood price");
Equal(12.35m, project.Lines[0].UnitPrice, "new unit price");
var invoice = new Invoice { ProjectId = project.Id, Number = "precision",
    Calculation = new InvoiceCalculation { BudgetDifference = -12.345m, BudgetUsagePercent = 51.25m,
        WoodVolumeM3 = 0.000123456789m },
    Lines = [new InvoiceCalculatedLine { BoardThicknessMm = null, UnitPrice = 45.675m,
        AreaM2 = 0.123456789m, VolumeM3 = 0.000123456789m, Quantity = 0.123456789m }],
    Vat = [new InvoiceVatSummary { Base = 12.345m, Vat = 2.595m, Total = 14.945m }] };
db.Invoices.Add(invoice);
db.SaveChanges();
Equal(-12.35m, invoice.Calculation.BudgetDifference, "negative currency midpoint");
Equal(51.3m, invoice.Calculation.BudgetUsagePercent, "invoice percentage");

// Simulate rows written by the old version, bypassing application rounding.
await db.Database.ExecuteSqlRawAsync("UPDATE ProjectWoodPart SET WidthMm = '123.45', LengthMm = '987.65', ThicknessMm = '25.05', PricePerM3 = '21900.125'");
await db.Database.ExecuteSqlRawAsync("UPDATE ProjectDetails SET BudgetLimit = '999.995', WoodReservePercent = '7.85', LamellaLengthExtraMm = '50.05'");
await db.Database.ExecuteSqlRawAsync("UPDATE PricingDefaults SET WoodReservePercent = '7.85'");
await db.Database.ExecuteSqlRawAsync("UPDATE CatalogWood SET Price32 = '1234.565'");
await db.Database.ExecuteSqlRawAsync("UPDATE InvoiceCalculation SET BudgetDifference = '-12.345', BudgetUsagePercent = '51.25'");
await DatabaseUpgrade.InitializeAsync(db);
var loaded = await db.Projects.Include(p => p.Details).Include(p => p.WoodParts).Include(p => p.Lines).SingleAsync();
Equal(1000m, loaded.Details.BudgetLimit, "upgraded budget");
Equal(7.9m, loaded.Details.WoodReservePercent, "upgraded percentage");
Equal(50.1m, loaded.Details.LamellaLengthExtraMm, "upgraded extra length");
Equal(123.5m, loaded.WoodParts[0].WidthMm, "upgraded width");
Equal(987.7m, loaded.WoodParts[0].LengthMm, "upgraded length");
Equal(25.1m, loaded.WoodParts[0].ThicknessMm, "upgraded thickness");
Equal(21900.13m, loaded.WoodParts[0].PricePerM3, "upgraded wood price");
Equal(1.23456789m, loaded.WoodParts[0].Quantity, "unrounded part quantity");
Equal(0.123456789m, loaded.Lines[0].Quantity, "unrounded recorded time");
Equal(7.9m, (await db.PricingDefaults.SingleAsync()).WoodReservePercent, "upgraded defaults");
Equal(1234.57m, (await db.Set<CatalogWood>().SingleAsync()).Price32, "upgraded catalog");
var savedInvoice = await db.Invoices.Include(i => i.Calculation).Include(i => i.Lines).Include(i => i.Vat).SingleAsync();
Equal(-12.35m, savedInvoice.Calculation.BudgetDifference, "upgraded negative currency");
Equal(51.3m, savedInvoice.Calculation.BudgetUsagePercent, "upgraded invoice percentage");
Equal(0.000123456789m, savedInvoice.Calculation.WoodVolumeM3, "unrounded wood volume");
Equal(0.123456789m, savedInvoice.Lines[0].AreaM2, "unrounded area");
if (savedInvoice.Lines[0].BoardThicknessMm is not null) throw new Exception("Nullable thickness changed");
if (loaded.WoodParts[0].PartId is null) throw new Exception("Production identifiers were not upgraded");
var snapshot = JsonSerializer.Serialize(savedInvoice.ToCalculation());
await DatabaseUpgrade.InitializeAsync(db);
loaded.Details.BudgetLimit = 543.215m;
db.Update(loaded);
await db.SaveChangesAsync();
savedInvoice = await db.Invoices.Include(i => i.Calculation).Include(i => i.Lines).Include(i => i.Vat).SingleAsync();
if (snapshot != JsonSerializer.Serialize(savedInvoice.ToCalculation())) throw new Exception("Invoice changed after repeated upgrade or project edit");
await db.PricingDefaults.LoadAsync();
await db.CatalogStates.Include(c => c.Wood).Include(c => c.Items).LoadAsync();
// Exercise every constrained column, including nullable thickness, on updates.
var entries = db.ChangeTracker.Entries().ToList();
foreach (var entry in entries)
    foreach (var property in entry.Properties)
        if (DecimalStorage.Scale(property.Metadata) is not null) property.CurrentValue = 123.455m;
await db.SaveChangesAsync();
foreach (var entry in entries)
{
    await entry.ReloadAsync();
    foreach (var property in entry.Properties)
        if (DecimalStorage.Scale(property.Metadata) is int scale)
            Equal(scale == 1 ? 123.5m : 123.46m, (decimal)property.CurrentValue!, $"updated {entry.Metadata.Name}.{property.Metadata.Name}");
}
Console.WriteLine("Decimal storage: SQLite writes, existing-data upgrade, idempotence, invoice isolation and MySQL schema passed.");
