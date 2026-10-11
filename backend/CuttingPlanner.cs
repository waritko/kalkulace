namespace Kalkulace.Api;

public record PlannedLamella(int BoardId, string WoodType, int BoardThicknessMm, int LengthMm, int WidthMm, int OffsetWidthMm, int OffsetLengthMm);
public record CuttingShortage(string WoodType, decimal BoardThicknessMm, decimal LengthMm, decimal MissingWidthMm);
public record CuttingPlan(bool Complete, int KerfMm, List<PlannedLamella> Lamellas, List<CuttingShortage> Shortages, List<string> Warnings);

public static class CuttingPlanner
{
    public const string SettingsError = "Nejmenší délka a šířky lamely musí být kladné celé milimetry; největší šířka nesmí být menší než nejmenší.";
    private const int Kerf = 3;
    private const int MaxLamellas = 20_000;
    public static bool ValidSettings(int length, int minWidth, int maxWidth) => length > 0 && minWidth > 0 && maxWidth >= minWidth;

    // A free rectangle retains its source coordinates. Cut across the grain first,
    // then rip the resulting section; never reuse material allocated to a lamella.
    private record FreePiece(StockBoard Board, int Width, int Length, int X = 0, int Y = 0, bool CanShorten = true);

    public static CuttingPlan Create(ProjectInput input, IReadOnlyList<StockBoard> stock)
    {
        var requirements = Calculator.Calculate(input).GlueBoardPurchase
            .Select(r => r with { LamellaLengthMm = Math.Max(input.LamellaMinLengthMm, Math.Ceiling(r.LamellaLengthMm)) })
            .GroupBy(r => (r.WoodType, r.BoardThicknessMm, r.LamellaLengthMm))
            .Select(g => g.First() with { TotalWidthMm = g.Sum(r => r.TotalWidthMm) })
            .OrderByDescending(r => r.LamellaLengthMm).ThenBy(r => r.WoodType).ThenBy(r => r.BoardThicknessMm).ToList();
        var pieces = stock.Where(b => !b.IsCut && b.ThicknessMm > 0)
            .Select(b => new FreePiece(b, b.WidthMm, b.LengthMm, CanShorten: b.Stage != "width")).ToList();
        var lamellas = new List<PlannedLamella>();
        var shortages = new List<CuttingShortage>();
        var warnings = new List<string>();
        var unknown = stock.Count(b => !b.IsCut && b.ThicknessMm is null);
        if (unknown > 0) warnings.Add($"U {unknown} dostupných kusů není zadána tloušťka. Doplňte ji ve skladu; tyto kusy nebyly použity.");

        foreach (var requirement in requirements)
        {
            var remaining = requirement.TotalWidthMm;
            while (remaining > 0 && lamellas.Count < MaxLamellas)
            {
                var piece = pieces.Where(p => string.Equals(p.Board.WoodType.Trim(), requirement.WoodType, StringComparison.OrdinalIgnoreCase)
                        && p.Board.ThicknessMm == requirement.BoardThicknessMm && p.Length >= requirement.LamellaLengthMm
                        && p.Width >= input.LamellaMinWidthMm)
                    .OrderBy(p => p.Length - requirement.LamellaLengthMm).ThenBy(p => p.Width).ThenBy(p => p.Board.Id).FirstOrDefault();
                if (piece is null) break;
                pieces.Remove(piece);
                // Width-cut stock cannot be shortened under the stock's cut-order rules.
                var length = piece.CanShorten ? (int)requirement.LamellaLengthMm : piece.Length;
                var slots = (int)Math.Ceiling(((decimal)piece.Width + Kerf) / ((decimal)input.LamellaMaxWidthMm + Kerf));
                var balancedWidth = (int)(((long)piece.Width - (long)(slots - 1) * Kerf) / slots);
                var width = Math.Min(input.LamellaMaxWidthMm, balancedWidth >= input.LamellaMinWidthMm ? balancedWidth : piece.Width);
                width = (int)Math.Min(width, Math.Max(input.LamellaMinWidthMm, Math.Ceiling(remaining)));
                lamellas.Add(new(piece.Board.Id, piece.Board.WoodType, piece.Board.ThicknessMm!.Value, length, width, piece.X, piece.Y));
                remaining -= width;
                var restLength = piece.Length - length - Kerf;
                if (restLength > 0)
                    pieces.Add(new(piece.Board, piece.Width, restLength, piece.X, piece.Y + length + Kerf));
                var restWidth = piece.Width - width - Kerf;
                if (restWidth >= input.LamellaMinWidthMm)
                    pieces.Add(new(piece.Board, restWidth, length, piece.X + width + Kerf, piece.Y, CanShorten: false));
            }
            if (remaining > 0) shortages.Add(new(requirement.WoodType, requirement.BoardThicknessMm, requirement.LamellaLengthMm, remaining));
        }
        if (lamellas.Count == MaxLamellas) warnings.Add("Plán dosáhl limitu 20 000 lamel. Rozdělte zakázku na menší části.");
        return new(shortages.Count == 0, Kerf, lamellas, shortages, warnings);
    }
}
