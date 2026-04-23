using Idealista.Configuration;
using Idealista.Helpers;
using Idealista.Models;
using Idealista.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
builder.Services.Configure<ScraperSettings>(builder.Configuration.GetSection("ScraperSettings"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<ScraperState>();
builder.Services.AddSingleton<LogService>();
builder.Services.AddSingleton<ScraperBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScraperBackgroundService>());

// ── Auth ─────────────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.Cookie.Name = "nexumlabs_session";
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole("admin"));
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();

// ── Login ─────────────────────────────────────────────────────────────────────
app.MapGet("/login", () => Results.Content(LoginHtml(), "text/html")).AllowAnonymous();

app.MapPost("/login", async (HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var user = form["username"].ToString();
    var pass = form["password"].ToString();

    var appUser = DatabaseHelper.ValidateUser(user, pass);
    if (appUser != null)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, appUser.Username),
            new Claim(ClaimTypes.Role, appUser.Role),
            new Claim("userId", appUser.Id.ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Redirect(appUser.Role == "admin" ? "/" : "/user");
    }

    return Results.Content(LoginHtml("Usuario o contraseña incorrectos"), "text/html");
}).AllowAnonymous();

app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireAuthorization();

// ── Dashboard ─────────────────────────────────────────────────────────────────
app.MapGet("/", (HttpContext ctx) =>
{
    if (ctx.User.IsInRole("user"))
        return Results.Redirect("/user");
    return Results.Content(File.ReadAllText(
        Path.Combine(builder.Environment.WebRootPath, "index.html")), "text/html");
}).RequireAuthorization();

app.MapGet("/user", (HttpContext ctx) =>
{
    if (ctx.User.IsInRole("admin"))
        return Results.Redirect("/");
    return Results.Content(File.ReadAllText(
        Path.Combine(builder.Environment.WebRootPath, "user.html")), "text/html");
}).RequireAuthorization();

