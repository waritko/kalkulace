namespace Kalkulace.Api;

public record PlannedLamella(int BoardId, string WoodType, int BoardThicknessMm, int LengthMm, int WidthMm, int OffsetWidthMm, int OffsetLengthMm, int FinishedWidthMm, int PanelId);
public record CuttingShortage(string WoodType, decimal BoardThicknessMm, decimal LengthMm, decimal MissingWidthMm);
public record PlannedOffcut(int BoardId, string WoodType, int BoardThicknessMm, int LengthMm, int WidthMm, int OffsetWidthMm, int OffsetLengthMm);
public record CuttingPlan(bool Complete, int KerfMm, List<PlannedLamella> Lamellas, List<CuttingShortage> Shortages, List<string> Warnings, List<PlannedPanel> Panels, List<PlannedOffcut> Offcuts);
public record PanelPart(int PartIndex, string Name, decimal LengthMm, decimal WidthMm, decimal OffsetLengthMm, decimal OffsetWidthMm);
public record PlannedPanel(int Id, string WoodType, decimal BoardThicknessMm, decimal LengthMm, decimal WidthMm, List<PanelPart> Parts);

public static class CuttingPlanner
{
    public const string SettingsError = "Nejmenší délka a šířky lamely musí být kladné celé milimetry; největší šířka nesmí být menší než nejmenší. Největší šířka spárovky musí být kladná v celých milimetrech. Úběr šířky při srovnávání musí být nezáporný a menší než nejmenší šířka lamely.";
    private const int Kerf = 3;
    private const int MaxLamellas = 20_000;
    public static bool ValidSettings(int length, int minWidth, int maxWidth, int widthLoss, int panelWidth) => length > 0 && minWidth > 0 && maxWidth >= minWidth && widthLoss >= 0 && widthLoss < minWidth && panelWidth > 0;

    // A free rectangle retains its source coordinates. Cut across the grain first,
    // then rip the resulting section; never reuse material allocated to a lamella.
    private record FreePiece(StockBoard Board, int Width, int Length, int X = 0, int Y = 0, bool CanShorten = true);

    // Stack short parts into columns, then join columns side by side.
    private static List<PlannedPanel>? BuildPanels(ProjectInput input)
    {
        if (input.WoodParts.Sum(p => Math.Ceiling(p.Quantity)) > MaxLamellas) return null;
        var panels = new List<PlannedPanel>();
        foreach (var group in input.WoodParts.Select((part, index) => (part, index))
            .GroupBy(e => (Wood: e.part.WoodType.Trim().ToUpperInvariant(), Thickness: e.part.ThicknessMm < 29 ? 32m : 50m)))
        {
            var columns = new List<PlannedPanel>();
            foreach (var (part, index) in group.OrderByDescending(e => Math.Max(e.part.LengthMm, e.part.WidthMm)))
            {
                var length = Math.Ceiling(Math.Max(part.LengthMm, part.WidthMm) + input.LamellaLengthExtraMm);
                var width = Math.Ceiling(Math.Min(part.LengthMm, part.WidthMm));
                for (var copy = 0; copy < Math.Ceiling(part.Quantity); copy++)
                {
                    var column = length < input.LamellaMinLengthMm
                        ? columns.FindIndex(c => c.LengthMm < input.LamellaMinLengthMm
                            && (c.WidthMm == width || Math.Max(c.WidthMm, width) <= input.GlueBoardMaxWidthMm))
                        : -1;
                    if (column < 0)
                        columns.Add(new(0, part.WoodType.Trim(), group.Key.Thickness, length, width, [new(index, part.Name, length, width, 0, 0)]));
                    else
                    {
                        var c = columns[column];
                        c.Parts.Add(new(index, part.Name, length, width, c.LengthMm + Kerf, 0));
                        columns[column] = c with { LengthMm = c.LengthMm + Kerf + length, WidthMm = Math.Max(c.WidthMm, width) };
                    }
                }
            }
            var joined = new List<PlannedPanel>();
            foreach (var column in columns.Select(c => c with { LengthMm = Math.Max(c.LengthMm, input.LamellaMinLengthMm) }).OrderByDescending(c => c.LengthMm))
            {
                var target = joined.FindIndex(p => p.LengthMm - column.LengthMm <= input.LamellaMergeToleranceMm
                    && p.WidthMm + Kerf + column.WidthMm <= input.GlueBoardMaxWidthMm);
                if (target < 0) joined.Add(column);
                else
                {
                    var panel = joined[target];
                    panel.Parts.AddRange(column.Parts.Select(p => p with { OffsetWidthMm = panel.WidthMm + Kerf }));
                    joined[target] = panel with { WidthMm = panel.WidthMm + Kerf + column.WidthMm };
                }
            }
            foreach (var panel in joined) panels.Add(panel with { Id = panels.Count + 1 });
        }
        return panels;
    }

