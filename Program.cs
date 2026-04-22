using Idealista.Configuration;
using Idealista.Helpers;
using Idealista.Models;
using Idealista.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
builder.Services.Configure<ScraperSettings>(builder.Configuration.GetSection("ScraperSettings"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.Configure<DashboardSettings>(builder.Configuration.GetSection("DashboardSettings"));

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
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();

// ── Login ─────────────────────────────────────────────────────────────────────
app.MapGet("/login", () => Results.Content(LoginHtml(), "text/html")).AllowAnonymous();

app.MapPost("/login", async (HttpContext ctx, IOptions<DashboardSettings> dashOpts) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var user = form["username"].ToString();
    var pass = form["password"].ToString();
    var settings = dashOpts.Value;

    if (user == settings.Username && pass == settings.Password)
    {
        var claims = new[] { new Claim(ClaimTypes.Name, user) };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Redirect("/");
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
    return Results.Content(File.ReadAllText(
        Path.Combine(builder.Environment.WebRootPath, "index.html")), "text/html");
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
}).RequireAuthorization();

app.MapPost("/api/stop", async (ScraperBackgroundService scraper) =>
{
    await scraper.StopAsync();
    return Results.Ok(new { message = "Scraping detenido" });
}).RequireAuthorization();

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
}).RequireAuthorization();

// ── API: Properties ───────────────────────────────────────────────────────────
app.MapGet("/api/properties", (int page = 1, int pageSize = 50, string? search = null) =>
{
    var all = DatabaseHelper.GetAllProperties();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var s = search.ToLowerInvariant();
        all = all.Where(p =>
            p.Title.ToLowerInvariant().Contains(s) ||
            p.Location.ToLowerInvariant().Contains(s) ||
            p.Province.ToLowerInvariant().Contains(s)
        ).ToList();
    }

    var total = all.Count;
    var items = all.Skip((page - 1) * pageSize).Take(pageSize).Select(p => new
    {
        p.Id, p.Title, p.Location, p.Province, p.Price, p.Currency,
        p.Url, p.IsProfessional, p.IsBank, p.Description, p.ListingType
    });

    return Results.Ok(new { total, page, pageSize, items });
}).RequireAuthorization();

// ── API: Locations ────────────────────────────────────────────────────────────
string LocationsPath() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "locations.json");

app.MapGet("/api/locations", () =>
{
    var json = File.ReadAllText(LocationsPath());
    var data = JsonConvert.DeserializeObject<LocationData>(json);
    return Results.Ok(data?.Locations ?? new List<Location>());
}).RequireAuthorization();

app.MapPost("/api/locations", async (HttpContext ctx) =>
{
    var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
    var loc = JsonConvert.DeserializeObject<Location>(body);
    if (loc == null || string.IsNullOrWhiteSpace(loc.Name) || string.IsNullOrWhiteSpace(loc.Url))
        return Results.BadRequest(new { error = "Name y Url son obligatorios" });

    var path = LocationsPath();
    var json = File.ReadAllText(path);
    var data = JsonConvert.DeserializeObject<LocationData>(json) ?? new LocationData();
    data.Locations.Add(loc);
    File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
    return Results.Ok(data.Locations);
}).RequireAuthorization();

app.MapDelete("/api/locations/{index:int}", (int index) =>
{
    var path = LocationsPath();
    var json = File.ReadAllText(path);
    var data = JsonConvert.DeserializeObject<LocationData>(json) ?? new LocationData();
    if (index < 0 || index >= data.Locations.Count)
        return Results.BadRequest(new { error = "Índice fuera de rango" });

    data.Locations.RemoveAt(index);
    File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
    return Results.Ok(data.Locations);
}).RequireAuthorization();

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
