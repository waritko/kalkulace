using System.Data;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Kalkulace.Api;

public class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string NormalizedUsername { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsAdmin { get; set; }
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString();
}

public record UserInfo(int Id, string Username, bool IsAdmin)
{
    public static UserInfo From(AppUser user) => new(user.Id, user.Username, user.IsAdmin);
}
public record LoginRequest(string? Username, string? Password);
public record UserRequest(string? Username, string? Password, bool IsAdmin);

public static class AppAuthentication
{
    // Salted ASP.NET Identity PBKDF2 hash of the requested initial password.
    private const string InitialPasswordHash = "AQAAAAIAAYagAAAAEBOcdq/m5IVWWUkHILqPnuinuaBZMBlG/8BYm7QULZKePP8CfBbKzsr6wnKW2aBmrg==";

    public static void AddAppAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "kalkulace-session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
            options.Events.OnValidatePrincipal = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDb>();
                var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var user = int.TryParse(id, out var userId) ? await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId) : null;
                if (user is null || user.SecurityStamp != context.Principal?.FindFirstValue("stamp"))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync();
                }
            };
        });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.AddPolicy("admin", policy => policy.RequireRole("admin"));
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    public static async Task SeedAsync(AppDb db)
    {
        if (await db.Users.AnyAsync()) return;
        db.Users.Add(new AppUser
        {
            Username = "waritko", NormalizedUsername = "WARITKO", IsAdmin = true,
            PasswordHash = InitialPasswordHash
        });
        await db.SaveChangesAsync();
    }

    public static void MapAppAuthentication(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest request, AppDb db, IPasswordHasher<AppUser> hasher, HttpContext context) =>
        {
            if (request.Username is null || request.Username.Length > 100 || request.Password is null || request.Password.Length > 128)
                return Results.BadRequest(new { error = "Vyplňte uživatelské jméno a heslo." });
            var normalized = request.Username.Trim().ToUpperInvariant();
            var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedUsername == normalized);
            // Verify a real hash even for unknown usernames to avoid a cheap timing distinction.
            var check = user ?? new AppUser { PasswordHash = InitialPasswordHash };
            var result = hasher.VerifyHashedPassword(check, check.PasswordHash, request.Password);
            if (user is null || result == PasswordVerificationResult.Failed)
                return Results.Json(new { error = "Nesprávné uživatelské jméno nebo heslo." }, statusCode: 401);
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = hasher.HashPassword(user, request.Password);
                await db.SaveChangesAsync();
            }
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.Username), new("stamp", user.SecurityStamp) };
            if (user.IsAdmin) claims.Add(new(ClaimTypes.Role, "admin"));
            await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.Ok(UserInfo.From(user));
        }).AllowAnonymous().RequireRateLimiting("login");
        app.MapGet("/api/auth/me", async (HttpContext context, AppDb db) =>
            UserInfo.From((await db.Users.FindAsync(int.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!)))!));
        app.MapPost("/api/auth/logout", async (HttpContext context) => { await context.SignOutAsync(); return Results.NoContent(); });

        var users = app.MapGroup("/api/users").RequireAuthorization("admin");
        users.MapGet("", async (AppDb db) => await db.Users.AsNoTracking().OrderBy(u => u.Username).Select(u => new UserInfo(u.Id, u.Username, u.IsAdmin)).ToListAsync());
        users.MapPost("", (UserRequest request, AppDb db, IPasswordHasher<AppUser> hasher) => SaveUser(null, request, db, hasher));
        users.MapPut("/{id:int}", (int id, UserRequest request, AppDb db, IPasswordHasher<AppUser> hasher) => SaveUser(id, request, db, hasher));
        users.MapDelete("/{id:int}", async (int id, AppDb db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var user = await db.Users.FindAsync(id);
            if (user is null) return Results.NotFound();
            if (user.IsAdmin && await db.Users.CountAsync(u => u.IsAdmin) <= 1)
                return Results.Conflict(new { error = "Posledního administrátora nelze odstranit." });
            db.Users.Remove(user);
            try { await db.SaveChangesAsync(); await transaction.CommitAsync(); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "Účty se mezitím změnily. Načtěte je znovu." }); }
            return Results.NoContent();
        });
    }

    private static async Task<IResult> SaveUser(int? id, UserRequest request, AppDb db, IPasswordHasher<AppUser> hasher)
    {
        var username = request.Username?.Trim() ?? "";
        if (username.Length is < 1 or > 100 || username.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.')))
            return Results.BadRequest(new { error = "Jméno musí mít 1–100 znaků: písmena, číslice, tečku, pomlčku nebo podtržítko." });
        if ((id is null || !string.IsNullOrEmpty(request.Password)) && (request.Password is null || request.Password.Length is < 8 or > 128))
            return Results.BadRequest(new { error = "Heslo musí mít 8–128 znaků." });
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = id is null ? new AppUser() : await db.Users.FindAsync(id.Value);
        if (user is null) return Results.NotFound();
        if (user.IsAdmin && !request.IsAdmin && await db.Users.CountAsync(u => u.IsAdmin) <= 1)
            return Results.Conflict(new { error = "Poslednímu administrátorovi nelze odebrat oprávnění." });
        var normalized = username.ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedUsername == normalized && u.Id != user.Id))
            return Results.Conflict(new { error = "Uživatelské jméno již existuje." });
        user.Username = username;
        user.NormalizedUsername = normalized;
        user.IsAdmin = request.IsAdmin;
        user.SecurityStamp = Guid.NewGuid().ToString();
        if (!string.IsNullOrEmpty(request.Password)) user.PasswordHash = hasher.HashPassword(user, request.Password);
        if (id is null) db.Users.Add(user);
        try { await db.SaveChangesAsync(); await transaction.CommitAsync(); }
        catch (DbUpdateException) { return Results.Conflict(new { error = "Účty se mezitím změnily nebo jméno již existuje. Načtěte je znovu." }); }
        return id is null ? Results.Created($"/api/users/{user.Id}", UserInfo.From(user)) : Results.Ok(UserInfo.From(user));
    }
}
