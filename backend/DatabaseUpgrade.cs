using System.Data.Common;
using System.Globalization;
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
        if (!await HasTable(db, "Users"))
        {
            var userScript = db.Database.GenerateCreateScript();
            foreach (var statement in userScript.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Regex.IsMatch(statement, @"^CREATE TABLE\s+[`""\[]?Users\b|^CREATE UNIQUE INDEX\s+.*\bON\s+[`""\[]?Users\b", RegexOptions.IgnoreCase))
                    await db.Database.ExecuteSqlRawAsync(statement);
        }
        await AppAuthentication.SeedAsync(db);
        if (!await HasTable(db, "SavedCuttingPlans"))
        {
            var create = db.Database.GenerateCreateScript().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Single(statement => Regex.IsMatch(statement, @"^CREATE TABLE\s+[`""\[]?SavedCuttingPlans\b", RegexOptions.IgnoreCase));
            await db.Database.ExecuteSqlRawAsync(create);
        }
        if (!await HasTable(db, "StockBoards"))
        {
            var create = db.Database.GenerateCreateScript().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Single(statement => Regex.IsMatch(statement, @"^CREATE TABLE\s+[`""\[]?StockBoards\b", RegexOptions.IgnoreCase));
            await db.Database.ExecuteSqlRawAsync(create);
        }
        // EnsureCreated does not add tables to an existing database.
        if (!await HasTable(db, "PricingDefaults"))
        {
            var create = db.Database.GenerateCreateScript().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Single(statement => Regex.IsMatch(statement, @"^CREATE TABLE\s+[`""\[]?PricingDefaults\b", RegexOptions.IgnoreCase));
            await db.Database.ExecuteSqlRawAsync(create);
        }
        await AddMaterialTypeColumns(db);
        await AddFinishColumns(db);
        await AddGlueBoardColumns(db);
        await AddCuttingPlanColumns(db);
        await AddWorkDateColumns(db);
        await AddProductionColumns(db);
        var projectPayload = await HasColumn(db, "Projects", "Payload");
        var invoiceSnapshot = await HasColumn(db, "Invoices", "Snapshot");
        var catalogExists = await HasTable(db, "CatalogSettings");
        if (!projectPayload && !invoiceSnapshot && !catalogExists)
        {
            await UpgradeDecimalStorage(db);
            return;
        }

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
        await AddCuttingPlanColumns(db);
        await AddWorkDateColumns(db);
        await AddProductionColumns(db);

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
        await UpgradeDecimalStorage(db);
    }

    private static async Task UpgradeDecimalStorage(AppDb db)
    {
        // SQLite has no fixed decimal scale. Upgrade values exactly in C# instead of
        // converting currency through SQLite's binary floating-point ROUND function.
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var transaction = db.Database.IsSqlite()
                ? await db.Database.GetDbConnection().BeginTransactionAsync() : null;
            if (transaction is not null)
            {
                await using var version = db.Database.GetDbConnection().CreateCommand();
                version.Transaction = transaction;
                version.CommandText = "CREATE TABLE IF NOT EXISTS DecimalStorageVersion (Version INTEGER PRIMARY KEY)";
                await version.ExecuteNonQueryAsync();
                version.CommandText = "SELECT 1 FROM DecimalStorageVersion WHERE Version = 2";
                if (await version.ExecuteScalarAsync() is not null) return;
            }
            foreach (var entity in db.Model.GetEntityTypes())
            {
                var table = entity.GetTableName()!;
                var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table, entity.GetSchema());
                foreach (var property in entity.GetProperties())
                {
                    if (DecimalStorage.Scale(property) is not int scale) continue;
                    var column = property.GetColumnName(store)!;
                    if (!db.Database.IsSqlite())
                    {
                        await using var check = db.Database.GetDbConnection().CreateCommand();
                        check.CommandText = "SELECT NUMERIC_PRECISION, NUMERIC_SCALE FROM information_schema.columns "
                            + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column";
                        AddParameter(check, "@table", table);
                        AddParameter(check, "@column", column);
                        bool matches;
                        await using (var reader = await check.ExecuteReaderAsync())
                            matches = await reader.ReadAsync() && !reader.IsDBNull(0) && !reader.IsDBNull(1)
                                && Convert.ToInt32(reader.GetValue(0)) == 18 && Convert.ToInt32(reader.GetValue(1)) == scale;
                        if (!matches)
                        {
                            // Identifiers come from the EF model, never from user input.
                            await using var alter = db.Database.GetDbConnection().CreateCommand();
                            alter.CommandText = $"ALTER TABLE `{table}` MODIFY COLUMN `{column}` decimal(18,{scale}) "
                                + (property.IsNullable ? "NULL" : "NOT NULL");
                            await alter.ExecuteNonQueryAsync();
                        }
                        continue;
                    }

                    var updates = new List<(long RowId, decimal Value)>();
                    await using (var read = db.Database.GetDbConnection().CreateCommand())
                    {
                        read.Transaction = transaction;
                        read.CommandText = $"SELECT rowid, \"{column}\" FROM \"{table}\" WHERE \"{column}\" IS NOT NULL";
                        await using var reader = await read.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            var value = decimal.Parse(reader.GetString(1), NumberStyles.Float, CultureInfo.InvariantCulture);
                            var rounded = Math.Round(value, scale, MidpointRounding.AwayFromZero);
                            if (value != rounded) updates.Add((reader.GetInt64(0), rounded));
                        }
                    }
                    foreach (var (rowId, value) in updates)
                    {
                        await using var update = db.Database.GetDbConnection().CreateCommand();
                        update.Transaction = transaction;
                        update.CommandText = $"UPDATE \"{table}\" SET \"{column}\" = @value WHERE rowid = @rowid";
                        AddParameter(update, "@value", value.ToString(CultureInfo.InvariantCulture));
                        AddParameter(update, "@rowid", rowId);
                        await update.ExecuteNonQueryAsync();
                    }
                }
            }
            if (transaction is not null)
            {
                await using var version = db.Database.GetDbConnection().CreateCommand();
                version.Transaction = transaction;
                version.CommandText = "INSERT INTO DecimalStorageVersion (Version) VALUES (2)";
                await version.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
        db.ChangeTracker.Clear();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static async Task AddProductionColumns(AppDb db)
    {
        if (!await HasTable(db, "ProjectWoodPart")) return;
        if (!await HasColumn(db, "ProjectWoodPart", "PartId"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectWoodPart ADD COLUMN PartId varchar(36) NULL");
        if (!await HasColumn(db, "ProjectWoodPart", "IsProduced"))
            await db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite()
                ? "ALTER TABLE ProjectWoodPart ADD COLUMN IsProduced INTEGER NOT NULL DEFAULT 0"
                : "ALTER TABLE ProjectWoodPart ADD COLUMN IsProduced tinyint(1) NOT NULL DEFAULT 0");
        var parts = await db.Set<ProjectWoodPart>().Where(p => p.PartId == null).ToListAsync();
        foreach (var part in parts) part.PartId = Guid.NewGuid().ToString();
        await db.SaveChangesAsync();
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
        if (await HasTable(db, "PricingDefaults"))
        {
            if (!await HasColumn(db, "PricingDefaults", "LamellaLengthExtraMm"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE PricingDefaults ADD COLUMN LamellaLengthExtraMm decimal(18,1) NOT NULL DEFAULT 50");
            if (!await HasColumn(db, "PricingDefaults", "LamellaMergeToleranceMm"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE PricingDefaults ADD COLUMN LamellaMergeToleranceMm decimal(18,1) NOT NULL DEFAULT 50");
            if (!await HasColumn(db, "PricingDefaults", "GlueBoardWastePercent"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE PricingDefaults ADD COLUMN GlueBoardWastePercent decimal(18,1) NOT NULL DEFAULT 10");
        }
        if (!await HasTable(db, "ProjectDetails")) return;
        if (!await HasColumn(db, "ProjectDetails", "LamellaLengthExtraMm"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectDetails ADD COLUMN LamellaLengthExtraMm decimal(18,1) NOT NULL DEFAULT 50");
        if (!await HasColumn(db, "ProjectDetails", "LamellaMergeToleranceMm"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectDetails ADD COLUMN LamellaMergeToleranceMm decimal(18,1) NOT NULL DEFAULT 50");
        if (!await HasColumn(db, "ProjectDetails", "GlueBoardWastePercent"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectDetails ADD COLUMN GlueBoardWastePercent decimal(18,1) NOT NULL DEFAULT 10");
    }

    private static async Task AddCuttingPlanColumns(AppDb db)
    {
        foreach (var table in new[] { "ProjectDetails", "PricingDefaults" })
        {
            if (!await HasTable(db, table)) continue;
            foreach (var (column, value) in new[] { ("LamellaMinLengthMm", 350), ("LamellaMinWidthMm", 50), ("LamellaMaxWidthMm", 110), ("LamellaWidthLossMm", 4), ("GlueBoardMaxWidthMm", 900), ("OffcutMinWidthMm", 50), ("OffcutMinLengthMm", 350), ("TransportMaxLengthMm", 2700) })
                if (!await HasColumn(db, table, column))
                {
                    // Identifiers and defaults come exclusively from the constants above.
                    var sql = $"ALTER TABLE {table} ADD COLUMN {column} INTEGER NOT NULL DEFAULT {value}";
                    await db.Database.ExecuteSqlRawAsync(sql);
                }
        }
        if (!await HasColumn(db, "StockBoards", "ThicknessMm"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE StockBoards ADD COLUMN ThicknessMm INTEGER NULL");
    }

    private static async Task AddWorkDateColumns(AppDb db)
    {
        if (await HasTable(db, "ProjectCostLine") && !await HasColumn(db, "ProjectCostLine", "WorkDate"))
        {
            if (db.Database.IsSqlite()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectCostLine ADD COLUMN WorkDate TEXT NULL");
            else await db.Database.ExecuteSqlRawAsync("ALTER TABLE ProjectCostLine ADD COLUMN WorkDate date NULL");
        }
        if (await HasTable(db, "InvoiceCalculatedLine") && !await HasColumn(db, "InvoiceCalculatedLine", "WorkDate"))
        {
            if (db.Database.IsSqlite()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE InvoiceCalculatedLine ADD COLUMN WorkDate TEXT NULL");
            else await db.Database.ExecuteSqlRawAsync("ALTER TABLE InvoiceCalculatedLine ADD COLUMN WorkDate date NULL");
        }
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
