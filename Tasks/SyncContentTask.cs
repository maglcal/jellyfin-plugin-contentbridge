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
            var trigger = new TaskTriggerInfo
            {
                IntervalTicks = TimeSpan.FromHours(6).Ticks
            };

            // Jellyfin 10.11 changed TaskTriggerInfo.Type from a string ("IntervalTrigger")
            // to the TaskTriggerInfoType enum. Assign it reflectively so that this single
            // build runs correctly on both 10.10.x and 10.11.x servers.
            var typeProperty = typeof(TaskTriggerInfo).GetProperty("Type");
            if (typeProperty?.PropertyType == typeof(string))
            {
                typeProperty.SetValue(trigger, "IntervalTrigger");
            }
            else if (typeProperty != null)
            {
                typeProperty.SetValue(trigger, Enum.Parse(typeProperty.PropertyType, "IntervalTrigger"));
            }

            return new[] { trigger };
        }
    }
}
