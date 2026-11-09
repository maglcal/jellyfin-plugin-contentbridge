using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentBridge.Models;
using Jellyfin.Plugin.ContentBridge.Services;
using Jellyfin.Plugin.ContentBridge.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.ContentBridge.Compat
{
    /// <summary>
    /// Routing-host seam (路由宿主): the host-neutral behavior behind every HTTP endpoint.
    ///
    /// Each host only provides a thin binding layer on top — the Jellyfin MVC controllers
    /// under Api/ call into this class; an Emby binding (whatever its route mechanism turns
    /// out to be, see docs/EMBY-COMPAT.md M2) reuses the exact same methods. DTO shapes are
    /// shared (Models/ApiContracts.cs), so the wire contract is identical on both hosts.
    /// </summary>
    public class RouteHostCompat
    {
        private readonly ContentApiClient _client;
        private readonly FeedClient _feed;
        private readonly SyncService _syncService;
        private readonly ITaskManager _taskManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="RouteHostCompat"/> class.
        /// </summary>
        /// <param name="client">MaccCMS-style content API client.</param>
        /// <param name="feed">Feed client.</param>
        /// <param name="syncService">Sync orchestration service.</param>
        /// <param name="taskManager">Host task manager (for the schedule endpoints).</param>
        public RouteHostCompat(ContentApiClient client, FeedClient feed, SyncService syncService, ITaskManager taskManager)
        {
            _client = client;
            _feed = feed;
            _syncService = syncService;
            _taskManager = taskManager;
        }

        /// <summary>
        /// Search a content source by keyword.
        /// </summary>
        /// <param name="source">Source name.</param>
        /// <param name="keyword">Search keyword.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Matched entries, or null when the source does not exist.</returns>
        public async Task<List<VodEntry>> SearchAsync(string source, string keyword, CancellationToken cancellationToken)
        {
            var sourceConfig = FindSource(source);
            if (sourceConfig == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(keyword))
            {
                return new List<VodEntry>();
            }

            if (SyncService.IsFeedSource(sourceConfig))
            {
                var entries = await _feed.LoadAsync(sourceConfig, cancellationToken).ConfigureAwait(false);
                return entries.Items
                    .Where(e => !e.Deleted
                                && ((e.Name ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                                || (e.Content ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                                || (e.Remarks ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    .Take(30)
                    .ToList();
            }

            var result = await _client.SearchAsync(sourceConfig, keyword, cancellationToken).ConfigureAwait(false);
            return result.Items;
        }

        /// <summary>
        /// Pull one title from a source and write it into the library immediately.
        /// </summary>
        /// <param name="source">Source name.</param>
        /// <param name="vodId">Entry id.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The add result, or null when the source does not exist.</returns>
        public async Task<AddResult> AddTitleAsync(string source, string vodId, CancellationToken cancellationToken)
        {
            var sourceConfig = FindSource(source);
            if (sourceConfig == null)
            {
                return null;
            }

            var written = await _syncService.AddTitleAsync(sourceConfig, vodId, cancellationToken).ConfigureAwait(false);
            return new AddResult { Files = written.ToList() };
        }

        /// <summary>
        /// Kick off a full sync right away (fire and forget).
        /// </summary>
        public void SyncNow()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _syncService.RunAsync(null, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // logged inside RunAsync; never let a background sync crash the host
                }
            });
        }

        /// <summary>
        /// Read the auto-sync schedule of the sync task.
        /// </summary>
        /// <returns>The schedule, or null when the sync task is not registered (host registration problem).</returns>
        public SyncScheduleInfo GetSchedule()
        {
            var worker = TaskHostCompat.FindTask(_taskManager, SyncContentTask.TaskKey);
            return TaskHostCompat.ReadSchedule(worker);
        }

        /// <summary>
        /// Update the auto-sync schedule. Returns the updated state so callers can confirm
        /// the round trip, or null when the sync task is not registered.
        /// </summary>
        /// <param name="intervalHours">Interval in hours. Null or 0 means manual only.</param>
        /// <returns>The updated schedule state.</returns>
        public SyncScheduleInfo SetSchedule(double? intervalHours)
        {
            var worker = TaskHostCompat.FindTask(_taskManager, SyncContentTask.TaskKey);
            return TaskHostCompat.ApplySchedule(worker, intervalHours);
        }

        /// <summary>
        /// Check for plugin updates (primary channel first, GitHub fallback on failure).
        /// </summary>
        /// <param name="primaryUrl">Primary channel override (falls back to saved config).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The update check result.</returns>
        public async Task<UpdateCheckResult> CheckUpdateAsync(string primaryUrl, CancellationToken cancellationToken)
        {
            var configured = string.IsNullOrWhiteSpace(primaryUrl) ? ConfigHostCompat.Current.UpdatePrimaryUrl : primaryUrl;
            var checker = new UpdateChecker(UpdateChecker.CreateDefaultFetcher());
            return await checker.CheckAsync(configured, ConfigHostCompat.VersionString, cancellationToken).ConfigureAwait(false);
        }

        private SourceConfig FindSource(string name)
        {
            var config = ConfigHostCompat.Current;
            return config.Sources?.FirstOrDefault(
                s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
