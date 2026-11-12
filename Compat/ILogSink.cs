// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace Jellyfin.Plugin.ContentBridge.Compat
{
    /// <summary>
    /// Host-neutral logging seam (日志缝). Shared services log through this interface so the
    /// core stays free of Microsoft.Extensions.Logging — that assembly is not part of the
    /// base .NET shared framework and Emby does not ship it, so referencing it directly
    /// would break plugin loading on Emby. Each host binding adapts its own logger
    /// (Jellyfin: ILoggerFactory; Emby: MediaBrowser.Model.Logging.ILogManager).
    /// </summary>
    public interface ILogSink
    {
        /// <summary>Log a debug message.</summary>
        /// <param name="message">The message.</param>
        void Debug(string message);

        /// <summary>Log an informational message.</summary>
        /// <param name="message">The message.</param>
        void Info(string message);

        /// <summary>Log a warning.</summary>
        /// <param name="message">The message.</param>
        void Warn(string message);

        /// <summary>Log an error, optionally with an exception.</summary>
        /// <param name="message">The message.</param>
        /// <param name="ex">The exception, or null.</param>
        void Error(string message, Exception ex = null);
    }

    /// <summary>
    /// No-op sink used when no host logger is available (tests, early bootstrap).
    /// </summary>
    public sealed class NullLogSink : ILogSink
    {
        /// <summary>Gets the shared instance.</summary>
        public static readonly NullLogSink Instance = new NullLogSink();

        /// <inheritdoc />
        public void Debug(string message)
        {
        }

        /// <inheritdoc />
        public void Info(string message)
        {
        }

        /// <inheritdoc />
        public void Warn(string message)
        {
        }

        /// <inheritdoc />
        public void Error(string message, Exception ex = null)
        {
        }
    }
}
