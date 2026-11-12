// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using System;
using Jellyfin.Plugin.ContentBridge.Compat;
using Jellyfin.Plugin.ContentBridge.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentBridge
{
    /// <summary>
    /// Registers plugin services in the Jellyfin DI container.
    /// (Jellyfin-only file — excluded from the Emby build.)
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<ILogSink>(sp => new JellyfinLogSink(sp.GetRequiredService<ILoggerFactory>()));
            serviceCollection.AddSingleton<ContentApiClient>();
            serviceCollection.AddSingleton<FeedClient>();
            serviceCollection.AddSingleton<StrmLibraryWriter>();
            serviceCollection.AddSingleton<StreamProxyService>();
            serviceCollection.AddSingleton<SyncService>();
            serviceCollection.AddSingleton<MediaBrowser.Model.Tasks.IScheduledTask, Tasks.SyncContentTask>();
        }

        /// <summary>
        /// Adapts Jellyfin's ILoggerFactory onto the host-neutral logging seam.
        /// </summary>
        private sealed class JellyfinLogSink : ILogSink
        {
            private readonly ILogger _logger;

            public JellyfinLogSink(ILoggerFactory factory)
            {
                _logger = factory.CreateLogger("ContentBridge");
            }

            public void Debug(string message)
            {
                _logger.LogDebug(message);
            }

            public void Info(string message)
            {
                _logger.LogInformation(message);
            }

            public void Warn(string message)
            {
                _logger.LogWarning(message);
            }

            public void Error(string message, Exception ex = null)
            {
                if (ex == null)
                {
                    _logger.LogError(message);
                }
                else
                {
                    _logger.LogError(ex, message);
                }
            }
        }
    }
}
