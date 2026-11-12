// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ContentBridge.Compat;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ContentBridge
{
    /// <summary>
    /// ContentBridge plugin entry point (shared across hosts; the Emby-only half —
    /// the plugin tile image — lives in emby/Plugin.Emby.cs).
    /// </summary>
    public partial class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <inheritdoc />
        public override string Name => "ContentBridge";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("b42fcd11-31f8-40fb-b605-77fc7b182edc");

        /// <summary>
        /// Gets the singleton plugin instance.
        /// </summary>
        public static Plugin Instance { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            // Host-specific page members (Jellyfin sidebar entry) are set opportunistically
            // by PluginPageCompat, so this compiles and runs on both hosts (see Compat/).
            if (HostCompat.Detect() == HostKind.Emby)
            {
                // Emby injects the page HTML into the dashboard and never executes inline
                // <script> blocks, so the Emby page declares data-controller and ships its
                // logic as a second, non-menu page (an AMD module) — see
                // Configuration/embyConfigPage.js.
                return new[]
                {
                    PluginPageCompat.BuildEmbyMainPageInfo(
                        Name,
                        "ContentBridge 设置",
                        GetType().Namespace + ".Configuration.embyConfigPage.html"),
                    PluginPageCompat.BuildControllerPageInfo(
                        EmbyConfigControllerName,
                        GetType().Namespace + ".Configuration.embyConfigPage.js"),
                };
            }

            return new[]
            {
                PluginPageCompat.BuildPageInfo(
                    Name,
                    "ContentBridge 设置",
                    GetType().Namespace + ".Configuration.configPage.html")
            };
        }

        /// <summary>
        /// Name of the Emby config-page controller module, matched by the page root's
        /// data-controller="__plugin/contentbridgejs" attribute (Emby page mechanism).
        /// </summary>
        public const string EmbyConfigControllerName = "contentbridgejs";
    }
}