    public static CuttingPlan Create(ProjectInput input, IReadOnlyList<StockBoard> stock)
    {
        var panels = BuildPanels(input);
        if (panels is null)
            return new(false, Kerf, [], [], ["Plán podporuje nejvýše 20 000 dílců. Rozdělte zakázku na menší části."], [], []);
        var requirements = panels.OrderByDescending(p => p.LengthMm).ThenBy(p => p.WoodType).ThenBy(p => p.BoardThicknessMm);
        var pieces = stock.Where(b => !b.IsCut && b.ThicknessMm > 0)
            .Select(b => new FreePiece(b, b.WidthMm, b.LengthMm, CanShorten: b.Stage != "width")).ToList();
        var lamellas = new List<PlannedLamella>();
        var discarded = new List<FreePiece>();
        var shortages = new List<CuttingShortage>();
        var warnings = new List<string>();
        if (panels.Any(p => p.WidthMm > input.GlueBoardMaxWidthMm)) warnings.Add("Samostatný dílec je širší než nastavená šířka spárovky; plán jej ponechává samostatně v požadované šířce.");
        if (input.WoodParts.Any(p => p.Quantity != Math.Ceiling(p.Quantity))) warnings.Add("Necelé počty dílců jsou pro nářez zaokrouhleny nahoru na celé kusy.");
        var unknown = stock.Count(b => !b.IsCut && b.ThicknessMm is null);
        if (unknown > 0) warnings.Add($"U {unknown} dostupných kusů není zadána tloušťka. Doplňte ji ve skladu; tyto kusy nebyly použity.");

        foreach (var requirement in requirements)
        {
            var remaining = requirement.WidthMm;
            while (remaining > 0 && lamellas.Count < MaxLamellas)
            {
                var piece = pieces.Where(p => string.Equals(p.Board.WoodType.Trim(), requirement.WoodType, StringComparison.OrdinalIgnoreCase)
                        && p.Board.ThicknessMm == requirement.BoardThicknessMm && p.Length >= requirement.LengthMm
                        && p.Width >= input.LamellaMinWidthMm)
                    .OrderBy(p => p.Length - requirement.LengthMm).ThenBy(p => p.Width).ThenBy(p => p.Board.Id).FirstOrDefault();
                if (piece is null) break;
                pieces.Remove(piece);
                // Width-cut stock cannot be shortened under the stock's cut-order rules.
                var length = piece.CanShorten ? (int)requirement.LengthMm : piece.Length;
                var slots = (int)Math.Ceiling(((decimal)piece.Width + Kerf) / ((decimal)input.LamellaMaxWidthMm + Kerf));
                var balancedWidth = (int)(((long)piece.Width - (long)(slots - 1) * Kerf) / slots);
                var width = Math.Min(input.LamellaMaxWidthMm, balancedWidth >= input.LamellaMinWidthMm ? balancedWidth : piece.Width);
                width = (int)Math.Min(width, Math.Max(input.LamellaMinWidthMm, Math.Ceiling(remaining) + input.LamellaWidthLossMm));
                lamellas.Add(new(piece.Board.Id, piece.Board.WoodType, piece.Board.ThicknessMm!.Value, length, width, piece.X, piece.Y, width - input.LamellaWidthLossMm, requirement.Id));
                remaining -= width - input.LamellaWidthLossMm;
                var restLength = piece.Length - length - Kerf;
                if (restLength > 0)
                    pieces.Add(new(piece.Board, piece.Width, restLength, piece.X, piece.Y + length + Kerf));
                var restWidth = piece.Width - width - Kerf;
                if (restWidth >= input.LamellaMinWidthMm)
                    pieces.Add(new(piece.Board, restWidth, length, piece.X + width + Kerf, piece.Y, CanShorten: false));
                else if (restWidth > 0)
                    discarded.Add(new(piece.Board, restWidth, length, piece.X + width + Kerf, piece.Y, CanShorten: false));
            }
            if (remaining > 0) shortages.Add(new(requirement.WoodType, requirement.BoardThicknessMm, requirement.LengthMm, remaining));
        }
        if (lamellas.Count == MaxLamellas) warnings.Add("Plán dosáhl limitu 20 000 lamel. Rozdělte zakázku na menší části.");
        var usedBoards = lamellas.Select(l => l.BoardId).ToHashSet();
        var offcuts = pieces.Concat(discarded).Where(p => usedBoards.Contains(p.Board.Id))
            .OrderBy(p => p.Board.Id).ThenBy(p => p.Y).ThenBy(p => p.X)
            .Select(p => new PlannedOffcut(p.Board.Id, p.Board.WoodType, p.Board.ThicknessMm!.Value, p.Length, p.Width, p.X, p.Y)).ToList();
        return new(shortages.Count == 0, Kerf, lamellas, shortages, warnings, panels, offcuts);
    }
}
