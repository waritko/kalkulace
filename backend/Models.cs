namespace Kalkulace.Api;

public record WoodPart(string Name, string WoodType, decimal WidthMm, decimal LengthMm, decimal ThicknessMm, decimal Quantity, decimal PricePerM3, string? Finish, bool ApplyFinish = false);
public record CostLine(string Name, string Category, string Unit, decimal Quantity, decimal UnitPrice, int VatRate, string? ServiceCategory = null, bool UsesExtraction = false, bool UsesVacuum = false, string? AutomaticMachineryCharge = null, string? MaterialType = null, bool AutomaticFinish = false);
public record ProjectInput(string Name, string? CustomerName, decimal BudgetLimit, bool NonVatPayer, decimal WoodReservePercent, decimal MaterialOverheadPercent, decimal MaterialMarginPercent, decimal LaborMarginPercent, decimal ServiceMarginPercent, decimal FinanceMarginPercent, decimal DiscountPercent, List<WoodPart> WoodParts, List<CostLine> Lines, decimal LamellaLengthExtraMm = 50m, decimal LamellaMergeToleranceMm = 50m, decimal GlueBoardWastePercent = 10m);
public record ProjectListItem(int Id, string Name, string CustomerName, DateTimeOffset UpdatedAt);
public record ProjectResponse(int Id, ProjectInput Input, Calculation Result, DateTimeOffset UpdatedAt)
{
    public static ProjectResponse From(Project project)
    {
        var input = project.ToInput();
        return new(project.Id, input, Calculator.Calculate(input), project.UpdatedAt);
    }
}
public record CalculatedLine(string Name, string Category, string Unit, decimal Quantity, decimal UnitPrice, decimal Cost, int VatRate, decimal AreaM2, decimal VolumeM3, decimal? BoardThicknessMm);
public record VatSummary(int Rate, decimal Base, decimal Vat, decimal Total);
public record WoodPurchaseItem(string WoodType, decimal BoardThicknessMm, decimal AreaM2, decimal VolumeM3, decimal Width3mCm, decimal Width4mCm);
public record GlueBoardPurchaseItem(string WoodType, decimal ThicknessMm, decimal LamellaLengthMm, decimal TotalWidthMm);
public record Calculation(List<CalculatedLine> Lines, decimal MaterialCost, decimal LaborCost, decimal ServiceCost, decimal FinanceCost, decimal WoodCost, decimal WoodVolumeM3, decimal Overhead, decimal Profit, decimal TotalCost, decimal TotalWithoutVat, decimal TotalVat, decimal TotalWithVat, decimal BudgetDifference, decimal BudgetUsagePercent, List<VatSummary> Vat)
{
    public List<WoodPurchaseItem> WoodPurchase { get; init; } = [];
    public List<GlueBoardPurchaseItem> GlueBoardPurchase { get; init; } = [];
}
public record InvoiceRequest(string Number, DateOnly IssuedOn, DateOnly DueOn, string CustomerName, string? CustomerAddress, string SupplierName, string? SupplierAddress, string? SupplierIco, string? SupplierDic, string? BankAccount, string? Note);
public record InvoiceStatusRequest(string Status);
public record InvoiceListItem(int Id, int ProjectId, string Number, string CustomerName, DateOnly IssuedOn, DateOnly DueOn, decimal Total, string Status);
public record InvoiceResponse(int Id, int ProjectId, string Number, DateOnly IssuedOn, DateOnly DueOn, string CustomerName, string CustomerAddress, string SupplierName, string SupplierAddress, string SupplierIco, string SupplierDic, string BankAccount, string Note, string Status, string ProjectName, Calculation Result)
{
    public static InvoiceResponse From(Invoice invoice) => new(invoice.Id, invoice.ProjectId, invoice.Number, invoice.IssuedOn, invoice.DueOn, invoice.CustomerName, invoice.CustomerAddress, invoice.SupplierName, invoice.SupplierAddress, invoice.SupplierIco, invoice.SupplierDic, invoice.BankAccount, invoice.Note, invoice.Status, invoice.ProjectName, invoice.ToCalculation());
}

