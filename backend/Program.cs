using System.Text.Json;
using Kalkulace.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connection = builder.Configuration.GetConnectionString(provider);
if (provider.Equals("MySql", StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("ConnectionStrings:MySql is required.");
    builder.Services.AddDbContext<AppDb>(options => options.UseMySql(connection, ServerVersion.AutoDetect(connection)));
}
else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
{
    connection ??= "Data Source=Data/kalkulace.db";
    var sqlite = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connection);
    if (!Path.IsPathRooted(sqlite.DataSource)) sqlite.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource);
    var directory = Path.GetDirectoryName(Path.GetFullPath(sqlite.DataSource));
    if (directory is not null) Directory.CreateDirectory(directory);
    connection = sqlite.ToString();
    builder.Services.AddDbContext<AppDb>(options => options.UseSqlite(connection));
}
else throw new InvalidOperationException("Database:Provider must be Sqlite or MySql.");

builder.Services.AddCors(options => options.AddPolicy("dev", policy => policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173").AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors("dev");
var staticRoot = new[] { Path.Combine(app.Environment.ContentRootPath, "wwwroot"), Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "frontend", "dist")) }.FirstOrDefault(path => File.Exists(Path.Combine(path, "index.html")));
if (staticRoot is not null)
{
    var files = new PhysicalFileProvider(staticRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = 404; return; }
        await context.Response.SendFileAsync(Path.Combine(staticRoot, "index.html"));
    });
}
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Database.EnsureCreatedAsync();
    // EnsureCreated does not add tables to an existing installation.
    var createCatalogTable = provider.Equals("MySql", StringComparison.OrdinalIgnoreCase)
        ? "CREATE TABLE IF NOT EXISTS CatalogSettings (Id INT NOT NULL PRIMARY KEY, Payload LONGTEXT NOT NULL)"
        : "CREATE TABLE IF NOT EXISTS CatalogSettings (Id INTEGER NOT NULL PRIMARY KEY, Payload TEXT NOT NULL)";
    await db.Database.ExecuteSqlRawAsync(createCatalogTable);
}

