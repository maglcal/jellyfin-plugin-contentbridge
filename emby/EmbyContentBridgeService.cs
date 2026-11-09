using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentBridge.Compat;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.ContentBridge.EmbyBinding
{
    /// <summary>
    /// Emby service binding for the ContentBridge management API. Thin shell over
    /// <see cref="RouteHostCompat"/> — the exact same host-neutral behavior the Jellyfin
    /// MVC controller uses. Routes live on the request DTOs (EmbyServiceContracts.cs).
    /// </summary>
    public class EmbyContentBridgeService : IService, IRequiresRequest
    {
        private readonly RouteHostCompat _routes;

        public EmbyContentBridgeService(ILogManager logManager, IServerApplicationHost appHost, ITaskManager taskManager)
        {
            ServiceRoot.EnsureAttached(logManager, appHost, taskManager);
            _routes = new RouteHostCompat(
                ServiceRoot.ContentApiClient,
                ServiceRoot.FeedClient,
                ServiceRoot.SyncService,
                taskManager);
        }

        /// <inheritdoc />
        public IRequest Request { get; set; }

        /// <summary>GET /ContentBridge/search.</summary>
        public async Task<object> Get(EmbySearchRequest request)
        {
            var entries = await _routes.SearchAsync(request.Source, request.Keyword, Request.CancellationToken).ConfigureAwait(false);
            if (entries == null)
            {
                throw new ResourceNotFoundException("数据源不存在");
            }

            return entries;
        }

        /// <summary>POST /ContentBridge/add.</summary>
        public async Task<object> Post(EmbyAddRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.VodId))
            {
                throw new InvalidOperationException("缺少条目 id");
            }

            var result = await _routes.AddTitleAsync(request.Source, request.VodId, Request.CancellationToken).ConfigureAwait(false);
            if (result == null)
            {
                throw new ResourceNotFoundException("数据源不存在");
            }

            return result;
        }

        /// <summary>POST /ContentBridge/sync — runs the sync and reports the per-source outcome.</summary>
        public async Task<object> Post(EmbySyncRequest request)
        {
            return await _routes.SyncNowAsync(Request.CancellationToken).ConfigureAwait(false);
        }

        /// <summary>GET /ContentBridge/schedule.</summary>
        public object Get(EmbyGetScheduleRequest request)
        {
            var info = _routes.GetSchedule();
            if (info == null)
            {
                throw new ResourceNotFoundException("找不到同步计划任务");
            }

            return info;
        }

        /// <summary>POST /ContentBridge/schedule.</summary>
        public object Post(EmbySetScheduleRequest request)
        {
            var info = _routes.SetSchedule(request?.IntervalHours);
            if (info == null)
            {
                throw new ResourceNotFoundException("找不到同步计划任务");
            }

            return info;
        }

        /// <summary>GET /ContentBridge/update-check.</summary>
        public async Task<object> Get(EmbyUpdateCheckRequest request)
        {
            return await _routes.CheckUpdateAsync(request.Primary, Request.CancellationToken).ConfigureAwait(false);
        }
    }
}
