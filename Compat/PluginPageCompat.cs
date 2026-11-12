// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ContentBridge.Compat
{
    /// <summary>
    /// Plugin-registration seam (插件注册). Builds the config-page registration info.
    ///
    /// Both hosts share the MediaBrowser.Model.Plugins.PluginPageInfo type, but Jellyfin's
    /// carries extra sidebar members (MenuIcon / EnableInMainMenu) that Emby's does not.
    /// Those are set opportunistically via reflection, so the same source compiles against
    /// either host's reference surface. Emby users reach the page through the regular
    /// 插件 → 设置 entry (sidebar entry is out of scope, see docs/EMBY-COMPAT.md).
    /// </summary>
    public static class PluginPageCompat
    {
        /// <summary>
        /// Build the plugin page info, setting host-specific members when present.
        /// </summary>
        /// <param name="name">Page name (plugin name).</param>
        /// <param name="displayName">Display name.</param>
        /// <param name="embeddedResourcePath">Embedded config page resource path.</param>
        /// <returns>The page info to register.</returns>
        public static PluginPageInfo BuildPageInfo(string name, string displayName, string embeddedResourcePath)
        {
            var page = new PluginPageInfo
            {
                Name = name,
                DisplayName = displayName,
                EmbeddedResourcePath = embeddedResourcePath,
            };

            // Jellyfin-only members (sidebar entry); silently skipped on hosts without them.
            HostCompat.TrySetOptionalProperty(page, "MenuIcon", "sync_alt");
            HostCompat.TrySetOptionalProperty(page, "EnableInMainMenu", true);
            return page;
        }

        /// <summary>
        /// Build the main config-page registration for the Emby page mechanism
        /// (IsMainConfigPage marks it as the plugin's settings page). Optional members are
        /// set opportunistically so this compiles against either host's reference surface.
        /// </summary>
        /// <param name="name">Page name (plugin name).</param>
        /// <param name="displayName">Display name.</param>
        /// <param name="embeddedResourcePath">Embedded Emby config page resource path.</param>
        /// <returns>The page info to register.</returns>
        public static PluginPageInfo BuildEmbyMainPageInfo(string name, string displayName, string embeddedResourcePath)
        {
            var page = new PluginPageInfo
            {
                Name = name,
                DisplayName = displayName,
                EmbeddedResourcePath = embeddedResourcePath,
            };

            HostCompat.TrySetOptionalProperty(page, "IsMainConfigPage", true);
            HostCompat.TrySetOptionalProperty(page, "EnableInMainMenu", true);
            return page;
        }

        /// <summary>
        /// Build a non-menu page registration — used for the Emby config page's AMD
        /// controller module (referenced by the page root's data-controller attribute).
        /// </summary>
        /// <param name="name">Module name (data-controller="__plugin/{name}").</param>
        /// <param name="embeddedResourcePath">Embedded .js resource path.</param>
        /// <returns>The page info to register.</returns>
        public static PluginPageInfo BuildControllerPageInfo(string name, string embeddedResourcePath)
        {
            return new PluginPageInfo
            {
                Name = name,
                EmbeddedResourcePath = embeddedResourcePath,
            };
        }
    }
}
