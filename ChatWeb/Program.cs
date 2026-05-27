using ChatWeb;
using ChatWeb.Data;
using ChatWeb.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    });
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddSingleton<Store>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/login", async (HttpContext ctx, Store store, LoginReq req) =>
{
    if (store.TryLogin(req.User, req.Pass))
    {
        var claims = new[] { new Claim(ClaimTypes.Name, req.User) };
        await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "cookies")));
        return Results.Ok(new { ok = true });
    }
    return Results.Json(new { ok = false, error = "Неверный логин или пароль" });
});

app.MapPost("/api/register", async (HttpContext ctx, Store store, LoginReq req) =>
{
    if (store.TryRegister(req.User, req.Pass))
    {
        var claims = new[] { new Claim(ClaimTypes.Name, req.User) };
        await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "cookies")));
        return Results.Ok(new { ok = true });
    }
    return Results.Json(new { ok = false, error = "Логин занят" });
});

app.MapPost("/api/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync();
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/me", (HttpContext ctx) =>
{
    var user = ctx.User.Identity?.Name;
    return Results.Json(new { user });
});

app.MapPost("/api/upload", async (HttpContext ctx, Store store) =>
{
    var user = ctx.User.Identity?.Name;
    if (user == null) return Results.Unauthorized();
    var file = ctx.Request.Form.Files.FirstOrDefault();
    if (file == null || file.Length == 0)
        return Results.Json(new { ok = false, error = "Файл не выбран" });

    var dir = Path.Combine("Data", "uploads", user);
    Directory.CreateDirectory(dir);
    var name = string.Concat(file.FileName.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Take(64).ToArray();
    var safeName = new string(name);
    if (string.IsNullOrEmpty(safeName)) return Results.Json(new { ok = false, error = "Имя файла недопустимо" });

    var path = Path.Combine(dir, safeName);
    using var fs = new FileStream(path, FileMode.Create);
    await file.CopyToAsync(fs);
    return Results.Json(new { ok = true, name = safeName });
});

app.MapGet("/api/files", (HttpContext ctx) =>
{
    var user = ctx.User.Identity?.Name;
    if (user == null) return Results.Unauthorized();
    var dir = Path.Combine("Data", "uploads", user);
    if (!Directory.Exists(dir)) return Results.Json(Array.Empty<string>());
    var files = Directory.GetFiles(dir).Select(Path.GetFileName).ToList();
    return Results.Json(files);
});

app.MapGet("/api/download/{user}/{name}", async (HttpContext ctx, Store store, string user, string name) =>
{
    var currentUser = ctx.User.Identity?.Name;
    if (currentUser == null) return Results.Unauthorized();
    name = new string(string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Take(64).ToArray());
    var path = Path.Combine("Data", "uploads", user, name);
    if (!File.Exists(path)) return Results.NotFound();
    var bytes = await File.ReadAllBytesAsync(path);
    return Results.File(bytes, "application/octet-stream", name);
});

app.MapGet("/api/rooms", (Store store) =>
{
    var rooms = store.GetRoomList();
    return Results.Json(rooms);
});

app.MapGet("/api/messages/{room}", (Store store, string room) =>
{
    var msgs = store.GetRecentMessages(room);
    return Results.Json(msgs);
});

app.MapHub<ChatHub>("/chat");

app.Run();

record LoginReq(string User, string Pass);