// Copyright (C) 2026 许诺
// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;

namespace Jellyfin.Plugin.ContentBridge.Models
{
    /// <summary>
    /// One title returned by a content source.
    /// </summary>
    public sealed class VodEntry
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string TypeName { get; set; } = string.Empty;

        public string Pic { get; set; } = string.Empty;

        public string Remarks { get; set; } = string.Empty;

        public string Year { get; set; } = string.Empty;

        public string Area { get; set; } = string.Empty;

        public string Lang { get; set; } = string.Empty;

        public string Actor { get; set; } = string.Empty;

        public string Director { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public string Score { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the forced layout type: "movie", "series", or empty for auto
        /// (1 episode = movie, more = series). Feed standard only.
        /// </summary>
        public string EntryType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the source-side change stamp (ISO-8601). Feed standard only.
        /// Used to skip rewriting entries that have not changed.
        /// </summary>
        public string Updated { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether this item is a tombstone (feed standard:
        /// <c>"deleted": true</c> marks the entry as removed upstream and deletes the files
        /// written for it).
        /// </summary>
        public bool Deleted { get; set; }

        /// <summary>
        /// Gets the play lines. Each line (flag) contains its own episode list.
        /// </summary>
        public List<PlaySource> PlaySources { get; set; } = new List<PlaySource>();
    }

    /// <summary>
    /// A play line ("vod_play_from" / "vod_play_url" pair).
    /// </summary>
    public sealed class PlaySource
    {
        public string Name { get; set; } = string.Empty;

        public List<PlayEpisode> Episodes { get; set; } = new List<PlayEpisode>();
    }

    /// <summary>
    /// One episode / play entry.
    /// </summary>
    public sealed class PlayEpisode
    {
        public string Name { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        /// <summary>Gets or sets the optional explicit episode number (feed standard).</summary>
        public int? Number { get; set; }
    }

    /// <summary>
    /// Result page of a list query.
    /// </summary>
    public sealed class VodPage
    {
        public int Page { get; set; } = 1;

        public int PageCount { get; set; } = 1;

        public List<VodEntry> Items { get; set; } = new List<VodEntry>();
    }
}
