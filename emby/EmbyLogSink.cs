using System;
using Jellyfin.Plugin.ContentBridge.Compat;
using MediaBrowser.Model.Logging;

namespace Jellyfin.Plugin.ContentBridge.EmbyBinding
{
    /// <summary>
    /// Adapts Emby's <see cref="ILogger"/> onto the host-neutral logging seam.
    /// </summary>
    public sealed class EmbyLogSink : ILogSink
    {
        private readonly ILogger _logger;

        public EmbyLogSink(ILogger logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public void Debug(string message)
        {
            _logger?.Debug(message, Array.Empty<object>());
        }

        /// <inheritdoc />
        public void Info(string message)
        {
            _logger?.Info(message, Array.Empty<object>());
        }

        /// <inheritdoc />
        public void Warn(string message)
        {
            _logger?.Warn(message, Array.Empty<object>());
        }

        /// <inheritdoc />
        public void Error(string message, Exception ex = null)
        {
            if (_logger == null)
            {
                return;
            }

            if (ex == null)
            {
                _logger.Error(message, Array.Empty<object>());
            }
            else
            {
                _logger.ErrorException(message, ex, Array.Empty<object>());
            }
        }
    }
}
