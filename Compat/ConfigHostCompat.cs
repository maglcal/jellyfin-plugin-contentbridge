using System;

namespace Jellyfin.Plugin.ContentBridge.Compat
{
    /// <summary>
    /// Configuration-access seam (配置存取). All reads of the plugin configuration go
    /// through here so the host-touching point stays in one place:
    /// - Both hosts expose BasePlugin&lt;T&gt; under the same namespace (different assembly
    ///   names are handled by the build matrix, see docs/EMBY-COMPAT.md).
    /// - Web-side configuration save/load differences (ApiClient on the config page) are
    ///   handled by the small apiGet/apiPost/normalize helper layer in configPage.html.
    /// </summary>
    public static class ConfigHostCompat
    {
        /// <summary>
        /// Gets the current plugin configuration (never null; a default instance is returned
        /// when the plugin instance is not available, e.g. in tests).
        /// </summary>
        public static PluginConfiguration Current => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Gets a value indicating whether the plugin instance is available (host finished loading us).
        /// </summary>
        public static bool IsLoaded => Plugin.Instance != null;

        /// <summary>
        /// Gets the plugin assembly version string (the "current version" base for update checks).
        /// </summary>
        public static string VersionString => typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    }
}