public static class Validator
{
    public static Dictionary<string, string[]> Validate(ProjectInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Name)) errors["name"] = ["Zadejte název zakázky."];
        if (input.WoodParts is null || input.Lines is null) errors["lines"] = ["Položky musí být zadány jako seznam."];
        if (input.WoodParts?.Count > 500 || input.Lines?.Count > 1000) errors["lines"] = ["Příliš mnoho položek."];
        if (input.BudgetLimit < 0) errors["budgetLimit"] = ["Finanční limit musí být nezáporný."];
        var percents = new[] { input.WoodReservePercent, input.MaterialOverheadPercent, input.MaterialMarginPercent, input.LaborMarginPercent, input.ServiceMarginPercent, input.FinanceMarginPercent, input.DiscountPercent };
        if (percents.Any(p => p < 0 || p > 100)) errors["percent"] = ["Procenta musí být v rozsahu 0–100."];
        if (input.GlueBoardWastePercent < 0 || input.GlueBoardWastePercent > 100 || input.LamellaLengthExtraMm < 0 || input.LamellaMergeToleranceMm < 0)
            errors["glueBoard"] = ["Prořez musí být v rozsahu 0–100 % a přídavek i tolerance délky musí být nezáporné."];
        if (input.WoodParts?.Any(p => string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.WoodType) || p.WidthMm <= 0 || p.LengthMm <= 0 || p.ThicknessMm <= 0 || p.Quantity <= 0 || p.PricePerM3 < 0) == true) errors["woodParts"] = ["Vyplňte název, dřevinu, kladné rozměry a počet; cena nesmí být záporná."];
        if (input.Lines?.Any(l => string.IsNullOrWhiteSpace(l.Name) || l.Quantity <= 0 || l.UnitPrice < 0 || l.Category is not ("material" or "labor" or "service" or "finance") || (l.MaterialType is not null && (l.Category != "material" || l.MaterialType is not ("fastener" or "finish" or "abrasive"))) || (l.ServiceCategory is not null && (l.Category != "service" || l.ServiceCategory is not ("transport" or "machinery" or "other"))) || l.VatRate is not (12 or 21) || ((l.UsesExtraction || l.UsesVacuum || l.AutomaticMachineryCharge is not null) && (l.Category != "service" || l.ServiceCategory != "machinery")) || (l.AutomaticMachineryCharge is not null && l.AutomaticMachineryCharge is not (MachineryCharges.Extraction or MachineryCharges.Vacuum)) || (l.AutomaticFinish && (l.Category != "material" || l.MaterialType != "finish"))) == true) errors["lines"] = ["Položky musí mít název, kladné množství, nezápornou cenu, platnou kategorii a sazbu DPH."];
        return errors;
    }
}

