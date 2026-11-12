// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ContentBridge.Models;
using Jellyfin.Plugin.ContentBridge.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.ContentBridge.Compat
{
    /// <summary>
    /// Scheduled-task seam (任务注册). Task lookup and schedule read/write converge here.
    ///
    /// Trigger-type differences (string on Jellyfin 10.10.x, enum on 10.11.x, string on Emby)
    /// are digested by the TaskTriggerCompat reflection layer; this class only exposes
    /// host-neutral schedule semantics on top of it. The trigger-list overloads are pure and
    /// unit-testable; the worker overloads are thin host bindings around them.
    /// </summary>
    public static class TaskHostCompat
    {
        /// <summary>
        /// Find a scheduled task worker by task key (host task manager lookup).
        /// </summary>
        /// <param name="taskManager">The host task manager.</param>
        /// <param name="taskKey">Task key (see SyncContentTask.TaskKey).</param>
        /// <returns>The worker, or null when not found.</returns>
        public static IScheduledTaskWorker FindTask(ITaskManager taskManager, string taskKey)
        {
            return taskManager?.ScheduledTasks?.FirstOrDefault(
                w => string.Equals(w.ScheduledTask?.Key, taskKey, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Read the schedule state from a trigger list (pure, host-free).
        /// </summary>
        /// <param name="triggers">Configured triggers.</param>
        /// <returns>The schedule state.</returns>
        public static SyncScheduleInfo ReadSchedule(IEnumerable<TaskTriggerInfo> triggers)
        {
            var info = new SyncScheduleInfo();
            foreach (var trigger in triggers ?? Enumerable.Empty<TaskTriggerInfo>())
            {
                if (TaskTriggerCompat.IsIntervalTrigger(trigger))
                {
                    info.Enabled = true;
                    info.IntervalHours = trigger.IntervalTicks.HasValue
                        ? TimeSpan.FromTicks(trigger.IntervalTicks.Value).TotalHours
                        : (double?)null;
                    break;
                }

                // Non-interval trigger configured manually on the host; surface its type.
                info.Enabled = true;
                info.TriggerType = info.TriggerType ?? TaskTriggerCompat.GetTriggerTypeName(trigger);
            }

            return info;
        }

        /// <summary>
        /// Build the trigger list for a schedule value (pure, host-free).
        /// Null or 0 means manual only. Returns an array because Emby workers expose
        /// <c>Triggers</c> as <c>TaskTriggerInfo[]</c> while Jellyfin exposes
        /// <c>IReadOnlyList&lt;TaskTriggerInfo&gt;</c> (an array satisfies both).
        /// </summary>
        /// <param name="intervalHours">Interval in hours, or null/0 for manual.</param>
        /// <returns>The trigger list to assign to the task.</returns>
        public static TaskTriggerInfo[] BuildTriggers(double? intervalHours)
        {
            return intervalHours.HasValue && intervalHours.Value > 0
                ? TaskTriggerCompat.BuildIntervalTriggers(intervalHours.Value).ToArray()
                : TaskTriggerCompat.BuildManualTriggers().ToArray();
        }

        /// <summary>
        /// Read the schedule of a task worker (host binding over <see cref="ReadSchedule"/>).
        /// </summary>
        /// <param name="worker">The task worker.</param>
        /// <returns>The schedule state, or null when the worker is missing.</returns>
        public static SyncScheduleInfo ReadSchedule(IScheduledTaskWorker worker)
        {
            return worker == null ? null : ReadSchedule(worker.Triggers ?? Array.Empty<TaskTriggerInfo>());
        }

        /// <summary>
        /// Apply a schedule value to a task worker and return the resulting state
        /// (host binding over <see cref="BuildTriggers"/>). Same effect as editing the task
        /// under 控制台 → 计划任务; kept in sync there.
        /// </summary>
        /// <param name="worker">The task worker.</param>
        /// <param name="intervalHours">Interval in hours, or null/0 for manual.</param>
        /// <returns>The resulting schedule state, or null when the worker is missing.</returns>
        public static SyncScheduleInfo ApplySchedule(IScheduledTaskWorker worker, double? intervalHours)
        {
            if (worker == null)
            {
                return null;
            }

            worker.Triggers = BuildTriggers(intervalHours);

            // Emby workers need ReloadTriggerEvents() to reschedule after a Triggers
            // change; Jellyfin has no such method — probe opportunistically.
            var reload = worker.GetType().GetMethod("ReloadTriggerEvents", Type.EmptyTypes);
            reload?.Invoke(worker, null);

            return ReadSchedule(worker);
        }
    }
}
