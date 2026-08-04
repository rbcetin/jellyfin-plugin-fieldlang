using Jellyfin.Plugin.FieldLang.Tmdb;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.FieldLang;

/// <summary>
/// Registers the plugin's services with the host.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Singleton so the TMDb response cache survives between scheduled-task runs.
        serviceCollection.AddSingleton<TmdbLocalizedClient>();
        serviceCollection.AddSingleton<FieldLangApplier>();
    }
}