public static class Calculator
{
    static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    public static Calculation Calculate(ProjectInput input)
    {
        var lines = new List<CalculatedLine>();
        decimal woodVolume = 0;
        foreach (var part in input.WoodParts)
        {
            var area = part.WidthMm * part.LengthMm * part.Quantity / 1_000_000m;
            var board = part.ThicknessMm < 29 ? 32m : 50m;
            var volume = area * board / 1000m;
            woodVolume += volume;
            lines.Add(new(part.Name, "material", "m³", volume, part.PricePerM3, Round(volume * part.PricePerM3 * (1 + input.WoodReservePercent / 100m)), 21, area, volume, board));
        }
        foreach (var line in input.Lines) lines.Add(new(line.Name, line.Category, line.Unit, line.Quantity, line.UnitPrice, Round(line.Quantity * line.UnitPrice), line.VatRate, 0, 0, null));

        var material = lines.Where(l => l.Category == "material").Sum(l => l.Cost);
        var labor = lines.Where(l => l.Category == "labor").Sum(l => l.Cost);
        var service = lines.Where(l => l.Category == "service").Sum(l => l.Cost);
        var finance = lines.Where(l => l.Category == "finance").Sum(l => l.Cost);
        var overhead = Round(material * input.MaterialOverheadPercent / 100m);
        var totalCost = material + labor + service + finance + overhead;
        var profit = Round(material * input.MaterialMarginPercent / 100m + labor * input.LaborMarginPercent / 100m + service * input.ServiceMarginPercent / 100m + finance * input.FinanceMarginPercent / 100m);
        var rateGroups = lines.GroupBy(l => l.VatRate).ToDictionary(g => g.Key, g => g.Sum(l => l.Cost));
        var vat = new List<VatSummary>();
        foreach (var rate in new[] { 12, 21 })
        {
            var baseCost = rateGroups.GetValueOrDefault(rate);
            // Overhead and margin are allocated in proportion to the base costs of each VAT group.
            var allocation = material + labor + service + finance == 0 ? 0 : (overhead + profit) * baseCost / (material + labor + service + finance);
            var beforeDiscount = baseCost + allocation;
            var taxable = Round(beforeDiscount * (1 - input.DiscountPercent / 100m));
            var tax = input.NonVatPayer ? 0 : Round(taxable * rate / 100m);
            vat.Add(new(rate, taxable, tax, taxable + tax));
        }
        var withoutVat = vat.Sum(v => v.Base);
        var totalVat = vat.Sum(v => v.Vat);
        var withVat = withoutVat + totalVat;
        var woodPurchase = input.WoodParts
            .GroupBy(part => (WoodType: part.WoodType.Trim(), BoardThicknessMm: part.ThicknessMm < 29 ? 32m : 50m))
            .Select(group =>
            {
                var area = group.Sum(part => part.WidthMm * part.LengthMm * part.Quantity / 1_000_000m);
                return new WoodPurchaseItem(group.Key.WoodType, group.Key.BoardThicknessMm, area,
                    area * group.Key.BoardThicknessMm / 1000m,
                    Math.Ceiling(area / 3m * 10m) * 10m,
                    Math.Ceiling(area / 4m * 10m) * 10m);
            })
            .OrderBy(item => item.WoodType, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.BoardThicknessMm)
            .ToList();
        var glueBoardPurchase = new List<GlueBoardPurchaseItem>();
        foreach (var group in input.WoodParts.GroupBy(part => (WoodType: part.WoodType.Trim(), part.ThicknessMm)))
        {
            // Work from the longest piece down. Every piece in a row stays within the
            // tolerance of the row's purchasing length, including at cluster boundaries.
            foreach (var part in group.OrderByDescending(part => Math.Max(part.WidthMm, part.LengthMm)))
            {
                var length = Math.Max(part.WidthMm, part.LengthMm) + input.LamellaLengthExtraMm;
                var width = Math.Min(part.WidthMm, part.LengthMm) * part.Quantity;
                var row = glueBoardPurchase.FindIndex(item => item.WoodType == group.Key.WoodType &&
                    item.ThicknessMm == group.Key.ThicknessMm && item.LamellaLengthMm - length <= input.LamellaMergeToleranceMm && item.LamellaLengthMm >= length);
                if (row < 0) glueBoardPurchase.Add(new(group.Key.WoodType, group.Key.ThicknessMm, length, width));
                else glueBoardPurchase[row] = glueBoardPurchase[row] with { TotalWidthMm = glueBoardPurchase[row].TotalWidthMm + width };
            }
        }
        glueBoardPurchase = glueBoardPurchase
            .Select(item => item with { TotalWidthMm = Math.Ceiling(item.TotalWidthMm * (1 + input.GlueBoardWastePercent / 100m)) })
            .OrderBy(item => item.WoodType, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.ThicknessMm)
            .ThenByDescending(item => item.LamellaLengthMm)
            .ToList();
        return new(lines, material, labor, service, finance, lines.Where(l => l.VolumeM3 > 0).Sum(l => l.Cost), woodVolume, overhead, profit, totalCost, withoutVat, totalVat, withVat, input.BudgetLimit - withVat, input.BudgetLimit == 0 ? 0 : Round(withVat / input.BudgetLimit * 100m), vat)
        { WoodPurchase = woodPurchase, GlueBoardPurchase = glueBoardPurchase };
    }
}

