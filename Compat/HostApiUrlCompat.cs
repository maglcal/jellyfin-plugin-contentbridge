// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using MediaBrowser.Controller;

namespace Jellyfin.Plugin.ContentBridge.Compat
{
    /// <summary>
    /// Reflection seam for <c>IServerApplicationHost.GetLocalApiUrl</c> (宿主 API 地址缝).
    /// The overload set differs per host — Jellyfin 10.10 has a 3-argument form while Emby
    /// has (string host) / (IPAddress) / (CancellationToken) forms — so the call is resolved
    /// by shape at runtime instead of compiled per host (反射优先于条件编译).
    /// </summary>
    public static class HostApiUrlCompat
    {
        /// <summary>
        /// Resolve the local API base url from a host application object.
        /// </summary>
        /// <param name="appHost">The host application host (any type exposing GetLocalApiUrl).</param>
        /// <returns>The base url, or null when unavailable.</returns>
        public static string GetLocalApiUrl(object appHost)
        {
            if (appHost == null)
            {
                return null;
            }

            foreach (var method in appHost.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Where(m => m.Name == "GetLocalApiUrl")
                         .OrderByDescending(m => m.GetParameters().Length))
            {
                var parameters = method.GetParameters();

                // Prefer overloads whose arguments can all be null (Jellyfin's 3-arg form,
                // Emby's string-host form); skip the task-based overload here.
                if (parameters.Any(p => p.ParameterType == typeof(CancellationToken)))
                {
                    continue;
                }

                if (parameters.Any(p => p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) == null))
                {
                    continue;
                }

                try
                {
                    if (method.Invoke(appHost, parameters.Select(_ => (object)null).ToArray()) is string url
                        && !string.IsNullOrWhiteSpace(url))
                    {
                        return url;
                    }
                }
                catch (Exception)
                {
                    // try the next overload
                }
            }

            // Task<string> GetLocalApiUrl(CancellationToken) — last resort.
            foreach (var method in appHost.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Where(m => m.Name == "GetLocalApiUrl" && m.ReturnType.Name.StartsWith("Task", StringComparison.Ordinal)))
            {
                try
                {
                    var task = method.Invoke(appHost, new object[] { CancellationToken.None }) as System.Threading.Tasks.Task<string>;
                    var url = task?.GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        return url;
                    }
                }
                catch (Exception)
                {
                    // give up
                }
            }

            return null;
        }
    }
}
