using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentBridge.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentBridge.Services
{
    /// <summary>
    /// Orchestrates source sync: pull entries, write virtual library files, maintain manifest.
    /// </summary>
    public sealed class SyncService
    {
        private const string ManifestFileName = ".contentbridge-manifest.json";

        private readonly ContentApiClient _client;
        private readonly FeedClient _feed;
        private readonly StrmLibraryWriter _writer;
        private readonly ILogger<SyncService> _logger;

        public SyncService(ContentApiClient client, FeedClient feed, StrmLibraryWriter writer, ILogger<SyncService> logger)
        {
            _client = client;
            _feed = feed;
            _writer = writer;
            _logger = logger;
        }

        /// <summary>
        /// Whether a source speaks the ContentBridge Feed standard instead of MaccCMS.
        /// </summary>
        public static bool IsFeedSource(SourceConfig source)
        {
            return string.Equals(source?.Type, "feed", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Run a full sync across all enabled sources.
        /// </summary>
        public async Task RunAsync(IProgress<double> progress, CancellationToken ct)
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            if (string.IsNullOrWhiteSpace(config.LibraryRoot))
            {
                _logger.LogWarning("ContentBridge: LibraryRoot is not configured, sync skipped");
                return;
            }

            Directory.CreateDirectory(config.LibraryRoot);
            var manifest = LoadManifest(config.LibraryRoot);
            var sources = (config.Sources ?? new List<SourceConfig>())
                .Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.ApiUrl))
                .ToList();

            if (sources.Count == 0)
            {
                _logger.LogInformation("ContentBridge: no enabled sources, nothing to do");
                return;
            }

            for (var i = 0; i < sources.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var source = sources[i];
                try
                {
                    await SyncSourceAsync(source, config, manifest, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError("ContentBridge: sync failed for source {Source}: {Error}", source.Name, ex.Message);
                }

                progress?.Report((i + 1) * 100.0 / sources.Count);
            }

            SaveManifest(config.LibraryRoot, manifest);

            if (config.CleanupRemoved)
            {
                await CleanupAsync(config, manifest, ct).ConfigureAwait(false);
                SaveManifest(config.LibraryRoot, manifest);
            }
        }

        /// <summary>
        /// Sync a single title into the library right away (search-and-add flow).
        /// </summary>
        public async Task<IReadOnlyList<string>> AddTitleAsync(SourceConfig source, string vodId, CancellationToken ct)
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            if (string.IsNullOrWhiteSpace(config.LibraryRoot))
            {
                throw new InvalidOperationException("LibraryRoot 未配置");
            }

            Directory.CreateDirectory(config.LibraryRoot);
            var entry = IsFeedSource(source)
                ? (await _feed.LoadAsync(source, ct).ConfigureAwait(false))
                    .FirstOrDefault(e => string.Equals(e.Id, vodId, StringComparison.Ordinal))
                : await _client.GetDetailAsync(source, vodId, ct).ConfigureAwait(false);
            if (entry == null)
            {
                throw new InvalidOperationException("接口未返回该条目");
            }

            var written = await _writer.WriteAsync(entry, source, config, ct).ConfigureAwait(false);
            var manifest = LoadManifest(config.LibraryRoot);
            UpdateManifest(manifest, source.Name, entry.Id, written);
            SaveManifest(config.LibraryRoot, manifest);
            return written;
        }

        private async Task SyncSourceAsync(SourceConfig source, PluginConfiguration config, ManifestData manifest, CancellationToken ct)
        {
            var maxItems = source.MaxItems > 0 ? source.MaxItems : 20;

            if (IsFeedSource(source))
            {
                var entries = (await _feed.LoadAsync(source, ct).ConfigureAwait(false)).Take(maxItems).ToList();
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var written = await _writer.WriteAsync(entry, source, config, ct).ConfigureAwait(false);
                    UpdateManifest(manifest, source.Name, entry.Id, written);
                }

                _logger.LogInformation("ContentBridge: feed source {Source} synced {Count} title(s)", source.Name, entries.Count);
                return;
            }

            var fetched = 0;
            var pageCount = 1;

            for (var page = 1; page <= pageCount && fetched < maxItems; page++)
            {
                ct.ThrowIfCancellationRequested();
                var result = await _client.GetPageAsync(source, page, ct).ConfigureAwait(false);
                pageCount = Math.Max(1, result.PageCount);

                foreach (var entry in result.Items)
                {
                    if (fetched >= maxItems)
                    {
                        break;
                    }

                    ct.ThrowIfCancellationRequested();
                    var written = await _writer.WriteAsync(entry, source, config, ct).ConfigureAwait(false);
                    if (written.Count > 0)
                    {
                        fetched++;
                    }

                    UpdateManifest(manifest, source.Name, entry.Id, written);
                }

                if (result.Items.Count == 0)
                {
                    break;
                }
            }

            _logger.LogInformation("ContentBridge: source {Source} synced {Count} title(s)", source.Name, fetched);
        }

        private async Task CleanupAsync(PluginConfiguration config, ManifestData manifest, CancellationToken ct)
        {
            foreach (var source in (config.Sources ?? new List<SourceConfig>())
                         .Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.ApiUrl)))
            {
                ct.ThrowIfCancellationRequested();
                HashSet<string> liveIds;
                try
                {
                    liveIds = IsFeedSource(source)
                        ? new HashSet<string>((await _feed.LoadAsync(source, ct).ConfigureAwait(false)).Select(e => e.Id), StringComparer.Ordinal)
                        : await _client.GetAllIdsAsync(source, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("ContentBridge: cleanup skipped for {Source}: {Error}", source.Name, ex.Message);
                    continue;
                }

                if (liveIds.Count == 0 || !manifest.Sources.TryGetValue(source.Name, out var entries))
                {
                    continue;
                }

                var staleIds = entries.Keys.Where(id => !liveIds.Contains(id)).ToList();
                foreach (var id in staleIds)
                {
                    _writer.Delete(entries[id], config.LibraryRoot);
                    entries.Remove(id);
                    _logger.LogInformation("ContentBridge: removed stale entry {Id} from {Source}", id, source.Name);
                }
            }
        }

        private static void UpdateManifest(ManifestData manifest, string sourceName, string vodId, IReadOnlyList<string> paths)
        {
            if (paths.Count == 0 || string.IsNullOrEmpty(vodId))
            {
                return;
            }

            if (!manifest.Sources.TryGetValue(sourceName, out var entries))
            {
                entries = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                manifest.Sources[sourceName] = entries;
            }

            entries[vodId] = paths.ToList();
        }

        private static ManifestData LoadManifest(string libraryRoot)
        {
            try
            {
                var path = Path.Combine(libraryRoot, ManifestFileName);
                if (File.Exists(path))
                {
                    return JsonSerializer.Deserialize<ManifestData>(File.ReadAllText(path)) ?? new ManifestData();
                }
            }
            catch
            {
                // fall through to a fresh manifest
            }

            return new ManifestData();
        }

        private static void SaveManifest(string libraryRoot, ManifestData manifest)
        {
            try
            {
                var path = Path.Combine(libraryRoot, ManifestFileName);
                File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // manifest loss is non-fatal
            }
        }
    }

    /// <summary>
    /// On-disk manifest mapping source -> vodId -> generated relative paths.
    /// </summary>
    public sealed class ManifestData
    {
        public Dictionary<string, Dictionary<string, List<string>>> Sources { get; set; }
            = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
    }
}
