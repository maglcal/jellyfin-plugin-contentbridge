// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using Jellyfin.Plugin.ContentBridge.Compat;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.ContentBridge.Api
{
    /// <summary>
    /// Jellyfin-side binder for the stream proxy: maps an ASP.NET Core
    /// <see cref="HttpContext"/> onto the host-neutral <see cref="ProxyExchange"/>.
    /// (Jellyfin-only file — excluded from the Emby build.)
    /// </summary>
    public static class HttpContextProxyExchange
    {
        /// <summary>
        /// Build a <see cref="ProxyExchange"/> that reads/writes the given HTTP context.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>The exchange.</returns>
        public static ProxyExchange FromHttpContext(HttpContext context)
        {
            return new ProxyExchange
            {
                RangeHeader = context.Request.Headers["Range"].ToString(),
                Body = context.Response.Body,
                SetStatusCode = code => context.Response.StatusCode = code,
                SetContentType = value => context.Response.ContentType = value,
                SetHeader = (name, value) => context.Response.Headers[name] = value,
                SetContentLength = length => context.Response.ContentLength = length,
            };
        }
    }
}
