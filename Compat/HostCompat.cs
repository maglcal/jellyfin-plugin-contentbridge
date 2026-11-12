// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Reflection;

namespace Jellyfin.Plugin.ContentBridge.Compat
{
    /// <summary>
    /// Which media-server host the plugin is running under. Detected at runtime by probing
    /// the loaded host assemblies (see <see cref="HostCompat.Detect"/>).
    /// </summary>
    public enum HostKind
    {
        /// <summary>Host could not be determined.</summary>
        Unknown = 0,

        /// <summary>Jellyfin (assembly Jellyfin.*).</summary>
        Jellyfin = 1,

        /// <summary>Emby (assembly MediaBrowser.*).</summary>
        Emby = 2,
    }

    /// <summary>
    /// Foundation of the host compatibility layer: runtime host detection plus the
    /// reflection helpers every other Compat shim is built on.
    ///
    /// Guiding rule (see docs/EMBY-COMPAT.md): reflection beats conditional compilation —
    /// one set of sources compiles against both hosts, and members that only exist on one
    /// host are set opportunistically instead of referenced directly.
    /// </summary>
    public static class HostCompat
    {
        private static HostKind? _detected;

        /// <summary>
        /// Detect the current host by probing which assemblies the host provides. Cached after first call.
        ///
        /// Assembly names are the reliable marker (namespaces are shared between the two hosts):
        /// Jellyfin ships Jellyfin.* assemblies (Jellyfin.Data on every 10.x, Jellyfin.Common on
        /// 10.11+ which renamed the legacy MediaBrowser.* names), while Emby keeps MediaBrowser.*
        /// and adds Emby.* assemblies. A MediaBrowser.Common without either marker stays Unknown.
        /// </summary>
        /// <returns>The detected host kind.</returns>
        public static HostKind Detect()
        {
            if (_detected.HasValue)
            {
                return _detected.Value;
            }

            var kind = HostKind.Unknown;
            try
            {
                if (HasAssembly("Jellyfin.Common") || HasAssembly("Jellyfin.Data"))
                {
                    kind = HostKind.Jellyfin;
                }
                else if (HasAssembly("MediaBrowser.Common")
                         && (HasAssembly("Emby.Server.Implementations") || HasAssembly("Emby.Naming")))
                {
                    kind = HostKind.Emby;
                }
            }
            catch (Exception)
            {
                kind = HostKind.Unknown;
            }

            _detected = kind;
            return kind;
        }

        private static bool HasAssembly(string assemblyName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            try
            {
                Assembly.Load(new AssemblyName(assemblyName));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Set a public instance property only when it exists on the target's type and is
        /// writable. Used for host-specific members (e.g. Jellyfin-only PluginPageInfo fields)
        /// so the same source compiles and runs on both hosts.
        /// </summary>
        /// <param name="target">Object to patch.</param>
        /// <param name="propertyName">Property name.</param>
        /// <param name="value">Value to assign (converted to the property type when needed).</param>
        /// <returns>True when the property existed and was set.</returns>
        public static bool TrySetOptionalProperty(object target, string propertyName, object value)
        {
            if (target == null || string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            PropertyInfo prop;
            try
            {
                prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception)
            {
                return false;
            }

            if (prop == null || !prop.CanWrite)
            {
                return false;
            }

            try
            {
                object converted = value;
                if (value != null && !prop.PropertyType.IsInstanceOfType(value))
                {
                    converted = prop.PropertyType.IsEnum
                        ? Enum.Parse(prop.PropertyType, value.ToString(), ignoreCase: true)
                        : Convert.ChangeType(value, prop.PropertyType);
                }

                prop.SetValue(target, converted);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
