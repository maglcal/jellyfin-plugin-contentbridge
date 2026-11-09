using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentBridge.Compat;
using Jellyfin.Plugin.ContentBridge.Services;
using MediaBrowser.Controller;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.ContentBridge.EmbyBinding
{
    /// <summary>
    /// Emby stream proxy endpoint (mirror of the Jellyfin StreamController). Anonymous by
    /// design — media clients and ffmpeg do not send auth headers; access is protected by
    /// the per-install proxy key embedded in every generated link. The response is written
    /// directly through <see cref="IResponse"/> so upstream status codes (206 partial
    /// content) pass through unchanged.
    /// </summary>
    public class EmbyStreamService : IService, IRequiresRequest
    {
        public EmbyStreamService(ILogManager logManager, IServerApplicationHost appHost, ITaskManager taskManager)
        {
            ServiceRoot.EnsureAttached(logManager, appHost, taskManager);
        }

        /// <inheritdoc />
        public IRequest Request { get; set; }

        /// <summary>GET /ContentBridge/Stream.</summary>
        public async Task<object> Get(EmbyStreamRequest request)
        {
            var response = Request.Response;

            var expected = StreamProxyService.EnsureProxyKey();
            if (string.IsNullOrEmpty(request.K) || !string.Equals(request.K, expected, StringComparison.Ordinal))
            {
                response.StatusCode = 403;
                return null;
            }

            if (!StreamProxyService.TryDecodeTarget(request.U, out var target))
            {
                response.StatusCode = 400;
                return null;
            }

            var source = (Plugin.Instance?.Configuration?.Sources ?? new System.Collections.Generic.List<SourceConfig>())
                .FirstOrDefault(s => string.Equals(s.Name, request.Src, StringComparison.OrdinalIgnoreCase));

            var body = response.OutputWriter.AsStream(leaveOpen: true);
            var exchange = new ProxyExchange
            {
                RangeHeader = Request.Headers["Range"],
                Body = body,
                SetStatusCode = code => response.StatusCode = code,
                SetContentType = value => response.ContentType = value,
                SetHeader = (name, value) => response.AddHeader(name, value),
                SetContentLength = length => response.SetContentLength(length),
            };

            try
            {
                await ServiceRoot.StreamProxyService.ProxyAsync(exchange, source, target, Request.CancellationToken).ConfigureAwait(false);
                await body.FlushAsync(Request.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // client went away
            }
            catch (Exception)
            {
                if (!response.SentHeaders)
                {
                    response.StatusCode = 502;
                }
            }

            return null;
        }
    }
}
