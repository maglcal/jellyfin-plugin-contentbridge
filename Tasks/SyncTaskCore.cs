// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.ContentBridge.Tasks
{
    /// <summary>
    /// Host-neutral core of the sync scheduled task (任务核心). The per-host task classes
    /// (Jellyfin's <see cref="SyncContentTask"/> and the Emby binding task) are thin shells
    /// over these members, because <c>IScheduledTask.Execute</c> has a different signature
    /// on each host (Jellyfin: ExecuteAsync(progress, ct); Emby: Execute(ct, progress)).
    /// </summary>
    public static class SyncTaskCore
    {
        /// <summary>
        /// The task key, also used to look the task up via <c>ITaskManager</c>.
        /// </summary>
        public const string TaskKey = "ContentBridgeSync";

        /// <summary>Gets the task display name.</summary>
        public static string Name => "同步外部内容源";

        /// <summary>Gets the task description.</summary>
        public static string Description => "拉取已配置的内容接口，把条目以 .strm 虚拟条目的形式写入媒体库目录";

        /// <summary>Gets the task category.</summary>
        public static string Category => "Library";

        /// <summary>
        /// Build the default triggers (every 6 hours).
        /// </summary>
        /// <returns>The default trigger list.</returns>
        public static IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Type (string on 10.10.x, enum on 10.11.x) is assigned via TaskTriggerCompat
            // so that a single build runs correctly on all server versions.
            return TaskTriggerCompat.CreateDefaultTriggers(TimeSpan.FromHours(6).Ticks);
        }
    }
}
