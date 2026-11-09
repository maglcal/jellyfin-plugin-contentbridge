using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentBridge.EmbyBinding;
using Jellyfin.Plugin.ContentBridge.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.ContentBridge.EmbyBinding
{
    /// <summary>
    /// Emby shell for the sync scheduled task. Emby discovers <see cref="IScheduledTask"/>
    /// implementations in plugin assemblies automatically and builds them through its own
    /// container, so the constructor only takes host-core types. The execute signature is
    /// Emby's <c>Execute(ct, progress)</c> — Jellyfin's shell uses <c>ExecuteAsync</c>.
    /// </summary>
    public class EmbyScheduledTask : IScheduledTask
    {
        public EmbyScheduledTask(ILogManager logManager, IServerApplicationHost appHost, ITaskManager taskManager)
        {
            // First binder to run (task discovery happens at startup) wires the shared root.
            ServiceRoot.EnsureAttached(logManager, appHost, taskManager);
        }

        /// <inheritdoc />
        public string Name => SyncTaskCore.Name;

        /// <inheritdoc />
        public string Key => SyncTaskCore.TaskKey;

        /// <inheritdoc />
        public string Description => SyncTaskCore.Description;

        /// <inheritdoc />
        public string Category => SyncTaskCore.Category;

        /// <inheritdoc />
        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var result = await ServiceRoot.SyncService.RunAsync(progress, cancellationToken).ConfigureAwait(false);
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
