using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Jellyfin.Plugin.FieldLang.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FieldLang.Tmdb;

/// <summary>
/// The localized values TMDb returned for one item in one language.
/// </summary>
/// <remarks>
/// Every property is nullable on purpose. TMDb answers a request for a language it has no data for
/// with the field present but empty, and blanking a populated field would be worse than leaving the
/// core provider's value in place. Null here means "TMDb gave us nothing usable, do not touch".
/// </remarks>
public sealed class LocalizedFields
{
    /// <summary>Gets or sets the localized title.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the localized description.</summary>
    public string? Overview { get; set; }

    /// <summary>Gets or sets the localized tagline.</summary>
    public string? Tagline { get; set; }

    /// <summary>Gets or sets the localized genre names.</summary>
    public string[]? Genres { get; set; }
}

/// <summary>
/// Minimal TMDb reader for per-language field values.
/// </summary>
public sealed class TmdbLocalizedClient
{
    private const string BaseUrl = "https://api.themoviedb.org/3";

    private readonly HttpClient _http;
    private readonly ILogger<TmdbLocalizedClient> _logger;

    // Keyed by "type:id:lang". A sweep over a whole library asks for the same series or season
    // repeatedly (once per episode), so this collapses a lot of identical requests.
    private readonly ConcurrentDictionary<string, LocalizedFields?> _cache = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="TmdbLocalizedClient"/> class.</summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public TmdbLocalizedClient(IHttpClientFactory httpClientFactory, ILogger<TmdbLocalizedClient> logger)
    {
        _http = httpClientFactory.CreateClient(NamedClient.Default);
        _logger = logger;
    }

    /// <summary>Drops all cached responses.</summary>
    public void ClearCache() => _cache.Clear();

    /// <summary>Fetches localized fields for a movie.</summary>
    /// <param name="tmdbId">TMDb movie id.</param>
    /// <param name="language">Requested language.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Localized fields, or null on failure.</returns>
    public Task<LocalizedFields?> GetMovieAsync(string tmdbId, string language, CancellationToken cancellationToken)
        => GetAsync($"movie/{tmdbId}", $"movie:{tmdbId}", language, cancellationToken);

    /// <summary>Fetches localized fields for a series.</summary>
    /// <param name="tmdbId">TMDb series id.</param>
    /// <param name="language">Requested language.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Localized fields, or null on failure.</returns>
    public Task<LocalizedFields?> GetSeriesAsync(string tmdbId, string language, CancellationToken cancellationToken)
        => GetAsync($"tv/{tmdbId}", $"tv:{tmdbId}", language, cancellationToken);

    /// <summary>Fetches localized fields for a season.</summary>
    /// <param name="seriesTmdbId">TMDb id of the parent series.</param>
    /// <param name="seasonNumber">Season number.</param>
    /// <param name="language">Requested language.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Localized fields, or null on failure.</returns>
    public Task<LocalizedFields?> GetSeasonAsync(string seriesTmdbId, int seasonNumber, string language, CancellationToken cancellationToken)
        => GetAsync(
            $"tv/{seriesTmdbId}/season/{seasonNumber.ToString(CultureInfo.InvariantCulture)}",
            $"season:{seriesTmdbId}:{seasonNumber}",
            language,
            cancellationToken);

    /// <summary>Fetches localized fields for an episode.</summary>
    /// <param name="seriesTmdbId">TMDb id of the parent series.</param>
    /// <param name="seasonNumber">Season number.</param>
    /// <param name="episodeNumber">Episode number.</param>
    /// <param name="language">Requested language.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Localized fields, or null on failure.</returns>
    public Task<LocalizedFields?> GetEpisodeAsync(string seriesTmdbId, int seasonNumber, int episodeNumber, string language, CancellationToken cancellationToken)
        => GetAsync(
            $"tv/{seriesTmdbId}/season/{seasonNumber.ToString(CultureInfo.InvariantCulture)}/episode/{episodeNumber.ToString(CultureInfo.InvariantCulture)}",
            $"episode:{seriesTmdbId}:{seasonNumber}:{episodeNumber}",
            language,
            cancellationToken);

    private async Task<LocalizedFields?> GetAsync(string path, string cacheKey, string language, CancellationToken cancellationToken)
    {
        var key = cacheKey + ":" + language;
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var apiKey = Plugin.Instance?.Configuration.TmdbApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("No TMDb API key configured; cannot localize fields");
            return null;
        }

        var url = $"{BaseUrl}/{path}?api_key={Uri.EscapeDataString(apiKey)}&language={Uri.EscapeDataString(language)}";

        LocalizedFields? result = null;
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Genuinely absent upstream (common for episodes of loosely-tracked shows).
                // Cache the miss so a sweep does not ask 400 times.
                _cache[key] = null;
                return null;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Do NOT cache -- a throttle is transient and caching it would turn a temporary
                // 429 into a permanent "no data" for the rest of the process lifetime.
                _logger.LogWarning("TMDb rate-limited the request for {Path}; skipping this item for now", path);
                return null;
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            result = Map(doc.RootElement);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Transient too -- leave it uncached so the next pass retries.
            _logger.LogWarning(ex, "TMDb lookup failed for {Path} in {Language}", path, language);
            return null;
        }

        _cache[key] = result;
        return result;
    }

    private static LocalizedFields Map(JsonElement root)
    {
        var fields = new LocalizedFields
        {
            // Movies use "title", everything else uses "name".
            Name = ReadString(root, "title") ?? ReadString(root, "name"),
            Overview = ReadString(root, "overview"),
            Tagline = ReadString(root, "tagline"),
        };

        if (root.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
        {
            var names = genres.EnumerateArray()
                .Select(g => ReadString(g, "name"))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToArray();

            if (names.Length > 0)
            {
                fields.Genres = names;
            }
        }

        return fields;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var s = value.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
