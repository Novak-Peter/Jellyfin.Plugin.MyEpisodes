using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MyEpisodes
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public List<MyEpisodesUserConfiguration> UserConfigurations { get; set; } = new();
    }

    public class MyEpisodesUserConfiguration
    {
        public string JellyfinUserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool SyncWatched { get; set; } = true;
    }
}
