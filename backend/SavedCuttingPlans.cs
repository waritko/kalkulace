using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Kalkulace.Api;

public class SavedCuttingPlan
{
    public string Id { get; set; } = "";
    public int ProjectId { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public string InputJson { get; set; } = "";
    public string PlanJson { get; set; } = "";
}

public record SaveCuttingPlanRequest(string Id, int ProjectId, ProjectInput Input, CuttingPlan Plan);
public record SavedCuttingPlanResponse(string Id, int ProjectId, DateTimeOffset SavedAt, ProjectInput Input, CuttingPlan Plan);

public static class SavedCuttingPlans
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static SavedCuttingPlanResponse Response(SavedCuttingPlan saved) => new(saved.Id, saved.ProjectId, saved.SavedAt,
        JsonSerializer.Deserialize<ProjectInput>(saved.InputJson, Json)!, JsonSerializer.Deserialize<CuttingPlan>(saved.PlanJson, Json)!);

    public static void MapSavedCuttingPlans(this WebApplication app)
    {
        app.MapGet("/api/projects/{id:int}/cutting-plans", async (int id, AppDb db) =>
        {
            var plans = await db.SavedCuttingPlans.AsNoTracking().Where(p => p.ProjectId == id).ToListAsync();
            return Results.Ok(plans.OrderByDescending(p => p.SavedAt).Select(Response));
        });
        app.MapPost("/api/cutting-plans", async (SaveCuttingPlanRequest request, AppDb db) =>
        {
            if (!Guid.TryParse(request.Id, out var guid) || request.Input is null || request.Plan is null)
                return Results.BadRequest(new { error = "Neplatný požadavek na uložení plánu." });
            var id = guid.ToString();
            var errors = Validator.Validate(request.Input);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var inputJson = JsonSerializer.Serialize(request.Input, Json);
            var planJson = JsonSerializer.Serialize(request.Plan, Json);
            var existing = await db.SavedCuttingPlans.FindAsync(id);
            // Retries after a lost response must never consume stock again.
            if (existing is not null)
                return existing.ProjectId == request.ProjectId && existing.InputJson == inputJson && existing.PlanJson == planJson
                    ? Results.Ok(Response(existing))
                    : Results.Conflict(new { error = "Tento identifikátor již patří jinému plánu." });
            if (!await db.Projects.AnyAsync(p => p.Id == request.ProjectId))
                return Results.BadRequest(new { error = "Nejprve uložte zakázku." });
            if (request.Plan.Lamellas is null || request.Plan.Offcuts is null || !request.Plan.Complete || request.Plan.Lamellas.Count == 0)
                return Results.BadRequest(new { error = "Uložit lze pouze kompletní plán s použitým materiálem." });
            var boardIds = request.Plan.Lamellas.Select(l => l.BoardId).Distinct().ToArray();
            var boards = await db.StockBoards.Where(b => boardIds.Contains(b.Id)).OrderBy(b => b.Id).ToListAsync();
            if (boards.Count != boardIds.Length || boards.Any(b => b.IsCut))
                return Results.Conflict(new { error = "Použité fošny již nejsou dostupné. Vytvořte nový plán." });
            // Recompute geometry; never trust client-provided stock pieces.
            var verified = CuttingPlanner.Create(request.Input, boards);
            bool Same<T>(T left, T right) => JsonSerializer.Serialize(left, Json) == JsonSerializer.Serialize(right, Json);
            if (!verified.Complete || request.Plan.KerfMm != verified.KerfMm || !Same(verified.Lamellas, request.Plan.Lamellas)
                || !Same(verified.Panels, request.Plan.Panels) || !Same(verified.Offcuts, request.Plan.Offcuts)
                || !Same(verified.Shortages, request.Plan.Shortages))
                return Results.Conflict(new { error = "Plán neodpovídá zadání nebo aktuálnímu skladu. Vytvořte nový plán." });
            foreach (var board in boards)
            {
                board.IsCut = true;
                board.CutAxis = "plan";
                board.KerfMm = verified.KerfMm;
            }
            foreach (var piece in verified.Offcuts.Where(p => p.KeepInStock && !p.IsKerf))
            {
                var parent = boards.Single(b => b.Id == piece.BoardId);
                db.StockBoards.Add(new StockBoard
                {
                    ParentId = parent.Id, WoodType = parent.WoodType, ThicknessMm = parent.ThicknessMm,
                    WidthMm = piece.WidthMm, LengthMm = piece.LengthMm,
                    Stage = parent.Stage == "width" || piece.OffsetWidthMm > 0 ? "width" : "length"
                });
            }
            var saved = new SavedCuttingPlan { Id = id, ProjectId = request.ProjectId, SavedAt = DateTimeOffset.UtcNow, InputJson = inputJson, PlanJson = planJson };
            db.SavedCuttingPlans.Add(saved);
            // SaveChanges atomically stores the snapshot, claims and offcuts.
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = "Uložení se nezdařilo kvůli souběžné změně. Obnovte uložené plány a sklad; případně opakujte uložení." });
            }
            return Results.Ok(Response(saved));
        });
    }
}
