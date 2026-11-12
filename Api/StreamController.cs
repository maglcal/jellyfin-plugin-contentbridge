// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentBridge.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentBridge.Api
{
    /// <summary>
    /// Stream proxy endpoint. Anonymous by design (media clients and ffmpeg do not send
    /// Jellyfin auth headers); access is protected by the per-install proxy key embedded
    /// in every generated link.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("ContentBridge")]
    public class StreamController : ControllerBase
    {
        private readonly StreamProxyService _proxy;
        private readonly ILogger<StreamController> _logger;

        public StreamController(StreamProxyService proxy, ILogger<StreamController> logger)
        {
            _proxy = proxy;
            _logger = logger;
        }

        /// <summary>
        /// Relay a play url (with the source's User-Agent / Referer) to the client.
        /// </summary>
        [HttpGet("Stream")]
        public async Task Stream([FromQuery] string k, [FromQuery] string src, [FromQuery] string u, CancellationToken ct)
        {
            var expected = StreamProxyService.EnsureProxyKey();
            if (string.IsNullOrEmpty(k) || !string.Equals(k, expected, StringComparison.Ordinal))
            {
                Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!StreamProxyService.TryDecodeTarget(u, out var target))
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var source = (Plugin.Instance?.Configuration?.Sources ?? new System.Collections.Generic.List<SourceConfig>())
                .FirstOrDefault(s => string.Equals(s.Name, src, StringComparison.OrdinalIgnoreCase));

            try
            {
                await _proxy.ProxyAsync(HttpContextProxyExchange.FromHttpContext(HttpContext), source, target, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // client went away
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Stream proxy failed for {Target}: {Error}", target, ex.Message);
                if (!Response.HasStarted)
                {
                    Response.StatusCode = StatusCodes.Status502BadGateway;
                }
            }
        }
    }
}
