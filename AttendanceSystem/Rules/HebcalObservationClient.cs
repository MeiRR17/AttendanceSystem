using System.Globalization;
using System.Text.Json;

namespace AttendanceSystem.Rules;

/// <summary>
/// Optional, explicitly invoked Hebcal observation feed. Output is descriptive:
/// a holiday entry NEVER automatically closes an institution or deletes attendance.
/// </summary>
public sealed class HebcalObservationClient
{
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly Dictionary<(int Year, bool Israel), CacheEntry> _cache = [];
    private readonly object _gate = new();

    public HebcalObservationClient(HttpClient http, TimeProvider? clock = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<HebcalObservations> FetchYearAsync(
        int year, bool israel, CancellationToken cancellationToken = default)
    {
        if (year is < 1900 or > 2100)
            throw new ArgumentOutOfRangeException(nameof(year), "Year must be explicitly selected.");

        var now = _clock.GetUtcNow();
        CacheEntry? cached;
        lock (_gate)
        {
            _cache.TryGetValue((year, israel), out cached);
            if (cached is not null && now - cached.FetchedAt < TimeSpan.FromHours(12))
                return new HebcalObservations(cached.Items, FromCache: true, IsStale: false);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var url = "https://www.hebcal.com/hebcal?v=1&cfg=json&yt=G&month=x&year=" +
                      year.ToString(CultureInfo.InvariantCulture) +
                      "&i=" + (israel ? "on" : "off") +
                      "&maj=on&min=on&mod=on&mf=on&nx=on&ss=off&s=off";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 1024 * 1024)
                throw new InvalidOperationException("Hebcal payload exceeds 1 MiB.");

            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var data = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) != 0)
            {
                if (data.Length + read > 1024 * 1024)
                    throw new InvalidOperationException("Hebcal payload exceeds 1 MiB.");
                data.Write(chunk, 0, read);
            }

            using var document = JsonDocument.Parse(data.ToArray());
            var items = new List<HebcalObservation>();
            if (!document.RootElement.TryGetProperty("items", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Unexpected Hebcal response shape.");
            foreach (var item in entries.EnumerateArray())
            {
                if (items.Count > 2000)
                    throw new InvalidOperationException("Hebcal event count exceeds safe bound.");

                var dayText = item.GetProperty("date").GetString();
                if (!DateOnly.TryParseExact(dayText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                            DateTimeStyles.None, out var date))
                    continue;
                if (date.Year != year)
                    continue;

                var title = item.TryGetProperty("title", out var titleElement)
                    ? titleElement.GetString() ?? "" : "";
                var category = item.TryGetProperty("category", out var categoryElement)
                    ? categoryElement.GetString() ?? "" : "";
                var yomTov = item.TryGetProperty("yomtov", out var yomTovElement) &&
                             yomTovElement.ValueKind == JsonValueKind.True;
                items.Add(new HebcalObservation(date,
                    title.Length > 160 ? title[..160] : title,
                    category.Length > 60 ? category[..60] : category,
                    yomTov));
            }
            var observations = items.OrderBy(x => x.Date).ToArray();
            lock (_gate)
                _cache[(year, israel)] = new CacheEntry(observations, now);
            return new HebcalObservations(observations, FromCache: false, IsStale: false);
        }
        catch (Exception error) when (
            error is HttpRequestException or TaskCanceledException or JsonException or
            InvalidOperationException or KeyNotFoundException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            if (cached is not null && now - cached.FetchedAt < TimeSpan.FromDays(7))
                return new HebcalObservations(cached.Items, FromCache: true, IsStale: true);
            throw new InvalidOperationException(
                "Calendar observations unavailable. Institution policy remains unchanged.", error);
        }
    }

    private sealed record CacheEntry(IReadOnlyList<HebcalObservation> Items, DateTimeOffset FetchedAt);
}

public sealed record HebcalObservation(DateOnly Date, string Title, string Category, bool YomTov);
public sealed record HebcalObservations(
    IReadOnlyList<HebcalObservation> Items, bool FromCache, bool IsStale)
{
    public const string Attribution = "Hebcal.com — CC BY 4.0";
}
