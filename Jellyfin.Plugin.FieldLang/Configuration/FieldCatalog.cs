using Jellyfin.Plugin.FieldLang.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.FieldLang.Configuration;

/// <summary>
/// One field that can be pulled in its own language.
/// </summary>
/// <param name="Name">Stable identifier, persisted in configuration.</param>
/// <param name="Label">Human label for the configuration page.</param>
/// <param name="LockField">The field that pins this value, or null if Jellyfin has no lock for it.</param>
/// <param name="Apply">Copies the localized value onto the item; returns true if anything changed.</param>
public sealed record LocalizableField(
    string Name,
    string Label,
    MetadataField? LockField,
    Func<LocalizedFields, BaseItem, bool> Apply);

/// <summary>
/// The item types and fields this plugin knows how to localize.
/// </summary>
/// <remarks>
/// Everything about a field lives in one entry here: its id, its label, whether it can be locked,
/// and how to move the value from a TMDb payload onto an item. Supporting a new field is one line
/// in this file -- the config page renders itself from this catalog over
/// <c>GET /FieldLang/Schema</c>, and the applier just iterates it.
/// </remarks>
public static class FieldCatalog
{
    /// <summary>Item type identifier for movies.</summary>
    public const string Movie = "Movie";

    /// <summary>Item type identifier for series.</summary>
    public const string Series = "Series";

    /// <summary>Item type identifier for seasons.</summary>
    public const string Season = "Season";

    /// <summary>Item type identifier for episodes.</summary>
    public const string Episode = "Episode";

    private static readonly LocalizableField _name = Text(
        "Name", "Title", MetadataField.Name,
        src => src.Name, item => item.Name, (item, value) => item.Name = value);

    private static readonly LocalizableField _overview = Text(
        "Overview", "Description", MetadataField.Overview,
        src => src.Overview, item => item.Overview, (item, value) => item.Overview = value);

    private static readonly LocalizableField _tagline = Text(
        "Tagline", "Tagline", null,
        src => src.Tagline, item => item.Tagline, (item, value) => item.Tagline = value);

    private static readonly LocalizableField _genres = new(
        "Genres", "Genres", MetadataField.Genres,
        (src, item) =>
        {
            if (src.Genres is not { Length: > 0 } genres
                || (item.Genres is not null && item.Genres.SequenceEqual(genres, StringComparer.Ordinal)))
            {
                return false;
            }

            item.Genres = genres;
            return true;
        });

    /// <summary>
    /// Gets the fields supported per item type.
    /// </summary>
    /// <remarks>
    /// Only fields TMDb actually returns per-language are listed. Season and episode payloads carry
    /// name and overview alone, so offering a Tagline control there would render something that
    /// could never do anything.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyList<LocalizableField>> ByItemType { get; } =
        new Dictionary<string, IReadOnlyList<LocalizableField>>(StringComparer.Ordinal)
        {
            [Movie] = new[] { _name, _overview, _tagline, _genres },
            [Series] = new[] { _name, _overview, _tagline, _genres },
            [Season] = new[] { _name, _overview },
            [Episode] = new[] { _name, _overview },
        };

    /// <summary>Gets every item type the plugin handles, in display order.</summary>
    public static IReadOnlyList<string> ItemTypes { get; } = new[] { Movie, Series, Season, Episode };

    /// <summary>Looks up a field definition.</summary>
    /// <param name="itemType">The item type.</param>
    /// <param name="fieldName">The field name.</param>
    /// <returns>The field, or null when unsupported.</returns>
    public static LocalizableField? Find(string itemType, string fieldName) =>
        ByItemType.TryGetValue(itemType, out var fields)
            ? fields.FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.Ordinal))
            : null;

    /// <summary>
    /// Builds a text field. An empty localized value is skipped rather than written, because TMDb
    /// answers a request for a language it lacks with the field present but blank -- and wiping a
    /// description is worse than leaving it in the wrong language.
    /// </summary>
    private static LocalizableField Text(
        string name,
        string label,
        MetadataField? lockField,
        Func<LocalizedFields, string?> read,
        Func<BaseItem, string?> get,
        Action<BaseItem, string> set) =>
        new(name, label, lockField, (src, item) =>
        {
            var value = read(src);
            if (string.IsNullOrWhiteSpace(value) || string.Equals(get(item), value, StringComparison.Ordinal))
            {
                return false;
            }

            set(item, value);
            return true;
        });
}
