using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentBridge.Models;
using Jellyfin.Plugin.ContentBridge.Services;
using Jellyfin.Plugin.ContentBridge.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ContentBridge.Api
{
    /// <summary>
    /// Endpoints backing the plugin config page: search, add-to-library, sync-now,
    /// and sync schedule (shared with the scheduled task).
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("ContentBridge")]
    public class ContentBridgeController : ControllerBase
    {
        private readonly ContentApiClient _client;
        private readonly FeedClient _feed;
        private readonly SyncService _syncService;
        private readonly ITaskManager _taskManager;

        public ContentBridgeController(ContentApiClient client, FeedClient feed, SyncService syncService, ITaskManager taskManager)
        {
            _client = client;
            _feed = feed;
            _syncService = syncService;
            _taskManager = taskManager;
        }

        /// <summary>
        /// Search a content source by keyword.
        /// </summary>
        [HttpGet("search")]
        public async Task<ActionResult<List<VodEntry>>> Search(
            [FromQuery] string source,
            [FromQuery] string keyword,
            CancellationToken cancellationToken)
        {
            var sourceConfig = FindSource(source);
            if (sourceConfig == null)
            {
                return NotFound("数据源不存在");
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
        [HttpPost("add")]
        public async Task<ActionResult<AddResult>> Add([FromBody] AddRequest request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.VodId))
            {
                return BadRequest("缺少条目 id");
            }

            var sourceConfig = FindSource(request.Source);
            if (sourceConfig == null)
            {
                return NotFound("数据源不存在");
            }

            try
            {
                var written = await _syncService.AddTitleAsync(sourceConfig, request.VodId, cancellationToken).ConfigureAwait(false);
                return new AddResult { Files = written.ToList() };
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Kick off a full sync right away (fire and forget).
        /// </summary>
        [HttpPost("sync")]
        public IActionResult SyncNow()
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
            return Accepted();
        }

        /// <summary>
        /// Read the auto-sync schedule (shared with the "同步外部内容源" scheduled task).
        /// </summary>
        [HttpGet("schedule")]
        public ActionResult<SyncScheduleInfo> GetSchedule()
        {
            var worker = FindSyncWorker();
            if (worker == null)
            {
                return NotFound("找不到同步计划任务");
            }

            var info = new SyncScheduleInfo();
            foreach (var trigger in worker.Triggers ?? Array.Empty<TaskTriggerInfo>())
            {
                if (TaskTriggerCompat.IsIntervalTrigger(trigger))
                {
                    info.Enabled = true;
                    info.IntervalHours = trigger.IntervalTicks.HasValue
                        ? TimeSpan.FromTicks(trigger.IntervalTicks.Value).TotalHours
                        : (double?)null;
                    break;
                }

                // Non-interval trigger configured manually in 计划任务; surface its type.
                info.Enabled = true;
                info.TriggerType = info.TriggerType ?? TaskTriggerCompat.GetTriggerTypeName(trigger);
            }

            return info;
        }

        /// <summary>
        /// Update the auto-sync schedule. Same effect as editing the task under
        /// 控制台 → 计划任务, kept in sync there. intervalHours of null or 0 means manual only.
        /// Returns the updated schedule so the config page can confirm the round trip.
        /// </summary>
        [HttpPost("schedule")]
        public ActionResult<SyncScheduleInfo> SetSchedule([FromBody] SyncScheduleRequest request)
        {
            var worker = FindSyncWorker();
            if (worker == null)
            {
                return NotFound("找不到同步计划任务");
            }

            if (request == null || !request.IntervalHours.HasValue || request.IntervalHours.Value <= 0)
            {
                worker.Triggers = TaskTriggerCompat.BuildManualTriggers();
            }
            else
            {
                worker.Triggers = TaskTriggerCompat.BuildIntervalTriggers(request.IntervalHours.Value);
            }

            return GetSchedule();
        }

        /// <summary>
        /// Check for plugin updates: the primary channel (self-hosted mirror, configured in the
        /// plugin settings) is tried first and falls back to the GitHub channel automatically.
        /// The optional "primary" query overrides the saved value so the check button works
        /// before saving.
        /// </summary>
        [HttpGet("update-check")]
        public async Task<ActionResult<UpdateCheckResult>> CheckUpdate(
            [FromQuery] string primary,
            CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            var primaryUrl = string.IsNullOrWhiteSpace(primary) ? config?.UpdatePrimaryUrl : primary;
            var current = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            var checker = new UpdateChecker(UpdateChecker.CreateDefaultFetcher());
            var result = await checker.CheckAsync(primaryUrl, current, cancellationToken).ConfigureAwait(false);
            return result;
        }

        private IScheduledTaskWorker FindSyncWorker()
        {
            return _taskManager.ScheduledTasks.FirstOrDefault(
                w => string.Equals(w.ScheduledTask?.Key, SyncContentTask.TaskKey, StringComparison.OrdinalIgnoreCase));
        }

        private SourceConfig FindSource(string name)
        {
            var config = Plugin.Instance?.Configuration;
            return config?.Sources?.FirstOrDefault(
                s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Request body for add-to-library.</summary>
    public class AddRequest
    {
        public string Source { get; set; }

        public string VodId { get; set; }
    }

    /// <summary>Response of add-to-library.</summary>
    public class AddResult
    {
        public List<string> Files { get; set; } = new List<string>();
    }

    /// <summary>Current auto-sync schedule of the sync task.</summary>
    public class SyncScheduleInfo
    {
        /// <summary>Gets or sets a value indicating whether any automatic trigger is configured.</summary>
        public bool Enabled { get; set; }

        /// <summary>Gets or sets the interval in hours for interval triggers (null when not an interval trigger).</summary>
        public double? IntervalHours { get; set; }

        /// <summary>Gets or sets the trigger type name when a non-interval trigger was configured manually.</summary>
        public string TriggerType { get; set; }
    }

    /// <summary>Request body for updating the auto-sync schedule.</summary>
    public class SyncScheduleRequest
    {
        /// <summary>Gets or sets the interval in hours. Null or 0 disables auto sync (manual only).</summary>
        public double? IntervalHours { get; set; }
    }
}
