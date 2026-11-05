using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ContentBridge
{
    /// <summary>
    /// Plugin configuration.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Gets or sets the root folder where virtual library entries (.strm/.nfo/poster) are written.
        /// Add this folder to Jellyfin as a "Mixed Movies and Shows" library.
        /// </summary>
        public string LibraryRoot { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether local .nfo metadata files are written.
        /// </summary>
        public bool WriteNfo { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether poster images are downloaded locally.
        /// </summary>
        public bool DownloadImages { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether generated entries whose source no longer
        /// lists them are deleted on sync. Off by default.
        /// </summary>
        public bool CleanupRemoved { get; set; } = false;

        /// <summary>
        /// Gets or sets the preferred play-source keyword (e.g. "m3u8"). When a title offers
        /// multiple play lines, the line whose flag contains this keyword wins.
        /// </summary>
        public string PreferredQuality { get; set; } = "m3u8";

        /// <summary>
        /// Gets or sets the public base url used in generated proxy links (e.g. http://192.168.1.10:8096).
        /// Leave empty to auto-detect a local url. Fill this in when clients reach Jellyfin via a
        /// public domain or a different address than the server's LAN ip.
        /// </summary>
        public string ProxyBaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the access key embedded in generated proxy links (auto-generated on first use).
        /// </summary>
        public string ProxyKey { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the configured content sources.
        /// </summary>
        public List<SourceConfig> Sources { get; set; } = new List<SourceConfig>();
    }

    /// <summary>
    /// One external content source (standard MaccCMS-style vod provide API).
    /// </summary>
    public class SourceConfig
    {
        /// <summary>Gets or sets the display name (also used as subfolder name).</summary>
        public string Name { get; set; } = "默认源";

        /// <summary>Gets or sets the API base url, e.g. https://host/api.php/provide/vod/</summary>
        public string ApiUrl { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether this source is active.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Gets or sets the category id filter (0 = all categories).</summary>
        public int TypeId { get; set; } = 0;

        /// <summary>Gets or sets the max number of items pulled per sync for this source.</summary>
        public int MaxItems { get; set; } = 20;

        /// <summary>
        /// Gets or sets a value indicating whether generated .strm links go through the local
        /// stream proxy (needed when the source requires a User-Agent or Referer header).
        /// </summary>
        public bool UseProxy { get; set; } = false;

        /// <summary>Gets or sets the User-Agent header sent to this source (proxy mode only).</summary>
        public string UserAgent { get; set; } = string.Empty;

        /// <summary>Gets or sets the Referer header sent to this source (proxy mode only).</summary>
        public string Referer { get; set; } = string.Empty;
    }
}
