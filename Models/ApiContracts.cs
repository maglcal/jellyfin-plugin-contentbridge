using System.Collections.Generic;

namespace Jellyfin.Plugin.ContentBridge.Models
{
    /// <summary>
    /// Shared API contracts (host-neutral DTOs). Wire shape is unchanged from the original
    /// Jellyfin-only controller; they live here so every host binding layer can use them.
    /// </summary>
    public class AddRequest
    {
        /// <summary>Gets or sets the source name.</summary>
        public string Source { get; set; }

        /// <summary>Gets or sets the entry id inside the source.</summary>
        public string VodId { get; set; }
    }

    /// <summary>Response of add-to-library.</summary>
    public class AddResult
    {
        /// <summary>Gets or sets the files written.</summary>
        public List<string> Files { get; set; } = new List<string>();
    }

    /// <summary>Current auto-sync schedule of the sync task.</summary>
    public class SyncScheduleInfo
    {
        /// <summary>Gets or sets a value indicating whether any automatic trigger is configured.</summary>
        public bool Enabled { get; set; }

        /// <summary>Gets or sets the interval in hours for interval triggers (null when not an interval trigger).</summary>
        public double? IntervalHours { get; set; }

        /// <summary>Gets or sets the trigger type name when a non-interval trigger was configured manually.</summary>
        public string TriggerType { get; set; }
    }

    /// <summary>Request body for updating the auto-sync schedule.</summary>
    public class SyncScheduleRequest
    {
        /// <summary>Gets or sets the interval in hours. Null or 0 disables auto sync (manual only).</summary>
        public double? IntervalHours { get; set; }
    }
}
