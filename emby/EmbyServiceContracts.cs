using System.Collections.Generic;
using Jellyfin.Plugin.ContentBridge.Models;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Jellyfin.Plugin.ContentBridge.EmbyBinding
{
    // Emby service contracts (ServiceStack-style): request DTOs carry the [Route] and are
    // bound case-insensitively from query/JSON body. Wire shapes mirror the Jellyfin MVC
    // routes exactly (see Api/ContentBridgeController.cs), so configPage.html works on both.

    /// <summary>GET /ContentBridge/search.</summary>
    [Authenticated]
    [Route("/ContentBridge/search", "GET")]
    public class EmbySearchRequest : IReturn<List<VodEntry>>
    {
        /// <summary>Gets or sets the source name.</summary>
        public string Source { get; set; }

        /// <summary>Gets or sets the search keyword.</summary>
        public string Keyword { get; set; }
    }

    /// <summary>POST /ContentBridge/add.</summary>
    [Authenticated]
    [Route("/ContentBridge/add", "POST")]
    public class EmbyAddRequest : IReturn<AddResult>
    {
        /// <summary>Gets or sets the source name.</summary>
        public string Source { get; set; }

        /// <summary>Gets or sets the entry id inside the source.</summary>
        public string VodId { get; set; }
    }

    /// <summary>POST /ContentBridge/sync.</summary>
    [Authenticated]
    [Route("/ContentBridge/sync", "POST")]
    public class EmbySyncRequest : IReturn<SyncRunResult>
    {
    }

    /// <summary>GET /ContentBridge/schedule.</summary>
    [Authenticated]
    [Route("/ContentBridge/schedule", "GET")]
    public class EmbyGetScheduleRequest : IReturn<SyncScheduleInfo>
    {
    }

    /// <summary>POST /ContentBridge/schedule.</summary>
    [Authenticated]
    [Route("/ContentBridge/schedule", "POST")]
    public class EmbySetScheduleRequest : IReturn<SyncScheduleInfo>
    {
        /// <summary>Gets or sets the interval in hours. Null or 0 disables auto sync.</summary>
        public double? IntervalHours { get; set; }
    }

    /// <summary>GET /ContentBridge/update-check.</summary>
    [Authenticated]
    [Route("/ContentBridge/update-check", "GET")]
    public class EmbyUpdateCheckRequest : IReturn<Services.UpdateCheckResult>
    {
        /// <summary>Gets or sets the primary channel override.</summary>
        public string Primary { get; set; }
    }

    /// <summary>
    /// GET /ContentBridge/Stream — anonymous by design (media clients do not send auth
    /// headers); access is protected by the per-install proxy key embedded in the link.
    /// </summary>
    [Route("/ContentBridge/Stream", "GET")]
    public class EmbyStreamRequest : IReturnVoid
    {
        /// <summary>Gets or sets the proxy access key.</summary>
        public string K { get; set; }

        /// <summary>Gets or sets the source name.</summary>
        public string Src { get; set; }

        /// <summary>Gets or sets the base64url-encoded target url.</summary>
        public string U { get; set; }
    }
}
