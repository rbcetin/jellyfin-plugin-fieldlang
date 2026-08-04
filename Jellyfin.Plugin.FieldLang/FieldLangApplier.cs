using System.Globalization;
using Jellyfin.Plugin.FieldLang.Configuration;
using Jellyfin.Plugin.FieldLang.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FieldLang;

/// <summary>
/// Applies the configured per-field languages to a single item.
/// </summary>
public sealed class FieldLangApplier
{
    private readonly ILibraryManager _libraryManager;
    private readonly TmdbLocalizedClient _tmdb;
    private readonly ILogger<FieldLangApplier> _logger;

    /// <summary>Initializes a new instance of the <see cref="FieldLangApplier"/> class.</summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="tmdb">TMDb reader.</param>
    /// <param name="logger">Logger.</param>
    public FieldLangApplier(ILibraryManager libraryManager, TmdbLocalizedClient tmdb, ILogger<FieldLangApplier> logger)
    {
        _libraryManager = libraryManager;
        _tmdb = tmdb;
        _logger = logger;
    }

    /// <summary>Maps a Jellyfin item to a catalog item type.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The item type name, or null when unsupported.</returns>
    public static string? ResolveItemType(BaseItem item) => item switch
    {
        Movie => FieldCatalog.Movie,
        Series => FieldCatalog.Series,
        Season => FieldCatalog.Season,
        Episode => FieldCatalog.Episode,
        _ => null,
    };

    /// <summary>
    /// Applies every matching rule to one item and saves it if anything actually changed.
    /// </summary>
    /// <param name="item">The item to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the item was modified and written back.</returns>
    /// <remarks>
    /// Writing only on a real difference keeps repeat sweeps cheap: a second run over an
    /// already-correct library issues no database writes at all.
    /// </remarks>
    public async Task<bool> ApplyAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || ResolveItemType(item) is not { } itemType)
        {
            return false;
        }

        var rules = ResolveRules(item, itemType, config);
        if (rules.Count == 0)
        {
            return false;
        }

        var changed = false;
        var locked = new HashSet<MetadataField>(item.LockedFields ?? Array.Empty<MetadataField>());
        var lockedBefore = locked.Count;

        // One TMDb request per distinct language, not per rule.
        foreach (var group in rules.GroupBy(r => r.Language, StringComparer.OrdinalIgnoreCase))
        {
            var localized = await FetchAsync(item, itemType, group.Key, cancellationToken).ConfigureAwait(false);
            if (localized is null)
            {
                continue;
            }

            foreach (var rule in group)
            {
                if (FieldCatalog.Find(itemType, rule.Field) is not { } field)
                {
                    continue;
                }

                if (field.Apply(localized, item))
                {
                    changed = true;
                    _logger.LogDebug("FieldLang: {Item} {Field} -> {Language}", item.Name, rule.Field, rule.Language);
                }

                if (rule.Lock && field.LockField is { } lockField)
                {
                    locked.Add(lockField);
                }
                else if (!rule.Lock && field.LockField is { } unlockField)
                {
                    // Unticking the box has to actually release the field, otherwise a lock set by
                    // an earlier run would stay forever with no way back short of editing the item.
                    locked.Remove(unlockField);
                }
            }
        }

        if (locked.Count != lockedBefore)
        {
            item.LockedFields = locked.ToArray();
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private List<FieldLanguageRule> ResolveRules(BaseItem item, string itemType, PluginConfiguration config)
    {
        // Match by library id, so renaming a library in Jellyfin does not orphan its rules.
        var libraryIds = _libraryManager.GetCollectionFolders(item)
            .Select(f => f.Id.ToString("N", CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return config.Libraries
            .Where(l => libraryIds.Contains(l.LibraryId))
            .SelectMany(l => l.Rules)
            .Where(r => string.Equals(r.ItemType, itemType, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(r.Language))
            .ToList();
    }

    private Task<LocalizedFields?> FetchAsync(BaseItem item, string itemType, string language, CancellationToken cancellationToken)
    {
        var none = Task.FromResult<LocalizedFields?>(null);

        switch (itemType)
        {
            case FieldCatalog.Movie when item.GetProviderId(MetadataProvider.Tmdb) is { Length: > 0 } id:
                return _tmdb.GetMovieAsync(id, language, cancellationToken);

            case FieldCatalog.Series when item.GetProviderId(MetadataProvider.Tmdb) is { Length: > 0 } id:
                return _tmdb.GetSeriesAsync(id, language, cancellationToken);

            // Seasons and episodes are addressed through their series, so they need the parent's id
            // plus their own numbering -- a loose episode with no series link cannot be looked up.
            case FieldCatalog.Season when item is Season { IndexNumber: { } season } s
                                          && s.Series?.GetProviderId(MetadataProvider.Tmdb) is { Length: > 0 } seriesId:
                return _tmdb.GetSeasonAsync(seriesId, season, language, cancellationToken);

            case FieldCatalog.Episode when item is Episode { ParentIndexNumber: { } season, IndexNumber: { } number } e
                                           && e.Series?.GetProviderId(MetadataProvider.Tmdb) is { Length: > 0 } seriesId:
                return _tmdb.GetEpisodeAsync(seriesId, season, number, language, cancellationToken);

            default:
                return none;
        }
    }
}
