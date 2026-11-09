using System;
using System.IO;

namespace Jellyfin.Plugin.ContentBridge.Compat
{
    /// <summary>
    /// Host-neutral HTTP exchange for the stream proxy (代理交换缝). The shared proxy logic
    /// reads the request range and writes status/headers/body through these members; each
    /// host binder maps them onto its own response object (Jellyfin: HttpContext,
    /// Emby: MediaBrowser.Model.Services.IResponse).
    /// </summary>
    public sealed class ProxyExchange
    {
        /// <summary>Gets or sets the raw Range request header value (null when absent).</summary>
        public string RangeHeader { get; set; }

        /// <summary>Gets or sets the response body stream.</summary>
        public Stream Body { get; set; }

        /// <summary>Gets or sets the status-code writer.</summary>
        public Action<int> SetStatusCode { get; set; }

        /// <summary>Gets or sets the content-type writer.</summary>
        public Action<string> SetContentType { get; set; }

        /// <summary>Gets or sets the response-header writer (name, value).</summary>
        public Action<string, string> SetHeader { get; set; }

        /// <summary>Gets or sets the content-length writer.</summary>
        public Action<long> SetContentLength { get; set; }
    }
}
