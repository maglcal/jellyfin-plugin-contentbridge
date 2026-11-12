// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.ContentBridge.Tasks
{
    /// <summary>
    /// Reflection helpers around <see cref="TaskTriggerInfo" />.
    /// Jellyfin 10.11 changed the <c>Type</c> property from a string ("IntervalTrigger")
    /// to the <c>TaskTriggerInfoType</c> enum, so every read/write of that property goes
    /// through reflection here. A single build then runs correctly on both 10.10.x and
    /// 10.11.x servers.
    /// </summary>
    public static class TaskTriggerCompat
    {
        private const string IntervalTriggerName = "IntervalTrigger";

        /// <summary>
        /// Creates an interval trigger with the given interval, setting the version-specific
        /// <c>Type</c> property reflectively.
        /// </summary>
        /// <param name="intervalTicks">Interval between runs, in ticks.</param>
        /// <returns>The configured trigger.</returns>
        public static TaskTriggerInfo CreateIntervalTrigger(long intervalTicks)
        {
            var trigger = new TaskTriggerInfo
            {
                IntervalTicks = intervalTicks
            };

            SetTriggerType(trigger, IntervalTriggerName);
            return trigger;
        }

        /// <summary>
        /// Creates a set of interval triggers (used as the default trigger list of the sync task).
        /// </summary>
        /// <param name="intervalTicks">Interval between runs, in ticks.</param>
        /// <returns>A single-element trigger list.</returns>
        public static IReadOnlyList<TaskTriggerInfo> CreateDefaultTriggers(long intervalTicks)
        {
            return new[] { CreateIntervalTrigger(intervalTicks) };
        }

        /// <summary>
        /// Reads the trigger type as its canonical name (e.g. "IntervalTrigger"),
        /// regardless of whether the server exposes it as a string or an enum.
        /// </summary>
        /// <param name="trigger">The trigger to read.</param>
        /// <returns>The type name, or null when unavailable.</returns>
        public static string GetTriggerTypeName(TaskTriggerInfo trigger)
        {
            if (trigger == null)
            {
                return null;
            }

            var typeProperty = typeof(TaskTriggerInfo).GetProperty("Type");
            return typeProperty?.GetValue(trigger)?.ToString();
        }

        /// <summary>
        /// Gets a value indicating whether the trigger is an interval trigger.
        /// </summary>
        /// <param name="trigger">The trigger to check.</param>
        /// <returns>True for interval triggers.</returns>
        public static bool IsIntervalTrigger(TaskTriggerInfo trigger)
        {
            return string.Equals(GetTriggerTypeName(trigger), IntervalTriggerName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Builds an interval trigger list for the sync task from an interval in hours.
        /// </summary>
        /// <param name="intervalHours">Interval in hours (must be positive).</param>
        /// <returns>A single-element trigger list.</returns>
        public static IReadOnlyList<TaskTriggerInfo> BuildIntervalTriggers(double intervalHours)
        {
            return new[] { CreateIntervalTrigger((long)(TimeSpan.FromHours(intervalHours).Ticks)) };
        }

        /// <summary>
        /// Builds an empty trigger list, i.e. "manual only".
        /// </summary>
        /// <returns>An empty trigger list.</returns>
        public static IReadOnlyList<TaskTriggerInfo> BuildManualTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }

        private static void SetTriggerType(TaskTriggerInfo trigger, string typeName)
        {
            var typeProperty = typeof(TaskTriggerInfo).GetProperty("Type");
            if (typeProperty == null)
            {
                return;
            }

            if (typeProperty.PropertyType == typeof(string))
            {
                typeProperty.SetValue(trigger, typeName);
            }
            else
            {
                typeProperty.SetValue(trigger, Enum.Parse(typeProperty.PropertyType, typeName));
            }
        }
    }
}