public record CatalogItem(string Category, string Name, string Unit, decimal UnitPrice, int VatRate, string? ServiceCategory = null, bool UsesExtraction = false, bool UsesVacuum = false, string? MaterialType = null);
public record WoodPrice(string Name, decimal Price32, decimal Price50);
public record CatalogData(List<WoodPrice> Wood, List<CatalogItem> Items);
public static class MaterialTypes
{
    public static string Infer(string name) => name switch
    {
        "Osmo" or "Houba na povrchovku" => "finish",
        "Brusný výsek 150 mm" or "Brusný výsek houbička" or "Brusný pás" => "abrasive",
        _ => "fastener"
    };
    public static CatalogData EnsurePresent(CatalogData data) => data with
    {
        Items = data.Items.Select(item => item.Category == "material" && item.MaterialType is null
            ? item with { MaterialType = Infer(item.Name) } : item).ToList()
    };
}
public static class FinishCharges
{
    public static ProjectInput Sync(ProjectInput input, CatalogData catalog)
    {
        var lines = input.Lines.Where(line => !line.AutomaticFinish).ToList();
        var area = input.WoodParts.Where(part => part.ApplyFinish)
            .Sum(part => part.WidthMm * part.LengthMm * part.Quantity / 1_000_000m);
        var finish = catalog.Items.FirstOrDefault(item => item.Category == "material" && item.MaterialType == "finish");
        if (area > 0 && finish is not null)
        {
            var existing = input.Lines.FirstOrDefault(line => line.AutomaticFinish);
            lines.Add(existing is null
                ? new CostLine(finish.Name, "material", finish.Unit, area * 2m * 1.1m, finish.UnitPrice, finish.VatRate, MaterialType: "finish", AutomaticFinish: true)
                : existing with { Name = finish.Name, Category = "material", Unit = finish.Unit, Quantity = area * 2m * 1.1m, MaterialType = "finish" });
        }
        return input with { Lines = lines };
    }
}
public static class MachineryCharges
{
    public const string Extraction = "Odsávání";
    public const string Vacuum = "Vysavač";
    public static CatalogData EnsurePresent(CatalogData data)
    {
        var items = new List<CatalogItem>(data.Items);
        foreach (var name in new[] { Extraction, Vacuum })
        {
            var index = items.FindIndex(i => i.Category == "service" && i.ServiceCategory == "machinery" && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) items[index] = items[index] with { Name = name, Unit = "hod", UsesExtraction = false, UsesVacuum = false };
            else
                items.Add(new("service", name, "hod", 0, 21, "machinery"));
        }
        return data with { Items = items };
    }
    public static ProjectInput Sync(ProjectInput input, CatalogData catalog)
    {
        var ordinary = input.Lines.Where(line => line.AutomaticMachineryCharge is null).ToList();
        var automatic = new List<CostLine>();
        foreach (var name in new[] { Extraction, Vacuum })
        {
            var quantity = ordinary.Where(line => line.Category == "service" && line.ServiceCategory == "machinery" && (name == Extraction ? line.UsesExtraction : line.UsesVacuum)).Sum(line => line.Quantity);
            if (quantity <= 0) continue;
            var existing = input.Lines.FirstOrDefault(line => line.AutomaticMachineryCharge == name);
            var price = catalog.Items.FirstOrDefault(item => item.Category == "service" && item.ServiceCategory == "machinery" && item.Name == name);
            automatic.Add(existing is null
                ? new CostLine(name, "service", "hod", quantity, price?.UnitPrice ?? 0, price?.VatRate ?? 21, "machinery", AutomaticMachineryCharge: name)
                : existing with { Name = name, Category = "service", ServiceCategory = "machinery", Unit = "hod", Quantity = quantity });
        }
        return input with { Lines = [.. ordinary, .. automatic] };
    }
}
public static class CatalogValidator
{
    public static Dictionary<string, string[]> Validate(CatalogData data)
    {
        var errors = new Dictionary<string, string[]>();
        if (data.Wood is null || data.Items is null || data.Wood.Count > 500 || data.Items.Count > 1000)
            errors["catalog"] = ["Neplatný počet položek ceníku."];
        if (data.Wood is null || data.Items is null) return errors;
        if (data.Wood.Any(w => string.IsNullOrWhiteSpace(w.Name) || w.Price32 < 0 || w.Price50 < 0) ||
            data.Wood.Select(w => w.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Wood.Count)
            errors["wood"] = ["Dřeviny musí mít jedinečný název a nezáporné ceny."];
        if (data.Items.Any(i => string.IsNullOrWhiteSpace(i.Name) || string.IsNullOrWhiteSpace(i.Unit) || i.UnitPrice < 0 ||
            i.VatRate is not (12 or 21) || i.Category is not ("material" or "labor" or "service" or "finance") ||
            (i.ServiceCategory is not null && (i.Category != "service" || i.ServiceCategory is not ("transport" or "machinery" or "other"))) ||
            (i.MaterialType is not null && (i.Category != "material" || i.MaterialType is not ("fastener" or "finish" or "abrasive"))) ||
            ((i.UsesExtraction || i.UsesVacuum) && (i.Category != "service" || i.ServiceCategory != "machinery" || i.Name is MachineryCharges.Extraction or MachineryCharges.Vacuum))) ||
            data.Items.Select(i => $"{i.Category}/{i.ServiceCategory}/{i.Name.Trim()}").Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Items.Count)
            errors["items"] = ["Položky musí mít jedinečný název v kategorii, jednotku, nezápornou cenu a platnou sazbu DPH."];
        return errors;
    }
}
public static class Catalog
{
    public static readonly CatalogData All = new(
        [new("Dub", 21900, 29900), new("Buk", 11000, 12300), new("Jasan", 13000, 14500), new("Javor", 13000, 14000), new("Smrk", 11500, 12300), new("Jedle", 0, 12100), new("Borovice", 11000, 11500), new("Modřín", 11500, 12500), new("Olše", 10500, 11500), new("Ořešák", 24000, 30500), new("Bříza", 8500, 8900), new("Topol", 8000, 8400), new("Lípa", 10000, 10500), new("Třešeň", 15000, 16000)],
        [new("material", "Lepidlo", "akce", 50, 21), new("material", "Kolík 8×40", "ks", 0.35m, 21), new("material", "Šroub M6", "ks", 1.5m, 21), new("material", "Insert M6", "ks", 0.5m, 21), new("material", "Osmo", "m²", 37.32m, 21), new("material", "Brusný výsek 150 mm", "ks", 15, 21), new("material", "Brusný výsek houbička", "ks", 8, 21), new("material", "Brusný pás", "ks", 70, 21), new("material", "Houba na povrchovku", "ks", 20, 21), new("labor", "Práce truhláře", "hod", 500, 21), new("labor", "Pomocné práce", "hod", 400, 21), new("service", "Osobní auto", "km", 9, 21, "transport"), new("service", "Dodávka", "km", 17, 21, "transport"), new("service", "Malý vozík", "km", 3, 21, "transport"), new("service", "Odsávání", "hod", 0, 21, "machinery"), new("service", "Vysavač", "hod", 0, 21, "machinery"), new("service", "Pokosová pila", "hod", 30, 21, "machinery"), new("service", "Formátovací pila", "hod", 260, 21, "machinery"), new("service", "Hoblovka", "hod", 140, 21, "machinery"), new("service", "Frézka", "hod", 250, 21, "machinery"), new("finance", "Kalkulace", "hod", 600, 21), new("finance", "Fakturace", "hod", 600, 21), new("finance", "Návrh projektu", "hod", 600, 21)]);
}
