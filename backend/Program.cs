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
    await DatabaseUpgrade.InitializeAsync(db);
}

app.MapGet("/api/catalog", async (AppDb db) =>
{
    return await LoadCatalog(db);
});
app.MapPut("/api/catalog", async (CatalogData data, AppDb db) =>
{
    var errors = CatalogValidator.Validate(data);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var normalized = MachineryCharges.EnsurePresent(MaterialTypes.EnsurePresent(new CatalogData(
        data.Wood.Select(w => w with { Name = w.Name.Trim() }).ToList(),
        data.Items.Select(i => i with { Name = i.Name.Trim(), Unit = i.Unit.Trim() }).ToList())));
    var settings = await db.CatalogStates.AsSplitQuery().Include(s => s.Wood).Include(s => s.Items).SingleOrDefaultAsync(s => s.Id == 1);
    await using var transaction = await db.Database.BeginTransactionAsync();
    if (settings is null) { settings = new CatalogState { Id = 1 }; db.CatalogStates.Add(settings); }
    else
    {
        db.RemoveRange(settings.Wood);
        db.RemoveRange(settings.Items);
        await db.SaveChangesAsync();
    }
    settings.SetData(normalized);
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
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
    input = MachineryCharges.Sync(input, await LoadCatalog(db));
    var project = new Project { UpdatedAt = DateTimeOffset.UtcNow };
    project.SetInput(input);
    db.Projects.Add(project);
    await db.SaveChangesAsync();
    return Results.Created($"/api/projects/{project.Id}", ProjectResponse.From(project));
});
app.MapGet("/api/projects/{id:int}", async (int id, AppDb db) =>
{
    var project = await LoadProject(db, id);
    return project is null ? Results.NotFound() : Results.Ok(ProjectResponse.From(project));
});
app.MapPut("/api/projects/{id:int}", async (int id, ProjectInput input, AppDb db) =>
{
    var errors = Validator.Validate(input);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    input = MachineryCharges.Sync(input, await LoadCatalog(db));
    var project = await LoadProject(db, id);
    if (project is null) return Results.NotFound();
    await using var transaction = await db.Database.BeginTransactionAsync();
    db.RemoveRange(project.WoodParts);
    db.RemoveRange(project.Lines);
    await db.SaveChangesAsync();
    project.SetInput(input);
    project.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
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
app.MapPost("/api/calculate", async (ProjectInput input, AppDb db) =>
{
    var errors = Validator.Validate(input);
    return errors.Count > 0 ? Results.ValidationProblem(errors) : Results.Ok(Calculator.Calculate(MachineryCharges.Sync(input, await LoadCatalog(db))));
});
app.MapGet("/api/invoices", async (AppDb db) => await db.Invoices.OrderByDescending(i => i.IssuedOn).ThenByDescending(i => i.Id).Select(i => new InvoiceListItem(i.Id, i.ProjectId, i.Number, i.CustomerName, i.IssuedOn, i.DueOn, i.Total, i.Status)).ToListAsync());
app.MapGet("/api/invoices/{id:int}", async (int id, AppDb db) =>
{
    var invoice = await LoadInvoice(db, id);
    return invoice is null ? Results.NotFound() : Results.Ok(InvoiceResponse.From(invoice));
});
app.MapPost("/api/projects/{id:int}/invoices", async (int id, InvoiceRequest request, AppDb db) =>
{
    var project = await LoadProject(db, id);
    if (project is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(request.Number) || string.IsNullOrWhiteSpace(request.CustomerName) || string.IsNullOrWhiteSpace(request.SupplierName) || request.DueOn < request.IssuedOn) return Results.BadRequest(new { error = "Vyplňte číslo, dodavatele, odběratele a platné datum splatnosti." });
    if (await db.Invoices.AnyAsync(i => i.Number == request.Number.Trim())) return Results.Conflict(new { error = "Číslo faktury již existuje." });
    var result = Calculator.Calculate(project.ToInput());
    var invoice = new Invoice { ProjectId = id, Number = request.Number.Trim(), IssuedOn = request.IssuedOn, DueOn = request.DueOn, CustomerName = request.CustomerName.Trim(), CustomerAddress = request.CustomerAddress?.Trim() ?? "", SupplierName = request.SupplierName.Trim(), SupplierAddress = request.SupplierAddress?.Trim() ?? "", SupplierIco = request.SupplierIco?.Trim() ?? "", SupplierDic = request.SupplierDic?.Trim() ?? "", BankAccount = request.BankAccount?.Trim() ?? "", Note = request.Note?.Trim() ?? "", Status = "vystavená", ProjectName = project.Name, Total = result.TotalWithVat };
    invoice.SetCalculation(result);
    db.Invoices.Add(invoice);
    await db.SaveChangesAsync();
    return Results.Created($"/api/invoices/{invoice.Id}", InvoiceResponse.From(invoice));
});
app.MapPatch("/api/invoices/{id:int}/status", async (int id, InvoiceStatusRequest request, AppDb db) =>
{
    if (request.Status is not ("vystavená" or "uhrazená" or "stornovaná")) return Results.BadRequest(new { error = "Neplatný stav faktury." });
    var invoice = await LoadInvoice(db, id);
    if (invoice is null) return Results.NotFound();
    invoice.Status = request.Status;
    await db.SaveChangesAsync();
    return Results.Ok(InvoiceResponse.From(invoice));
});
app.Run();

static async Task<CatalogData> LoadCatalog(AppDb db)
{
    var settings = await db.CatalogStates.AsNoTracking().AsSplitQuery().Include(s => s.Wood).Include(s => s.Items).SingleOrDefaultAsync(s => s.Id == 1);
    return MachineryCharges.EnsurePresent(MaterialTypes.EnsurePresent(settings is null ? Catalog.All : settings.ToData()));
}

static Task<Project?> LoadProject(AppDb db, int id) => db.Projects
    .AsSplitQuery().Include(p => p.Details).Include(p => p.WoodParts).Include(p => p.Lines)
    .SingleOrDefaultAsync(p => p.Id == id);

static Task<Invoice?> LoadInvoice(AppDb db, int id) => db.Invoices
    .AsSplitQuery().Include(i => i.Calculation).Include(i => i.Lines).Include(i => i.Vat)
    .SingleOrDefaultAsync(i => i.Id == id);

public partial class Program { }
