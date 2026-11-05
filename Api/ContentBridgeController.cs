using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentBridge.Models;
using Jellyfin.Plugin.ContentBridge.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ContentBridge.Api
{
    /// <summary>
    /// Endpoints backing the plugin config page: search, add-to-library, sync-now.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("ContentBridge")]
    public class ContentBridgeController : ControllerBase
    {
        private readonly ContentApiClient _client;
        private readonly FeedClient _feed;
        private readonly SyncService _syncService;

        public ContentBridgeController(ContentApiClient client, FeedClient feed, SyncService syncService)
        {
            _client = client;
            _feed = feed;
            _syncService = syncService;
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
                return entries
                    .Where(e => (e.Name ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                                || (e.Content ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                                || (e.Remarks ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase))
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
}