// ── API: Me ───────────────────────────────────────────────────────────────────
app.MapGet("/api/me", (HttpContext ctx) =>
{
    var username = ctx.User.Identity?.Name ?? "";
    var user = DatabaseHelper.GetAllUsers().FirstOrDefault(u =>
        u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    return Results.Ok(new
    {
        username,
        role             = ctx.User.IsInRole("admin") ? "admin" : "user",
        alertEmails      = user?.AlertEmails      ?? new List<string>(),
        allowedProvinces = user?.AllowedProvinces ?? new List<string>()
    });
}).RequireAuthorization();

// ── API: Users (admin only) ───────────────────────────────────────────────────
app.MapGet("/api/users", () =>
{
    var users = DatabaseHelper.GetAllUsers()
        .Select(u => new { u.Id, u.Username, u.Role, u.AlertEmails, u.AllowedProvinces, u.CreatedAt });
    return Results.Ok(users);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/users", async (HttpContext ctx) =>
{
    var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
    JObject data;
    try { data = JObject.Parse(body); } catch { return Results.BadRequest(new { error = "JSON inválido" }); }

    var username = data["username"]?.ToString();
    var password = data["password"]?.ToString();
    var role     = data["role"]?.ToString() ?? "user";

    var (ok, error) = DatabaseHelper.CreateUser(username ?? "", password ?? "", role);
    if (!ok) return Results.BadRequest(new { error });

    var users = DatabaseHelper.GetAllUsers().Select(u => new { u.Id, u.Username, u.Role, u.AlertEmails, u.AllowedProvinces, u.CreatedAt });
    return Results.Ok(users);
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/users/{id:int}", async (HttpContext ctx, int id) =>
{
    var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
    JObject data;
    try { data = JObject.Parse(body); } catch { return Results.BadRequest(new { error = "JSON inválido" }); }

    var newUsername        = data["username"]?.ToString();
    var newPassword        = data["password"]?.ToString();
    var newRole            = data["role"]?.ToString();
    var newAlertEmails     = data["alertEmails"]?.ToObject<List<string>>();
    var newAllowedProvinces = data["allowedProvinces"]?.ToObject<List<string>>();
    var currentUser        = ctx.User.Identity?.Name ?? "";

    var (ok, error) = DatabaseHelper.UpdateUser(id, newUsername, newPassword, newRole, newAlertEmails, newAllowedProvinces, currentUser);
    if (!ok) return Results.BadRequest(new { error });

    var users = DatabaseHelper.GetAllUsers().Select(u => new { u.Id, u.Username, u.Role, u.AlertEmails, u.AllowedProvinces, u.CreatedAt });
    return Results.Ok(users);
}).RequireAuthorization("AdminOnly");

app.MapDelete("/api/users/{id:int}", (HttpContext ctx, int id) =>
{
    var currentUser = ctx.User.Identity?.Name ?? "";
    var (ok, error) = DatabaseHelper.DeleteUser(id, currentUser);
    if (!ok) return Results.BadRequest(new { error });

    var users = DatabaseHelper.GetAllUsers().Select(u => new { u.Id, u.Username, u.Role, u.AlertEmails, u.AllowedProvinces, u.CreatedAt });
    return Results.Ok(users);
}).RequireAuthorization("AdminOnly");

// ── API: My alert emails (any authenticated user) ─────────────────────────────
app.MapGet("/api/me/emails", (HttpContext ctx) =>
{
    var idClaim = ctx.User.FindFirst("userId")?.Value;
    if (!int.TryParse(idClaim, out int userId)) return Results.Unauthorized();
    var user = DatabaseHelper.GetUserById(userId);
    return user == null ? Results.NotFound() : Results.Ok(user.AlertEmails);
}).RequireAuthorization();

app.MapPut("/api/me/emails", async (HttpContext ctx) =>
{
    var idClaim = ctx.User.FindFirst("userId")?.Value;
    if (!int.TryParse(idClaim, out int userId)) return Results.Unauthorized();

    var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
    List<string> emails;
    try   { emails = System.Text.Json.JsonSerializer.Deserialize<List<string>>(body) ?? new(); }
    catch { return Results.BadRequest(new { error = "JSON inválido" }); }

    var (ok, error) = DatabaseHelper.UpdateUserEmails(userId, emails);
    if (!ok) return Results.BadRequest(new { error });
    return Results.Ok(DatabaseHelper.GetUserById(userId)?.AlertEmails ?? new List<string>());
}).RequireAuthorization();

// ── API: Status ───────────────────────────────────────────────────────────────
app.MapGet("/api/status", (ScraperState state) =>
{
    string? countdown = null;
    if (state.NextCycleTime.HasValue)
    {
        var remaining = state.NextCycleTime.Value - DateTime.Now;
        countdown = remaining.TotalSeconds > 0
            ? $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}"
            : "00:00";
    }

    return Results.Ok(new
    {
        isRunning      = state.IsRunning,
        cycleCount     = state.CycleCount,
        newLeads       = state.NewLeadsSession,
        emailsSent     = state.EmailsSentSession,
        propertyCount  = state.PropertyCount,
        lastRunTime    = state.LastRunTime?.ToString("dd/MM/yyyy HH:mm:ss"),
        currentLocation = state.CurrentLocation,
        countdown,
        lastProperty   = state.LastProperty == null ? null : new
        {
            title       = state.LastProperty.Title,
            location    = state.LastProperty.Location,
            province    = state.LastProperty.Province,
            price       = state.LastProperty.Price,
            currency    = state.LastProperty.Currency,
            url         = state.LastProperty.Url,
            id          = state.LastProperty.Id,
            isProfessional = state.LastProperty.IsProfessional,
            isBank      = state.LastProperty.IsBank,
            description = state.LastProperty.Description,
            listingType = state.LastProperty.ListingType
        }
    });
}).RequireAuthorization();

// ── API: Start / Stop ─────────────────────────────────────────────────────────
app.MapPost("/api/start", (ScraperState state, ScraperBackgroundService scraper) =>
{
    if (state.IsRunning) return Results.BadRequest(new { error = "Ya está en ejecución" });
    scraper.Start();
    return Results.Ok(new { message = "Scraping iniciado" });
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/stop", async (ScraperBackgroundService scraper) =>
{
    await scraper.StopAsync();
    return Results.Ok(new { message = "Scraping detenido" });
}).RequireAuthorization("AdminOnly");

// ── API: Logs SSE ─────────────────────────────────────────────────────────────
app.MapGet("/api/logs/stream", async (HttpContext ctx, LogService logService) =>
{
    ctx.Response.Headers["Content-Type"]  = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["Connection"]    = "keep-alive";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";

    var ct = ctx.RequestAborted;

    await foreach (var entry in logService.SubscribeAsync(ct))
    {
        var data = $"data: [{entry.Timestamp}] {entry.Message.Replace("\n", " ").Trim()}\n\n";
        await ctx.Response.WriteAsync(data, ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
}).RequireAuthorization("AdminOnly");

// ── API: Properties ───────────────────────────────────────────────────────────
app.MapGet("/api/properties", (HttpContext ctx,
    int page = 1, int pageSize = 50,
    string? search = null,
    string? listingType = null,
    string? vendorType = null,
    int? minPrice = null,
    int? maxPrice = null,
    string? province = null) =>
{
    var all = DatabaseHelper.GetAllProperties();

    // Non-admin users: restrict to their assigned provinces
    List<string> userProvinces = new();
    if (!ctx.User.IsInRole("admin"))
    {
        var idClaim = ctx.User.FindFirst("userId")?.Value;
        if (int.TryParse(idClaim, out int userId))
            userProvinces = DatabaseHelper.GetUserById(userId)?.AllowedProvinces ?? new();
        if (userProvinces.Count > 0)
            all = all.Where(p => userProvinces.Any(ap =>
                p.Province.Equals(ap, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        var s = search.ToLowerInvariant();
        all = all.Where(p =>
            p.Title.ToLowerInvariant().Contains(s) ||
            p.Location.ToLowerInvariant().Contains(s) ||
            p.Province.ToLowerInvariant().Contains(s) ||
            p.Description.ToLowerInvariant().Contains(s)
        ).ToList();
    }

    if (!string.IsNullOrWhiteSpace(listingType))
        all = all.Where(p => p.ListingType.Equals(listingType, StringComparison.OrdinalIgnoreCase)).ToList();

    if (!string.IsNullOrWhiteSpace(vendorType))
        all = vendorType.ToLower() switch
        {
            "banco"       => all.Where(p => p.IsBank).ToList(),
            "profesional" => all.Where(p => p.IsProfessional && !p.IsBank).ToList(),
            "particular"  => all.Where(p => !p.IsProfessional && !p.IsBank).ToList(),
            _             => all
        };

    if (minPrice.HasValue)
        all = all.Where(p => p.Price >= minPrice.Value).ToList();
    if (maxPrice.HasValue)
        all = all.Where(p => p.Price <= maxPrice.Value).ToList();

    if (!string.IsNullOrWhiteSpace(province))
        all = all.Where(p => p.Province.Equals(province, StringComparison.OrdinalIgnoreCase)).ToList();

    // Provinces list: for users, only their assigned ones; for admin, all
    var provinces = (userProvinces.Count > 0 ? userProvinces :
        DatabaseHelper.GetAllProperties()
            .Select(p => p.Province).Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct().OrderBy(p => p).ToList())
        .OrderBy(p => p).ToList();

    var total = all.Count;
    var items = all.Skip((page - 1) * pageSize).Take(pageSize).Select(p => new
    {
        p.Id, p.Title, p.Location, p.Province, p.Price, p.Currency,
        p.Url, p.IsProfessional, p.IsBank, p.Description, p.ListingType
    });

    return Results.Ok(new { total, page, pageSize, items, provinces });
}).RequireAuthorization();

// ── API: Locations ────────────────────────────────────────────────────────────
app.MapGet("/api/locations", () =>
{
    return Results.Ok(DatabaseHelper.GetAllLocations());
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/locations", async (HttpContext ctx) =>
{
    var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
    JObject data;
    try { data = JObject.Parse(body); } catch { return Results.BadRequest(new { error = "JSON inválido" }); }

    var name    = data["name"]?.ToString();
    var url     = data["url"]?.ToString();
    var rentUrl = data["rentUrl"]?.ToString();

    var (ok, error, _) = DatabaseHelper.AddLocation(name ?? "", url ?? "", rentUrl);
    if (!ok) return Results.BadRequest(new { error });
    return Results.Ok(DatabaseHelper.GetAllLocations());
}).RequireAuthorization("AdminOnly");

app.MapDelete("/api/locations/{id:int}", (int id) =>
{
    var (ok, error) = DatabaseHelper.DeleteLocation(id);
    if (!ok) return Results.BadRequest(new { error });
    return Results.Ok(DatabaseHelper.GetAllLocations());
}).RequireAuthorization("AdminOnly");

// ── API: Settings ─────────────────────────────────────────────────────────────
string AppSettingsPath() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

app.MapGet("/api/settings", () =>
{
    var json = File.ReadAllText(AppSettingsPath());
    var doc = JObject.Parse(json);
    var result = new JObject
    {
        ["scraper"] = doc["ScraperSettings"] ?? new JObject(),
        ["email"]   = doc["EmailSettings"]   ?? new JObject()
    };
    return Results.Content(result.ToString(Formatting.None), "application/json");
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/settings", async (HttpContext ctx) =>
{
    var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
    JObject incoming;
    try { incoming = JObject.Parse(body); }
    catch { return Results.BadRequest(new { error = "JSON inválido" }); }

    var path = AppSettingsPath();
    var existing = JObject.Parse(File.ReadAllText(path));

    if (incoming["scraper"] is JObject scraperIn)
        MergeInto(existing["ScraperSettings"] as JObject ?? new JObject(), scraperIn,
                  j => existing["ScraperSettings"] = j);
    if (incoming["email"] is JObject emailIn)
        MergeInto(existing["EmailSettings"] as JObject ?? new JObject(), emailIn,
                  j => existing["EmailSettings"] = j);

    File.WriteAllText(path, existing.ToString(Formatting.Indented));
    return Results.Ok(new { message = "Configuración guardada correctamente" });

    static void MergeInto(JObject target, JObject source, Action<JObject> assign)
    {
        foreach (var prop in source.Properties())
        {
            // Match existing PascalCase key case-insensitively so we don't create duplicates
            var existingKey = target.Properties()
                .FirstOrDefault(p => string.Equals(p.Name, prop.Name, StringComparison.OrdinalIgnoreCase))
                ?.Name ?? prop.Name;
            target[existingKey] = prop.Value;
        }
        assign(target);
    }
}).RequireAuthorization("AdminOnly");

app.Run();

// ── Login HTML ────────────────────────────────────────────────────────────────
static string LoginHtml(string? error = null) => $@"<!DOCTYPE html>
<html lang=""es"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>NexumLabs – Login</title>
<style>
  :root{{--bg:#f5f7fa;--surface:#ffffff;--border:#e1e4e8;--text:#1c2128;--muted:#6e7781;--blue:#0969da;--green:#238636;--red:#cf222e}}
  *{{box-sizing:border-box;margin:0;padding:0}}
  body{{background:var(--bg);color:var(--text);font-family:'Segoe UI',sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;transition:background .2s,color .2s}}
  .card{{background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:40px;width:360px;box-shadow:0 4px 24px rgba(0,0,0,.08)}}
  h1{{font-size:1.1rem;margin-bottom:4px;color:var(--blue);font-weight:700}}
  .logo-wrap{{text-align:center;margin-bottom:20px}}
  .logo-wrap img{{max-width:160px;height:auto}}
  .subtitle{{color:var(--muted);font-size:.85rem;margin-bottom:28px}}
  label{{display:block;font-size:.85rem;color:var(--muted);margin-bottom:6px}}
  input{{width:100%;padding:10px 14px;background:var(--bg);border:1px solid var(--border);border-radius:8px;color:var(--text);font-size:.95rem;margin-bottom:16px;outline:none}}
  input:focus{{border-color:var(--blue)}}
  button{{width:100%;padding:11px;background:var(--green);border:none;border-radius:8px;color:#fff;font-size:1rem;cursor:pointer;font-weight:600}}
  button:hover{{opacity:.88}}
  .error{{background:#fff0f0;border:1px solid var(--red);border-radius:8px;padding:10px 14px;color:var(--red);font-size:.85rem;margin-bottom:16px}}
</style>
</head>
<body>
<div class=""card"">
  <div class=""logo-wrap""><img src=""/logo.png"" alt=""Logo"" onerror=""this.style.display='none'""></div>
  <h1>NexumLabs</h1>
  <p class=""subtitle"">Alertas automáticas de propiedades</p>
  {(error != null ? $@"<div class=""error"">⚠ {error}</div>" : "")}
  <form method=""post"" action=""/login"">
    <label>Usuario</label>
    <input type=""text"" name=""username"" autofocus autocomplete=""username"" required>
    <label>Contraseña</label>
    <input type=""password"" name=""password"" autocomplete=""current-password"" required>
    <button type=""submit"">Entrar</button>
  </form>
</div>
</body>
</html>";