app.MapGet("/api/catalog", async (AppDb db) =>
{
    var settings = await db.CatalogSettings.FindAsync(1);
    return settings is null ? Catalog.All : JsonSerializer.Deserialize<CatalogData>(settings.Payload)!;
});
app.MapPut("/api/catalog", async (CatalogData data, AppDb db) =>
{
    var errors = CatalogValidator.Validate(data);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var normalized = new CatalogData(
        data.Wood.Select(w => w with { Name = w.Name.Trim() }).ToList(),
        data.Items.Select(i => i with { Name = i.Name.Trim(), Unit = i.Unit.Trim() }).ToList());
    var settings = await db.CatalogSettings.FindAsync(1);
    if (settings is null) db.CatalogSettings.Add(new CatalogSettings { Id = 1, Payload = JsonSerializer.Serialize(normalized) });
    else settings.Payload = JsonSerializer.Serialize(normalized);
    await db.SaveChangesAsync();
    return Results.Ok(normalized);
});
app.MapGet("/api/projects", async (AppDb db) =>
{
    var projects = await db.Projects.Select(p => new ProjectListItem(p.Id, p.Name, p.CustomerName, p.UpdatedAt)).ToListAsync();
    return projects.OrderByDescending(p => p.UpdatedAt).ToList();
});
app.MapPost("/api/projects", async (ProjectInput input, AppDb db) =>
{
    var errors = Validator.Validate(input);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var project = new Project { Name = input.Name.Trim(), CustomerName = input.CustomerName?.Trim() ?? "", Payload = JsonSerializer.Serialize(input), UpdatedAt = DateTimeOffset.UtcNow };
    db.Projects.Add(project);
    await db.SaveChangesAsync();
    return Results.Created($"/api/projects/{project.Id}", ProjectResponse.From(project));
});
app.MapGet("/api/projects/{id:int}", async (int id, AppDb db) =>
{
    var project = await db.Projects.FindAsync(id);
    return project is null ? Results.NotFound() : Results.Ok(ProjectResponse.From(project));
});
app.MapPut("/api/projects/{id:int}", async (int id, ProjectInput input, AppDb db) =>
{
    var errors = Validator.Validate(input);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var project = await db.Projects.FindAsync(id);
    if (project is null) return Results.NotFound();
    project.Name = input.Name.Trim();
    project.CustomerName = input.CustomerName?.Trim() ?? "";
    project.Payload = JsonSerializer.Serialize(input);
    project.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(ProjectResponse.From(project));
});
app.MapDelete("/api/projects/{id:int}", async (int id, AppDb db) =>
{
    var project = await db.Projects.FindAsync(id);
    if (project is null) return Results.NotFound();
    db.Projects.Remove(project);
    await db.SaveChangesAsync();
    return Results.NoContent();
});
app.MapPost("/api/calculate", (ProjectInput input) =>
{
    var errors = Validator.Validate(input);
    return errors.Count > 0 ? Results.ValidationProblem(errors) : Results.Ok(Calculator.Calculate(input));
});
app.MapGet("/api/invoices", async (AppDb db) => await db.Invoices.OrderByDescending(i => i.IssuedOn).ThenByDescending(i => i.Id).Select(i => new InvoiceListItem(i.Id, i.ProjectId, i.Number, i.CustomerName, i.IssuedOn, i.DueOn, i.Total, i.Status)).ToListAsync());
app.MapGet("/api/invoices/{id:int}", async (int id, AppDb db) =>
{
    var invoice = await db.Invoices.FindAsync(id);
    return invoice is null ? Results.NotFound() : Results.Ok(InvoiceResponse.From(invoice));
});
app.MapPost("/api/projects/{id:int}/invoices", async (int id, InvoiceRequest request, AppDb db) =>
{
    var project = await db.Projects.FindAsync(id);
    if (project is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(request.Number) || string.IsNullOrWhiteSpace(request.CustomerName) || string.IsNullOrWhiteSpace(request.SupplierName) || request.DueOn < request.IssuedOn) return Results.BadRequest(new { error = "Vyplňte číslo, dodavatele, odběratele a platné datum splatnosti." });
    if (await db.Invoices.AnyAsync(i => i.Number == request.Number.Trim())) return Results.Conflict(new { error = "Číslo faktury již existuje." });
    var input = JsonSerializer.Deserialize<ProjectInput>(project.Payload)!;
    var result = Calculator.Calculate(input);
    var invoice = new Invoice { ProjectId = id, Number = request.Number.Trim(), IssuedOn = request.IssuedOn, DueOn = request.DueOn, CustomerName = request.CustomerName.Trim(), CustomerAddress = request.CustomerAddress?.Trim() ?? "", SupplierName = request.SupplierName.Trim(), SupplierAddress = request.SupplierAddress?.Trim() ?? "", SupplierIco = request.SupplierIco?.Trim() ?? "", SupplierDic = request.SupplierDic?.Trim() ?? "", BankAccount = request.BankAccount?.Trim() ?? "", Note = request.Note?.Trim() ?? "", Status = "vystavená", ProjectName = project.Name, Snapshot = JsonSerializer.Serialize(result), Total = result.TotalWithVat };
    db.Invoices.Add(invoice);
    await db.SaveChangesAsync();
    return Results.Created($"/api/invoices/{invoice.Id}", InvoiceResponse.From(invoice));
});
app.MapPatch("/api/invoices/{id:int}/status", async (int id, InvoiceStatusRequest request, AppDb db) =>
{
    if (request.Status is not ("vystavená" or "uhrazená" or "stornovaná")) return Results.BadRequest(new { error = "Neplatný stav faktury." });
    var invoice = await db.Invoices.FindAsync(id);
    if (invoice is null) return Results.NotFound();
    invoice.Status = request.Status;
    await db.SaveChangesAsync();
    return Results.Ok(InvoiceResponse.From(invoice));
});
app.Run();

public partial class Program { }
