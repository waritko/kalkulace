using Microsoft.EntityFrameworkCore.Metadata;

namespace Kalkulace.Api;

public static class DecimalStorage
{
    // Invoice quantities, areas and volumes retain their original precision.
    public static int? Scale(IReadOnlyProperty property)
    {
        if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) != typeof(decimal)) return null;
        var name = property.Name;
        if (name == "Quantity" && property.DeclaringType.ClrType == typeof(ProjectWoodPart)) return 0;
        if (name == "Quantity" && property.DeclaringType.ClrType == typeof(ProjectCostLine)) return 6;
        if (name.EndsWith("Mm", StringComparison.Ordinal) || name.EndsWith("Percent", StringComparison.Ordinal)) return 1;
        return name is "BudgetLimit" or "PricePerM3" or "Price32" or "Price50" or "UnitPrice"
            or "MaterialCost" or "LaborCost" or "ServiceCost" or "FinanceCost" or "WoodCost"
            or "Overhead" or "Profit" or "TotalCost" or "TotalWithoutVat" or "TotalVat"
            or "TotalWithVat" or "BudgetDifference" or "Cost" or "Base" or "Vat" or "Total" ? 2 : null;
    }

    // Recover minute values within half a unit of the stored hourly precision.
    // Otherwise an exact quarter hour assembled from rounded rows could bill twice.
    public static decimal RestoreMinutePrecision(decimal hours)
    {
        var minutes = Math.Round(hours * 60m, 0, MidpointRounding.AwayFromZero) / 60m;
        return Math.Abs(hours - minutes) <= 0.0000005m ? minutes : hours;
    }
}
