namespace Kalkulace.Api;

public static class TransportCutting
{
    private const int LamellaKerf = 3;
    private const int TransportKerf = 20;

    // Each suggestion concerns one purchasing length of lamella, not a mixed cutting plan.
    public static List<TransportCut> Recommend(decimal lamellaLengthMm, int maxLengthMm) =>
        new[] { 3000, 4000, 5000 }.Select(length => Recommend(length, lamellaLengthMm, maxLengthMm)).ToList();

    private static TransportCut Recommend(int boardLength, decimal lamellaLength, int maxLength)
    {
        if (lamellaLength > maxLength || lamellaLength > boardLength)
            return new(boardLength, [], 0, lamellaLength > maxLength
                ? "Lamela je delší než limit vozíku. Zvolte jiný způsob přepravy."
                : "Fošna je kratší než potřebná lamela.");

        var unit = (int)Math.Ceiling(lamellaLength);
        var limit = Math.Min(maxLength, boardLength);
        var capacity = (limit + LamellaKerf) / (unit + LamellaKerf);
        var totalUnits = (boardLength + LamellaKerf) / (unit + LamellaKerf);
        var minimumPieces = (boardLength + TransportKerf + limit + TransportKerf - 1) / (limit + TransportKerf);
        var maximumPieces = Math.Max(minimumPieces, (totalUnits + capacity - 1) / capacity);
        List<int> best = [];
        var bestCount = -1;
        for (var count = minimumPieces; count <= maximumPieces; count++)
        {
            var available = boardLength - TransportKerf * (count - 1);
            var units = Math.Min(totalUnits, count * capacity);
            List<int> pieces;
            while (true)
            {
                var occupied = Math.Min(units, count);
                var remainingUnits = units - occupied;
                pieces = [];
                for (var i = 0; i < count; i++)
                {
                    var extraUnits = i < occupied ? Math.Min(capacity - 1, remainingUnits) : 0;
                    var assigned = i < occupied ? 1 + extraUnits : 0;
                    remainingUnits -= extraUnits;
                    pieces.Add(assigned == 0 ? 1 : assigned * unit + (assigned - 1) * LamellaKerf);
                }
                if (pieces.Sum() <= available || units == 0) break;
                units--;
            }
            if (pieces.Sum() > available || units <= bestCount) continue;
            var slack = available - pieces.Sum();
            for (var i = 0; i < pieces.Count; i++)
            {
                var extra = Math.Min(slack, limit - pieces[i]);
                pieces[i] += extra;
                slack -= extra;
            }
            if (slack != 0) continue;
            best = pieces;
            bestCount = units;
        }
        return new(boardLength, best, bestCount, null);
    }
}
