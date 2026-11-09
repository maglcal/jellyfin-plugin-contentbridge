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
    /// (Jellyfin shell over <see cref="SyncTaskCore"/> — the Emby build uses its own thin
    /// shell because <c>IScheduledTask.Execute</c> has a different signature there.)
    /// </summary>
    public class SyncContentTask : IScheduledTask
    {
        /// <summary>
        /// The task key, also used to look the task up via <c>ITaskManager</c>.
        /// </summary>
        public const string TaskKey = SyncTaskCore.TaskKey;

        private readonly SyncService _syncService;

        public SyncContentTask(SyncService syncService)
        {
            _syncService = syncService;
        }

        /// <inheritdoc />
        public string Name => SyncTaskCore.Name;

        /// <inheritdoc />
        public string Key => TaskKey;

        /// <inheritdoc />
        public string Description => SyncTaskCore.Description;

        /// <inheritdoc />
        public string Category => SyncTaskCore.Category;

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var result = await _syncService.RunAsync(progress, cancellationToken).ConfigureAwait(false);
            if (result.Failed > 0)
            {
                // Surface per-source failures in 计划任务 instead of reporting a green run.
                throw new InvalidOperationException(
                    $"{result.Failed} 个数据源同步失败：{string.Join("；", result.Errors)}");
            }
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return SyncTaskCore.GetDefaultTriggers();
        }
    }
}
