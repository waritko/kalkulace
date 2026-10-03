namespace Kalkulace.Api;

public static class EntityMapping
{
    public static ProjectInput ToInput(this Project project)
    {
        var d = project.Details;
        return new(project.Name, project.CustomerName, d.BudgetLimit, d.NonVatPayer, d.WoodReservePercent,
            d.MaterialOverheadPercent, d.MaterialMarginPercent, d.LaborMarginPercent, d.ServiceMarginPercent,
            d.FinanceMarginPercent, d.DiscountPercent,
            project.WoodParts.OrderBy(p => p.Position).Select(p => new WoodPart(p.Name, p.WoodType, p.WidthMm,
                p.LengthMm, p.ThicknessMm, p.Quantity, p.PricePerM3, p.Finish)).ToList(),
            project.Lines.OrderBy(l => l.Position).Select(l => new CostLine(l.Name, l.Category, l.Unit, l.Quantity,
                l.UnitPrice, l.VatRate, l.ServiceCategory, l.UsesExtraction, l.UsesVacuum, l.AutomaticMachineryCharge, l.MaterialType)).ToList());
    }

    public static void SetInput(this Project project, ProjectInput input)
    {
        project.Name = input.Name.Trim();
        project.CustomerName = input.CustomerName?.Trim() ?? "";
        project.Details ??= new ProjectDetails { ProjectId = project.Id };
        project.Details.BudgetLimit = input.BudgetLimit;
        project.Details.NonVatPayer = input.NonVatPayer;
        project.Details.WoodReservePercent = input.WoodReservePercent;
        project.Details.MaterialOverheadPercent = input.MaterialOverheadPercent;
        project.Details.MaterialMarginPercent = input.MaterialMarginPercent;
        project.Details.LaborMarginPercent = input.LaborMarginPercent;
        project.Details.ServiceMarginPercent = input.ServiceMarginPercent;
        project.Details.FinanceMarginPercent = input.FinanceMarginPercent;
        project.Details.DiscountPercent = input.DiscountPercent;
        project.WoodParts = input.WoodParts.Select((p, n) => new ProjectWoodPart
        {
            ProjectId = project.Id, Position = n, Name = p.Name, WoodType = p.WoodType, WidthMm = p.WidthMm,
            LengthMm = p.LengthMm, ThicknessMm = p.ThicknessMm, Quantity = p.Quantity, PricePerM3 = p.PricePerM3,
            Finish = p.Finish
        }).ToList();
        project.Lines = input.Lines.Select((l, n) => new ProjectCostLine
        {
            ProjectId = project.Id, Position = n, Name = l.Name, Category = l.Category, Unit = l.Unit,
            Quantity = l.Quantity, UnitPrice = l.UnitPrice, VatRate = l.VatRate, ServiceCategory = l.ServiceCategory,
            UsesExtraction = l.UsesExtraction, UsesVacuum = l.UsesVacuum, AutomaticMachineryCharge = l.AutomaticMachineryCharge, MaterialType = l.MaterialType
        }).ToList();
    }

    public static void SetCalculation(this Invoice invoice, Calculation result)
    {
        invoice.Calculation = new InvoiceCalculation
        {
            InvoiceId = invoice.Id, MaterialCost = result.MaterialCost, LaborCost = result.LaborCost,
            ServiceCost = result.ServiceCost, FinanceCost = result.FinanceCost, WoodCost = result.WoodCost,
            WoodVolumeM3 = result.WoodVolumeM3, Overhead = result.Overhead, Profit = result.Profit,
            TotalCost = result.TotalCost, TotalWithoutVat = result.TotalWithoutVat, TotalVat = result.TotalVat,
            TotalWithVat = result.TotalWithVat, BudgetDifference = result.BudgetDifference,
            BudgetUsagePercent = result.BudgetUsagePercent
        };
        invoice.Lines = result.Lines.Select((l, n) => new InvoiceCalculatedLine
        {
            InvoiceId = invoice.Id, Position = n, Name = l.Name, Category = l.Category, Unit = l.Unit,
            Quantity = l.Quantity, UnitPrice = l.UnitPrice, Cost = l.Cost, VatRate = l.VatRate,
            AreaM2 = l.AreaM2, VolumeM3 = l.VolumeM3, BoardThicknessMm = l.BoardThicknessMm
        }).ToList();
        invoice.Vat = result.Vat.Select((v, n) => new InvoiceVatSummary
        {
            InvoiceId = invoice.Id, Position = n, Rate = v.Rate, Base = v.Base, Vat = v.Vat, Total = v.Total
        }).ToList();
    }

    public static Calculation ToCalculation(this Invoice invoice)
    {
        var c = invoice.Calculation;
        return new(invoice.Lines.OrderBy(l => l.Position).Select(l => new CalculatedLine(l.Name, l.Category,
                l.Unit, l.Quantity, l.UnitPrice, l.Cost, l.VatRate, l.AreaM2, l.VolumeM3, l.BoardThicknessMm)).ToList(),
            c.MaterialCost, c.LaborCost, c.ServiceCost, c.FinanceCost, c.WoodCost, c.WoodVolumeM3,
            c.Overhead, c.Profit, c.TotalCost, c.TotalWithoutVat, c.TotalVat, c.TotalWithVat,
            c.BudgetDifference, c.BudgetUsagePercent,
            invoice.Vat.OrderBy(v => v.Position).Select(v => new VatSummary(v.Rate, v.Base, v.Vat, v.Total)).ToList());
    }

    public static CatalogData ToData(this CatalogState state) => new(
        state.Wood.OrderBy(w => w.Position).Select(w => new WoodPrice(w.Name, w.Price32, w.Price50)).ToList(),
        state.Items.OrderBy(i => i.Position).Select(i => new CatalogItem(i.Category, i.Name, i.Unit, i.UnitPrice,
            i.VatRate, i.ServiceCategory, i.UsesExtraction, i.UsesVacuum, i.MaterialType)).ToList());

    public static void SetData(this CatalogState state, CatalogData data)
    {
        state.Wood = data.Wood.Select((w, n) => new CatalogWood
        {
            CatalogStateId = state.Id, Position = n, Name = w.Name, Price32 = w.Price32, Price50 = w.Price50
        }).ToList();
        state.Items = data.Items.Select((i, n) => new CatalogItemRow
        {
            CatalogStateId = state.Id, Position = n, Category = i.Category, Name = i.Name, Unit = i.Unit,
            UnitPrice = i.UnitPrice, VatRate = i.VatRate, ServiceCategory = i.ServiceCategory,
            UsesExtraction = i.UsesExtraction, UsesVacuum = i.UsesVacuum, MaterialType = i.MaterialType
        }).ToList();
    }
}
