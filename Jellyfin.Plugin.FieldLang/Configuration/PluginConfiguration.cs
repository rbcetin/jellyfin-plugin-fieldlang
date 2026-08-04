using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.FieldLang.Configuration;

/// <summary>
/// One field pinned to one language, within a library.
/// </summary>
public class FieldLanguageRule
{
    /// <summary>Gets or sets the item type (see <see cref="FieldCatalog"/>).</summary>
    public string ItemType { get; set; } = string.Empty;

    /// <summary>Gets or sets the field name (see <see cref="FieldCatalog"/>).</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the language, ISO 639-1 and optionally region-qualified ("pt-BR").
    /// Empty leaves the field alone.
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this field is pinned against provider writes.
    /// Ignored for fields Jellyfin cannot lock, such as Tagline.
    /// </summary>
    /// <remarks>
    /// Defaults to true, which is also what configs written before this field existed deserialize
    /// to, since XmlSerializer leaves an absent element at its initialized value.
    /// </remarks>
    public bool Lock { get; set; } = true;
}

/// <summary>
/// The rules that apply to one library.
/// </summary>
public class LibraryRuleSet
{
    /// <summary>Gets or sets the virtual folder id these rules apply to.</summary>
    public string LibraryId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the library name, for display only. Matching is by id, so renaming a library
    /// does not break its rules.
    /// </summary>
    public string LibraryName { get; set; } = string.Empty;

    /// <summary>Gets or sets the per-field rules for this library.</summary>
    public List<FieldLanguageRule> Rules { get; set; } = new();
}

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the TMDb API key (v3). Required -- the server's built-in key is compiled into
    /// the core assembly and is not reachable from a plugin.
    /// </summary>
    public string TmdbApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the per-library rule sets.</summary>
    public List<LibraryRuleSet> Libraries { get; set; } = new();
}
