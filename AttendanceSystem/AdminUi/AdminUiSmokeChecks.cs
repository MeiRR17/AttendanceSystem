using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;

namespace AttendanceSystem.AdminUi;

/// <summary>
/// Offline static-asset HTTP smoke suite: no DB, OIDC, device, contact or token.
/// Verifies actual localhost-hosted Hebrew RTL assets and safe headers.
/// Browser accessibility and visual review remain separate release gates.
/// </summary>
public static class AdminUiSmokeChecks
{
    public static async Task RunAsync()
    {
        await using var app = AdminUiAssets.CreatePreviewApp(port: 0);
        await app.StartAsync();
        try
        {
            var address = app.Urls.FirstOrDefault(url =>
                url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
            if (address is null)
                throw new InvalidOperationException("UI preview did not bind on IPv4 loopback.");
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            using var response = await client.GetAsync("/app");
            Check(response.StatusCode == HttpStatusCode.OK, "HTML available");
            Check(response.Content.Headers.ContentType?.MediaType == "text/html",
                  "HTML MIME type");
            Check(response.Headers.CacheControl?.NoStore == true ||
                  response.Headers.TryGetValues("Cache-Control", out var cache) &&
                  cache.Any(item => item.Contains("no-store", StringComparison.Ordinal)),
                  "HTML no-store");
            Check(response.Headers.TryGetValues("Content-Security-Policy", out var csp) &&
                  csp.Any(x => x.Contains("script-src 'self'", StringComparison.Ordinal)),
                  "restricted scripts CSP");
            var html = await response.Content.ReadAsStringAsync();
            Check(html.Contains("<html lang=\"he\" dir=\"rtl\">", StringComparison.Ordinal),
                  "Hebrew RTL document");
            Check(html.Contains("href=\"#main\"", StringComparison.Ordinal) &&
                  html.Contains("aria-live=\"polite\"", StringComparison.Ordinal) &&
                  html.Contains("<dialog ", StringComparison.Ordinal),
                  "keyboard skip-link, live feedback and modal markup");
            Check(html.Contains("ניהול נוכחות", StringComparison.Ordinal) &&
                  html.Contains("נתונים סינתטיים", StringComparison.Ordinal),
                  "obvious synthetic-only UI disclaimer");
            Check(!html.Contains("TwilioClient.Init", StringComparison.Ordinal) &&
                  !html.Contains("private-test-student", StringComparison.Ordinal),
                  "no production secrets or student identifiers in HTML");

            using var cssResponse = await client.GetAsync("/app/assets/style.css");
            Check(cssResponse.StatusCode == HttpStatusCode.OK &&
                  cssResponse.Content.Headers.ContentType?.MediaType == "text/css",
                  "CSS served");
            var css = await cssResponse.Content.ReadAsStringAsync();
            Check(css.Contains("@media(max-width:720px)", StringComparison.Ordinal) &&
                  css.Contains("focus-visible", StringComparison.Ordinal),
                  "responsive CSS and keyboard focus rules");

            using var jsResponse = await client.GetAsync("/app/assets/app.js");
            Check(jsResponse.StatusCode == HttpStatusCode.OK &&
                  jsResponse.Content.Headers.ContentType?.MediaType == "application/javascript",
                  "JavaScript served");
            var js = await jsResponse.Content.ReadAsStringAsync();
            Check(js.Contains("apiGet(\"/api/v1/overview\"", StringComparison.Ordinal) &&
                  js.Contains("apiGet(\"/api/v1/corrections", StringComparison.Ordinal),
                  "real API integration exists");
            Check(!js.Contains("localStorage", StringComparison.Ordinal) &&
                  !js.Contains("sessionStorage", StringComparison.Ordinal) &&
                  !js.Contains("innerHTML", StringComparison.Ordinal),
                  "no browser credential storage or raw HTML insertion");
            Check(js.Contains("if (!isDemo() || !hasStaff()) return;", StringComparison.Ordinal),
                  "CSV is demo and role gated");

            using var missing = await client.GetAsync("/app/assets/../../api/v1/overview");
            Check(missing.StatusCode != HttpStatusCode.OK,
                  "no unprotected backend exposed through preview");
            using var backend = await client.GetAsync("/api/v1/overview");
            Check(backend.StatusCode == HttpStatusCode.NotFound,
                  "synthetic preview has no real attendance API");

            Console.WriteLine("Admin UI localhost smoke passed: Hebrew RTL, assets, CSP, " +
                              "no PII, no credentials, demo-only export and no backend.");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static void Check(bool valid, string condition)
    {
        if (!valid) throw new InvalidOperationException(
            "Admin UI smoke check failed: " + condition);
    }
}
