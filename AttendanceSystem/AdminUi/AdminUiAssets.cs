using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.AdminUi;

/// <summary>
/// Embedded, dependency-free Hebrew RTL shell. Contains only static markup,
/// CSS and synthetic demo data. Real API requests still require a bearer JWT.
/// </summary>
public static class AdminUiAssets
{
    public static void MapRoutes(WebApplication app)
    {
        app.MapGet("/app", () => Asset("index.html", "text/html; charset=utf-8"))
            .AllowAnonymous();
        app.MapGet("/app/assets/style.css",
                () => Asset("style.css", "text/css; charset=utf-8"))
            .AllowAnonymous();
        app.MapGet("/app/assets/app.js",
                () => Asset("app.js", "application/javascript; charset=utf-8"))
            .AllowAnonymous();
    }

    private static IResult Asset(string file, string type)
    {
        // Fixed allowlist; no path from users reaches the resource loader.
        using var resource = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("AttendanceSystem.AdminUi." + file);
        if (resource is null)
            return Results.Problem(statusCode: 500, title: "Embedded UI asset missing.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return Results.Bytes(buffer.ToArray(), type);
    }

    public static void ApplySecurityHeaders(HttpContext context)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; script-src 'self'; style-src 'self'; " +
            "connect-src 'self'; img-src 'self'; form-action 'none'; " +
            "frame-ancestors 'none'; base-uri 'none'";
    }

    public static WebApplication CreatePreviewApp(int port = 21814)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
            options.Limits.MaxRequestBodySize = 1024;
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            ApplySecurityHeaders(context);
            await next();
        });
        app.MapGet("/", () => Results.Redirect("/app"));
        MapRoutes(app);
        return app;
    }

    public static async Task RunPreviewAsync()
    {
        // Does not need any user/token/config/db/real scanner.
        await using var app = CreatePreviewApp();
        Console.WriteLine("Synthetic-only RTL preview: http://127.0.0.1:21814/app");
        await app.RunAsync();
    }
}
