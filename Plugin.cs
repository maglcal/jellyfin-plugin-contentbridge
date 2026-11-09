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
    /// ContentBridge plugin entry point.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
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
            return new[]
            {
                PluginPageCompat.BuildPageInfo(
                    Name,
                    "ContentBridge 设置",
                    GetType().Namespace + ".Configuration.configPage.html")
            };
        }
    }
}
