// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentBridge.Compat;
using MediaBrowser.Controller;

namespace Jellyfin.Plugin.ContentBridge.Services
{
    /// <summary>
    /// Local stream proxy: fetches play urls with the source's User-Agent / Referer headers and
    /// relays the response. HLS playlists are rewritten so their segments go through the proxy too.
    /// Host response handling is expressed through <see cref="ProxyExchange"/> so this class
    /// compiles and runs on both Jellyfin and Emby.
    /// </summary>
    public sealed class StreamProxyService
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        private readonly IServerApplicationHost _appHost;
        private readonly ILogSink _log;

        public StreamProxyService(IServerApplicationHost appHost, ILogSink log)
        {
            _appHost = appHost;
            _log = log ?? NullLogSink.Instance;
        }

        /// <summary>
        /// Get (or lazily create) the proxy access key embedded in generated links.
        /// </summary>
        public static string EnsureProxyKey()
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(config.ProxyKey))
            {
                config.ProxyKey = Guid.NewGuid().ToString("N");
                Plugin.Instance.SaveConfiguration();
            }

            return config.ProxyKey;
        }

        /// <summary>
        /// Resolve the public base url used in generated links.
        /// </summary>
        public string GetBaseUrl()
        {
            var config = Plugin.Instance?.Configuration;
            if (config != null && !string.IsNullOrWhiteSpace(config.ProxyBaseUrl))
            {
                return config.ProxyBaseUrl.TrimEnd('/');
            }

            try
            {
                var url = HostApiUrlCompat.GetLocalApiUrl(_appHost);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return url.TrimEnd('/');
                }
            }
            catch (Exception ex)
            {
                _log.Debug("Local api url fallback failed: " + ex.Message);
            }

            return "http://127.0.0.1:8096";
        }

        /// <summary>
        /// Wrap a target play url into a proxy link.
        /// </summary>
        public string BuildProxyUrl(string sourceName, string targetUrl)
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(targetUrl))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            return $"{GetBaseUrl()}/ContentBridge/Stream?k={EnsureProxyKey()}"
                 + $"&src={Uri.EscapeDataString(sourceName ?? string.Empty)}&u={encoded}";
        }

        /// <summary>
        /// Decode a target url from the u= parameter.
        /// </summary>
        public static bool TryDecodeTarget(string encoded, out string targetUrl)
        {
            targetUrl = string.Empty;
            if (string.IsNullOrWhiteSpace(encoded))
            {
                return false;
            }

            try
            {
                var s = encoded.Replace('-', '+').Replace('_', '/');
                switch (s.Length % 4)
                {
                    case 2: s += "=="; break;
                    case 3: s += "="; break;
                }

                targetUrl = Encoding.UTF8.GetString(Convert.FromBase64String(s));
                return Uri.IsWellFormedUriString(targetUrl, UriKind.Absolute);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Proxy the target url into the current HTTP exchange.
        /// </summary>
        public async Task ProxyAsync(ProxyExchange exchange, SourceConfig source, string targetUrl, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);

            if (source != null)
            {
                if (!string.IsNullOrWhiteSpace(source.UserAgent))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", source.UserAgent);
                }

                if (!string.IsNullOrWhiteSpace(source.Referer))
                {
                    request.Headers.TryAddWithoutValidation("Referer", source.Referer);
                }
            }

            if (!string.IsNullOrEmpty(exchange.RangeHeader))
            {
                request.Headers.TryAddWithoutValidation("Range", exchange.RangeHeader);
            }

            using var upstream = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            var contentType = upstream.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var isPlaylist = contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
                             || new Uri(targetUrl).AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);

            if (isPlaylist && upstream.IsSuccessStatusCode)
            {
                await WritePlaylistAsync(exchange, upstream, targetUrl, source?.Name ?? string.Empty, ct).ConfigureAwait(false);
                return;
            }

            exchange.SetStatusCode?.Invoke((int)upstream.StatusCode);
            if (!string.IsNullOrEmpty(contentType))
            {
                exchange.SetContentType?.Invoke(contentType);
            }

            CopyHeader(upstream, exchange, "Content-Length");
            CopyHeader(upstream, exchange, "Content-Range");
            CopyHeader(upstream, exchange, "Accept-Ranges");

            if (upstream.Content.Headers.ContentLength > 0 && exchange.SetContentLength != null)
            {
                exchange.SetContentLength(upstream.Content.Headers.ContentLength.Value);
            }

            await using var body = await upstream.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await body.CopyToAsync(exchange.Body, ct).ConfigureAwait(false);
        }

        private async Task WritePlaylistAsync(ProxyExchange exchange, HttpResponseMessage upstream, string targetUrl, string sourceName, CancellationToken ct)
        {
            var text = await upstream.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var baseUri = new Uri(targetUrl);
            var sb = new StringBuilder(text.Length + 256);

            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line))
                {
                    sb.Append(line).Append('\n');
                }
                else if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    sb.Append(RewriteTagUri(line, baseUri, sourceName)).Append('\n');
                }
                else
                {
                    var absolute = new Uri(baseUri, line.Trim()).AbsoluteUri;
                    sb.Append(BuildProxyUrl(sourceName, absolute)).Append('\n');
                }
            }

            exchange.SetStatusCode?.Invoke((int)upstream.StatusCode);
            exchange.SetContentType?.Invoke("application/vnd.apple.mpegurl");
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            exchange.SetContentLength?.Invoke(bytes.Length);
            await exchange.Body.WriteAsync(bytes, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Rewrite URI="..." attributes inside playlist tags (EXT-X-KEY / EXT-X-MAP / EXT-X-MEDIA).
        /// </summary>
        private string RewriteTagUri(string line, Uri baseUri, string sourceName)
        {
            const string marker = "URI=\"";
            var idx = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return line;
            }

            var start = idx + marker.Length;
            var end = line.IndexOf('"', start);
            if (end < 0)
            {
                return line;
            }

            var value = line.Substring(start, end - start);
            var absolute = new Uri(baseUri, value).AbsoluteUri;
            var proxied = BuildProxyUrl(sourceName, absolute);
            return line.Substring(0, start) + proxied + line.Substring(end);
        }

        private static void CopyHeader(HttpResponseMessage upstream, ProxyExchange exchange, string name)
        {
            if (exchange.SetHeader != null && upstream.Content.Headers.TryGetValues(name, out var values))
            {
                exchange.SetHeader(name, string.Join(", ", values));
            }
        }
    }
}
