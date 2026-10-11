using Microsoft.EntityFrameworkCore.Metadata;

namespace Kalkulace.Api;

public static class DecimalStorage
{
    // Quantities, areas and volumes must retain their original precision.
    public static int? Scale(IReadOnlyProperty property)
    {
        if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) != typeof(decimal)) return null;
        var name = property.Name;
        if (name.EndsWith("Mm", StringComparison.Ordinal) || name.EndsWith("Percent", StringComparison.Ordinal)) return 1;
        return name is "BudgetLimit" or "PricePerM3" or "Price32" or "Price50" or "UnitPrice"
            or "MaterialCost" or "LaborCost" or "ServiceCost" or "FinanceCost" or "WoodCost"
            or "Overhead" or "Profit" or "TotalCost" or "TotalWithoutVat" or "TotalVat"
            or "TotalWithVat" or "BudgetDifference" or "Cost" or "Base" or "Vat" or "Total" ? 2 : null;
    }
}
