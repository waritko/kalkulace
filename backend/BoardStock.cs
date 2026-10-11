using Microsoft.EntityFrameworkCore;

namespace Kalkulace.Api;

public class StockBoard
{
    public int Id { get; set; }
    public string WoodType { get; set; } = "";
    public int WidthMm { get; set; }
    public int LengthMm { get; set; }
    public int? ThicknessMm { get; set; }
    public int? ParentId { get; set; }
    public string Stage { get; set; } = "original";
    public bool IsCut { get; set; }
    public string? CutAxis { get; set; }
    public int? CutSizeMm { get; set; }
    public int? KerfMm { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public record StockBoardInput(string WoodType, int WidthMm, int LengthMm, int? ThicknessMm = null);
public record BoardThicknessInput(int ThicknessMm);
public record BoardCutInput(string Axis, int SizeMm, int KerfMm = 3);

public static class BoardStock
{
    public static void MapBoardStock(this WebApplication app)
    {
        app.MapGet("/api/stock/boards", async (AppDb db) =>
            await db.StockBoards.AsNoTracking().OrderByDescending(b => b.Id).ToListAsync());

        app.MapPost("/api/stock/boards", async (StockBoardInput input, AppDb db) =>
        {
            if (input.ThicknessMm is <= 0)
                return Results.BadRequest(new { error = "Tloušťka musí být kladná, v celých milimetrech." });
            if (string.IsNullOrWhiteSpace(input.WoodType) || input.WoodType.Trim().Length > 100)
                return Results.BadRequest(new { error = "Vyplňte dřevinu (nejvýše 100 znaků)." });
            if (input.WidthMm <= 0)
                return Results.BadRequest(new { error = "Šířka musí být kladná, v celých milimetrech." });
            if (input.LengthMm is not (3000 or 4000 or 5000))
                return Results.BadRequest(new { error = "Prvotní délka fošny musí být 3, 4 nebo 5 m." });
            var board = new StockBoard { WoodType = input.WoodType.Trim(), WidthMm = input.WidthMm, LengthMm = input.LengthMm, ThicknessMm = input.ThicknessMm };
            db.StockBoards.Add(board);
            await db.SaveChangesAsync();
            return Results.Created($"/api/stock/boards/{board.Id}", board);
        });

        app.MapPost("/api/stock/boards/{id:int}/thickness", async (int id, BoardThicknessInput input, AppDb db) =>
        {
            if (input.ThicknessMm <= 0) return Results.BadRequest(new { error = "Tloušťka musí být kladná." });
            var board = await db.StockBoards.FindAsync(id);
            if (board is null) return Results.NotFound();
            if (board.IsCut) return Results.Conflict(new { error = "Tloušťku doplňte u dostupného kusu." });
            board.ThicknessMm = input.ThicknessMm;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Kus mezitím rozřezal jiný uživatel. Obnovte sklad." }); }
            return Results.Ok(board);
        });

        app.MapPost("/api/stock/boards/{id:int}/cut", async (int id, BoardCutInput input, AppDb db) =>
        {
            var board = await db.StockBoards.FindAsync(id);
            if (board is null) return Results.NotFound();
            if (board.IsCut) return Results.Conflict(new { error = "Tento kus již byl rozřezán. Obnovte sklad." });
            if (input.Axis is not ("length" or "width"))
                return Results.BadRequest(new { error = "Zvolte řez na délku nebo na šířku." });
            if (input.Axis == "width" && board.Stage == "original")
                return Results.BadRequest(new { error = "Fošnu nejprve rozřežte na délku." });
            if (input.Axis == "length" && board.Stage == "width")
                return Results.BadRequest(new { error = "Po rozřezání na šířku lze pokračovat pouze na šířku." });
            var dimension = input.Axis == "length" ? board.LengthMm : board.WidthMm;
            if (input.SizeMm <= 0 || input.KerfMm < 0 || (long)input.SizeMm + input.KerfMm >= dimension)
                return Results.BadRequest(new { error = "Řez a prořez musí ponechat dva kladné kusy. Prořez nesmí být záporný." });

            StockBoard Piece(int size) => new()
            {
                WoodType = board.WoodType,
                ThicknessMm = board.ThicknessMm,
                ParentId = board.Id,
                Stage = input.Axis,
                WidthMm = input.Axis == "width" ? size : board.WidthMm,
                LengthMm = input.Axis == "length" ? size : board.LengthMm
            };
            var pieces = new[] { Piece(input.SizeMm), Piece(dimension - input.SizeMm - input.KerfMm) };
            board.IsCut = true;
            board.CutAxis = input.Axis;
            board.CutSizeMm = input.SizeMm;
            board.KerfMm = input.KerfMm;
            db.StockBoards.AddRange(pieces);
            // SaveChanges atomically claims the original and inserts both pieces.
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "Tento kus mezitím rozřezal jiný uživatel. Obnovte sklad." });
            }
            return Results.Ok(pieces);
        });
    }
}
