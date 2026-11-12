// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentBridge.Compat;
using Jellyfin.Plugin.ContentBridge.Models;
using Jellyfin.Plugin.ContentBridge.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ContentBridge.Api
{
    /// <summary>
    /// Jellyfin MVC binding for the plugin endpoints. Thin by design: all endpoint behavior
    /// lives in the host-neutral <see cref="RouteHostCompat"/> (Compat/), so an Emby binding
    /// can reuse it unchanged. Routes and DTO wire shapes are unchanged.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("ContentBridge")]
    public class ContentBridgeController : ControllerBase
    {
        private readonly RouteHostCompat _routes;

        public ContentBridgeController(ContentApiClient client, FeedClient feed, SyncService syncService, ITaskManager taskManager)
        {
            _routes = new RouteHostCompat(client, feed, syncService, taskManager);
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
            var entries = await _routes.SearchAsync(source, keyword, cancellationToken).ConfigureAwait(false);
            if (entries == null)
            {
                return NotFound("数据源不存在");
            }

            return entries;
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

            try
            {
                var result = await _routes.AddTitleAsync(request.Source, request.VodId, cancellationToken).ConfigureAwait(false);
                if (result == null)
                {
                    return NotFound("数据源不存在");
                }

                return result;
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Run a full sync right away and report the per-source outcome (errors included).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Per-source sync outcome.</returns>
        [HttpPost("sync")]
        public async Task<ActionResult<SyncRunResult>> SyncNow(CancellationToken cancellationToken)
        {
            return await _routes.SyncNowAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Read the auto-sync schedule (shared with the "同步外部内容源" scheduled task).
        /// </summary>
        [HttpGet("schedule")]
        public ActionResult<SyncScheduleInfo> GetSchedule()
        {
            var info = _routes.GetSchedule();
            if (info == null)
            {
                return NotFound("找不到同步计划任务");
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
            var info = _routes.SetSchedule(request?.IntervalHours);
            if (info == null)
            {
                return NotFound("找不到同步计划任务");
            }

            return info;
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
            var result = await _routes.CheckUpdateAsync(primary, cancellationToken).ConfigureAwait(false);
            return result;
        }
    }
}
