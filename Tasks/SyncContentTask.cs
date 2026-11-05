using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentBridge.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.ContentBridge.Tasks
{
    /// <summary>
    /// Scheduled task that syncs all enabled content sources into the library.
    /// </summary>
    public class SyncContentTask : IScheduledTask
    {
        private readonly SyncService _syncService;

        public SyncContentTask(SyncService syncService)
        {
            _syncService = syncService;
        }

        /// <inheritdoc />
        public string Name => "同步外部内容源";

        /// <inheritdoc />
        public string Key => "ContentBridgeSync";

        /// <inheritdoc />
        public string Description => "拉取已配置的内容接口，把条目以 .strm 虚拟条目的形式写入媒体库目录";

        /// <inheritdoc />
        public string Category => "Library";

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            await _syncService.RunAsync(progress, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromHours(6).Ticks
                }
            };
        }
    }
}
