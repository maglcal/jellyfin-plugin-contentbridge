using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ContentBridge.Services
{
    /// <summary>
    /// Dual-channel plugin update check. The primary channel (a self-hosted mirror of the
    /// plugin catalog, url configured at runtime in the plugin settings) is tried first;
    /// whenever it is unreachable or malformed the check automatically degrades to the
    /// built-in GitHub channel. The mirror url is never hardcoded — it only ever lives in
    /// the operator's own configuration.
    /// </summary>
    public class UpdateChecker
    {
        /// <summary>
        /// Built-in fallback channel: the public GitHub catalog (repository.json).
        /// </summary>
        public const string FallbackRepositoryUrl =
            "https://raw.githubusercontent.com/maglcal/jellyfin-plugin-contentbridge/main/repository.json";

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private readonly Func<string, CancellationToken, Task<string>> _fetch;

        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateChecker"/> class.
        /// </summary>
        /// <param name="fetch">Fetches a catalog url and returns its body as text.</param>
        public UpdateChecker(Func<string, CancellationToken, Task<string>> fetch)
        {
            _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
        }

        /// <summary>
        /// Default fetcher: plain HTTP GET returning the response body as text.
        /// </summary>
        public static Func<string, CancellationToken, Task<string>> CreateDefaultFetcher()
        {
            return async (url, cancellationToken) =>
            {
                using (var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            };
        }

        /// <summary>
        /// Check for a newer plugin version: the primary channel first (when configured),
        /// automatically degrading to the GitHub channel on any failure.
        /// </summary>
        /// <param name="primaryUrl">Primary catalog url (empty = GitHub only).</param>
        /// <param name="currentVersion">Currently installed plugin version.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Update check result, never null.</returns>
        public async Task<UpdateCheckResult> CheckAsync(string primaryUrl, string currentVersion, CancellationToken cancellationToken)
        {
            var result = new UpdateCheckResult { CurrentVersion = currentVersion ?? string.Empty };
            var hasPrimary = !string.IsNullOrWhiteSpace(primaryUrl);

            if (hasPrimary)
            {
                try
                {
                    var manifest = await LoadAsync(primaryUrl, cancellationToken).ConfigureAwait(false);
                    Fill(result, manifest, "primary", degraded: false);
                    return result;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.PrimaryError = ex.Message;
                }
            }

            try
            {
                var manifest = await LoadAsync(FallbackRepositoryUrl, cancellationToken).ConfigureAwait(false);
                Fill(result, manifest, "fallback", degraded: hasPrimary);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = hasPrimary
                    ? "主渠道不可用：" + result.PrimaryError + "；备用渠道不可用：" + ex.Message
                    : "更新渠道不可用：" + ex.Message;
                return result;
            }
        }

        private async Task<UpdateManifest> LoadAsync(string url, CancellationToken cancellationToken)
        {
            var json = await _fetch(url, cancellationToken).ConfigureAwait(false);
            var manifest = ParseManifest(json);
            if (manifest == null)
            {
                throw new InvalidOperationException("更新清单格式无法解析");
            }

            return manifest;
        }

        private static void Fill(UpdateCheckResult result, UpdateManifest manifest, string channel, bool degraded)
        {
            result.Success = true;
            result.Channel = channel;
            result.Degraded = degraded;
            result.LatestVersion = manifest.LatestVersion;
            result.DownloadUrl = manifest.SourceUrl;
            result.ImageUrl = manifest.ImageUrl;
            result.Changelog = manifest.Changelog;
            result.Timestamp = manifest.Timestamp;
            result.UpdateAvailable = CompareVersions(manifest.LatestVersion, result.CurrentVersion) > 0;
        }

        /// <summary>
        /// Parse a plugin catalog (repository.json) document: a list with one entry per
        /// plugin, each carrying a versions array. Returns the newest version entry
        /// (with the entry's imageUrl). A single-object root is accepted too.
        /// </summary>
        /// <param name="json">Catalog document text.</param>
        /// <returns>The newest version entry, or null when the document is unusable.</returns>
        public static UpdateManifest ParseManifest(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                return null;
            }

            using (doc)
            {
                var root = doc.RootElement;
                JsonElement entry = default;
                var found = false;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        entry = item;
                        found = true;
                        if (item.TryGetProperty("guid", out var guid)
                            && string.Equals(guid.GetString(), "b42fcd11-31f8-40fb-b605-77fc7b182edc", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    entry = root;
                    found = true;
                }

                if (!found
                    || !entry.TryGetProperty("versions", out var versions)
                    || versions.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                UpdateManifest best = null;
                foreach (var v in versions.EnumerateArray())
                {
                    if (v.ValueKind != JsonValueKind.Object || !v.TryGetProperty("version", out var verEl))
                    {
                        continue;
                    }

                    var version = verEl.ValueKind == JsonValueKind.String
                        ? verEl.GetString()
                        : verEl.ToString();
                    if (string.IsNullOrWhiteSpace(version))
                    {
                        continue;
                    }

                    var manifest = new UpdateManifest
                    {
                        LatestVersion = version,
                        SourceUrl = GetString(v, "sourceUrl"),
                        Changelog = GetString(v, "changelog"),
                        Timestamp = GetString(v, "timestamp"),
                    };
                    if (best == null || CompareVersions(manifest.LatestVersion, best.LatestVersion) > 0)
                    {
                        best = manifest;
                    }
                }

                if (best == null)
                {
                    return null;
                }

                best.ImageUrl = GetString(entry, "imageUrl");
                return best;
            }
        }

        private static string GetString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : string.Empty;
        }

        /// <summary>
        /// Compare two version strings per the project naming rules: an optional "main-v"/"v"
        /// prefix is stripped, then segments are compared numerically with zero fill
        /// (0.3.0 &lt; 0.3.0.1 &lt; 0.3.1).
        /// </summary>
        /// <param name="a">First version.</param>
        /// <param name="b">Second version.</param>
        /// <returns>-1 / 0 / 1.</returns>
        public static int CompareVersions(string a, string b)
        {
            var pa = SplitVersion(a);
            var pb = SplitVersion(b);
            var n = Math.Max(pa.Length, pb.Length);
            for (var i = 0; i < n; i++)
            {
                var va = i < pa.Length ? pa[i] : 0;
                var vb = i < pb.Length ? pb[i] : 0;
                if (va != vb)
                {
                    return va < vb ? -1 : 1;
                }
            }

            return 0;
        }

        private static int[] SplitVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return Array.Empty<int>();
            }

            var s = version.Trim();
            if (s.StartsWith("main-v", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(6);
            }
            else if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(1);
            }

            return s.Split('.')
                .Select(p => int.TryParse(p.Trim(), out var n) ? n : 0)
                .ToArray();
        }
    }

    /// <summary>
    /// Result of a dual-channel update check.
    /// </summary>
    public class UpdateCheckResult
    {
        /// <summary>Gets or sets a value indicating whether any channel answered.</summary>
        public bool Success { get; set; }

        /// <summary>Gets or sets the currently installed version.</summary>
        public string CurrentVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets the newest version found.</summary>
        public string LatestVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether a newer version exists.</summary>
        public bool UpdateAvailable { get; set; }

        /// <summary>Gets or sets the channel that answered: "primary" or "fallback".</summary>
        public string Channel { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether the primary channel failed and the fallback was used.</summary>
        public bool Degraded { get; set; }

        /// <summary>Gets or sets the download url of the newest version (same channel).</summary>
        public string DownloadUrl { get; set; } = string.Empty;

        /// <summary>Gets or sets the icon url from the catalog entry.</summary>
        public string ImageUrl { get; set; } = string.Empty;

        /// <summary>Gets or sets the changelog of the newest version.</summary>
        public string Changelog { get; set; } = string.Empty;

        /// <summary>Gets or sets the publish timestamp of the newest version.</summary>
        public string Timestamp { get; set; } = string.Empty;

        /// <summary>Gets or sets the primary channel error message (when degraded).</summary>
        public string PrimaryError { get; set; } = string.Empty;

        /// <summary>Gets or sets the error message (when no channel answered).</summary>
        public string Error { get; set; } = string.Empty;
    }

    /// <summary>
    /// Newest version entry of a plugin catalog document.
    /// </summary>
    public class UpdateManifest
    {
        /// <summary>Gets or sets the newest version string.</summary>
        public string LatestVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets the download url of the newest version.</summary>
        public string SourceUrl { get; set; } = string.Empty;

        /// <summary>Gets or sets the icon url of the catalog entry.</summary>
        public string ImageUrl { get; set; } = string.Empty;

        /// <summary>Gets or sets the changelog of the newest version.</summary>
        public string Changelog { get; set; } = string.Empty;

        /// <summary>Gets or sets the publish timestamp of the newest version.</summary>
        public string Timestamp { get; set; } = string.Empty;
    }
}
