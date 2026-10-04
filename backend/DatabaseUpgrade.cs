using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Kalkulace.Api;

public static class DatabaseUpgrade
{
    private static readonly HashSet<string> NewTables =
    [
        "ProjectDetails", "ProjectWoodPart", "ProjectCostLine", "CatalogStates", "CatalogWood",
        "CatalogItemRow", "InvoiceCalculation", "InvoiceCalculatedLine", "InvoiceVatSummary"
    ];

    public static async Task InitializeAsync(AppDb db)
    {
        await db.Database.EnsureCreatedAsync();
        await AddMaterialTypeColumns(db);
        await AddFinishColumns(db);
        await AddGlueBoardColumns(db);
        var projectPayload = await HasColumn(db, "Projects", "Payload");
        var invoiceSnapshot = await HasColumn(db, "Invoices", "Snapshot");
        var catalogExists = await HasTable(db, "CatalogSettings");
        if (!projectPayload && !invoiceSnapshot && !catalogExists) return;

        // EnsureCreated leaves existing databases alone. Add the new tables before reading legacy data.
        var script = db.Database.GenerateCreateScript();
        foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var table = Regex.Match(statement, @"^CREATE TABLE\s+[`""\[]?(\w+)", RegexOptions.IgnoreCase).Groups[1].Value;
            var indexedTable = Regex.Match(statement, @"\bON\s+[`""\[]?(\w+)", RegexOptions.IgnoreCase).Groups[1].Value;
            if (NewTables.Contains(table) && !await HasTable(db, table))
                await db.Database.ExecuteSqlRawAsync(statement);
            else if (NewTables.Contains(indexedTable) && !await HasIndex(db, statement))
                await db.Database.ExecuteSqlRawAsync(statement);
        }
        await AddMaterialTypeColumns(db);
        await AddFinishColumns(db);
        await AddGlueBoardColumns(db);

        var projects = projectPayload ? await ReadLegacy(db, "SELECT Id, Payload FROM Projects") : [];
        var invoices = invoiceSnapshot ? await ReadLegacy(db, "SELECT Id, Snapshot FROM Invoices") : [];
        var catalog = catalogExists ? await ReadLegacy(db, "SELECT Id, Payload FROM CatalogSettings") : [];

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            foreach (var (id, json) in projects)
            {
                if (await db.Set<ProjectDetails>().AnyAsync(d => d.ProjectId == id)) continue;
                var project = await db.Projects.SingleAsync(p => p.Id == id);
                project.SetInput(JsonSerializer.Deserialize<ProjectInput>(json)!);
                await db.SaveChangesAsync();
            }
            foreach (var (id, json) in invoices)
            {
                if (await db.Set<InvoiceCalculation>().AnyAsync(c => c.InvoiceId == id)) continue;
                var invoice = await db.Invoices.SingleAsync(i => i.Id == id);
                invoice.SetCalculation(JsonSerializer.Deserialize<Calculation>(json)!);
                await db.SaveChangesAsync();
            }
            if (catalog.Count > 0 && !await db.CatalogStates.AnyAsync(s => s.Id == 1))
            {
                var state = new CatalogState { Id = 1 };
                state.SetData(JsonSerializer.Deserialize<CatalogData>(catalog[0].Json)!);
                db.CatalogStates.Add(state);
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
        }

        // Keep old columns until all rows have been copied successfully. This also permits a retry after a failed upgrade.
        if (projectPayload) await db.Database.ExecuteSqlRawAsync("ALTER TABLE Projects DROP COLUMN Payload");
        if (invoiceSnapshot) await db.Database.ExecuteSqlRawAsync("ALTER TABLE Invoices DROP COLUMN Snapshot");
        if (catalogExists) await db.Database.ExecuteSqlRawAsync("DROP TABLE CatalogSettings");
    }

    private static async Task AddMaterialTypeColumns(AppDb db)
    {
        if (await HasTable(db, "ProjectCostLine") && !await HasColumn(db, "ProjectCostLine", "MaterialType"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectCostLine ADD COLUMN MaterialType TEXT NULL");
        if (await HasTable(db, "CatalogItemRow") && !await HasColumn(db, "CatalogItemRow", "MaterialType"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE CatalogItemRow ADD COLUMN MaterialType TEXT NULL");
    }

    private static async Task AddFinishColumns(AppDb db)
    {
        if (await HasTable(db, "ProjectWoodPart") && !await HasColumn(db, "ProjectWoodPart", "ApplyFinish"))
        {
            if (db.Database.IsSqlite()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectWoodPart ADD COLUMN ApplyFinish INTEGER NOT NULL DEFAULT 0");
            else await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectWoodPart ADD COLUMN ApplyFinish tinyint(1) NOT NULL DEFAULT 0");
        }
        if (await HasTable(db, "ProjectCostLine") && !await HasColumn(db, "ProjectCostLine", "AutomaticFinish"))
        {
            if (db.Database.IsSqlite()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectCostLine ADD COLUMN AutomaticFinish INTEGER NOT NULL DEFAULT 0");
            else await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectCostLine ADD COLUMN AutomaticFinish tinyint(1) NOT NULL DEFAULT 0");
        }
    }

    private static async Task AddGlueBoardColumns(AppDb db)
    {
        if (!await HasTable(db, "ProjectDetails")) return;
        if (!await HasColumn(db, "ProjectDetails", "LamellaLengthExtraMm"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectDetails ADD COLUMN LamellaLengthExtraMm decimal(18,2) NOT NULL DEFAULT 50");
        if (!await HasColumn(db, "ProjectDetails", "LamellaMergeToleranceMm"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectDetails ADD COLUMN LamellaMergeToleranceMm decimal(18,2) NOT NULL DEFAULT 50");
        if (!await HasColumn(db, "ProjectDetails", "GlueBoardWastePercent"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectDetails ADD COLUMN GlueBoardWastePercent decimal(18,2) NOT NULL DEFAULT 10");
    }

    private static async Task<bool> HasColumn(AppDb db, string table, string column)
    {
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT {column} FROM {table} WHERE 1 = 0";
            await db.Database.OpenConnectionAsync();
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (DbException) { return false; }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task<bool> HasTable(AppDb db, string table)
    {
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT 1 FROM {table} WHERE 1 = 0";
            await db.Database.OpenConnectionAsync();
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (DbException) { return false; }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task<bool> HasIndex(AppDb db, string statement)
    {
        var index = Regex.Match(statement, @"^CREATE (?:UNIQUE )?INDEX\s+[`""\[]?(\w+)", RegexOptions.IgnoreCase).Groups[1].Value;
        if (index.Length == 0) return false;
        var sqlite = db.Database.IsSqlite();
        var sql = sqlite
            ? "SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = @name"
            : "SELECT 1 FROM information_schema.statistics WHERE table_schema = DATABASE() AND index_name = @name LIMIT 1";
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@name";
            parameter.Value = index;
            command.Parameters.Add(parameter);
            return await command.ExecuteScalarAsync() is not null;
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task<List<(int Id, string Json)>> ReadLegacy(AppDb db, string sql)
    {
        var rows = new List<(int, string)>();
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rows.Add((reader.GetInt32(0), reader.GetString(1)));
        }
        finally { await db.Database.CloseConnectionAsync(); }
        return rows;
    }
}
