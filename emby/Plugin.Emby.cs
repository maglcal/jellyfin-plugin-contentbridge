using System.IO;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;

namespace Jellyfin.Plugin.ContentBridge
{
    /// <summary>
    /// Emby-only half of the shared <see cref="Plugin"/> class: the plugin tile image.
    ///
    /// Emby reads the "My Plugins" thumbnail through <see cref="IHasThumbImage"/> straight
    /// off the plugin assembly (since Emby 3.6); Jellyfin instead reads meta.json's
    /// imagePath, and has no IHasThumbImage interface at all. The interface only exists on
    /// the Emby reference surface, so it is implemented here in a partial that only the
    /// Emby build compiles (single source, per-host thin shells — docs/EMBY-COMPAT.md).
    /// </summary>
    public partial class Plugin : IHasThumbImage
    {
        /// <inheritdoc />
        public Stream GetThumbImage()
        {
            return GetType().Assembly.GetManifestResourceStream(GetType().Namespace + ".Configuration.thumb.png");
        }

        /// <inheritdoc />
        public ImageFormat ThumbImageFormat => ImageFormat.Png;
    }
}
