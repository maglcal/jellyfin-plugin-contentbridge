using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentBridge.Compat;
using Jellyfin.Plugin.ContentBridge.Models;

namespace Jellyfin.Plugin.ContentBridge.Services
{
    /// <summary>
    /// Loader for the ContentBridge Feed standard (contentbridge-feed/1).
    /// Reads single JSON files, directories of *.json files, or http(s) feed urls.
    /// See FEED-SPEC.md for the contract.
    /// </summary>
    public sealed class FeedClient
    {
        public const string SchemaId = "contentbridge-feed/1";
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        private readonly ILogSink _log;

        public FeedClient(ILogSink log)
        {
            _log = log ?? NullLogSink.Instance;
        }

        /// <summary>
        /// Load and merge all entries for a feed source. Returns the merged document,
        /// including feed-level update metadata (generator / version / mode).
        /// </summary>
        public async Task<FeedData> LoadAsync(SourceConfig source, CancellationToken ct)
        {
            var location = (source.ApiUrl ?? string.Empty).Trim();
            if (location.Length == 0)
            {
                throw new InvalidOperationException("Feed 路径或 URL 为空");
            }

            if (location.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || location.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var body = await Http.GetStringAsync(location, ct).ConfigureAwait(false);
                return ParseFeedDoc(body);
            }

            if (File.Exists(location))
            {
                return ParseFeedDoc(await File.ReadAllTextAsync(location, ct).ConfigureAwait(false));
            }

            if (Directory.Exists(location))
            {
                var merged = new Dictionary<string, VodEntry>(StringComparer.Ordinal);
                var result = new FeedData();
                foreach (var file in Directory.GetFiles(location, "*.json").OrderBy(f => f, StringComparer.Ordinal))
                {
                    ct.ThrowIfCancellationRequested();
                    FeedData part;
                    try
                    {
                        part = ParseFeedDoc(await File.ReadAllTextAsync(file, ct).ConfigureAwait(false));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.Warn($"Feed file {file} skipped: {ex.Message}");
                        continue;
                    }

                    // header merge: delta wins (conservative), generator/version taken from the latest file
                    if (part.IsDelta)
                    {
                        result.Mode = FeedData.ModeDelta;
                    }

                    if (part.Generator.Length > 0)
                    {
                        result.Generator = part.Generator;
                    }

                    if (part.Version.Length > 0)
                    {
                        result.Version = part.Version;
                    }

                    // later files (by name order) override earlier ones on id collision
                    foreach (var entry in part.Items)
                    {
                        merged[entry.Id] = entry;
                    }
                }

                result.Items = merged.Values.ToList();
                return result;
            }

            throw new InvalidOperationException($"Feed 路径不存在：{location}");
        }

        /// <summary>
        /// Parse one feed document (contentbridge-feed/1), including header metadata.
        /// </summary>
        public static FeedData ParseFeedDoc(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var schema = GetString(root, "schema");
            if (!string.Equals(schema, SchemaId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"schema 不匹配（期望 {SchemaId}，实际 {(string.IsNullOrEmpty(schema) ? "缺失" : schema)}）");
            }

            var result = new FeedData
            {
                Generator = GetString(root, "generator"),
                Version = GetString(root, "version"),
                Mode = GetString(root, "mode"),
                Updated = GetString(root, "updated")
            };

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var entry = ParseItem(item);
                    if (entry != null)
                    {
                        result.Items.Add(entry);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Parse one feed document and return only its items (compat helper).
        /// </summary>
        public static List<VodEntry> ParseFeed(string json)
        {
            return ParseFeedDoc(json).Items;
        }

        private static VodEntry ParseItem(JsonElement item)
        {
            var id = GetString(item, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            // tombstone: {"id": "...", "deleted": true} — entry removed upstream
            var deletedFlag = GetString(item, "deleted");
            var deleted = deletedFlag == "true" || deletedFlag == "1";

            var title = GetString(item, "title");
            if (string.IsNullOrWhiteSpace(title) && !deleted)
            {
                return null;
            }

            var entry = new VodEntry
            {
                Id = id,
                Name = title,
                Deleted = deleted,
                Updated = GetString(item, "updated"),
                Year = GetString(item, "year"),
                TypeName = GetString(item, "category", "type_name"),
                Pic = GetString(item, "poster", "pic"),
                Remarks = GetString(item, "remarks"),
                Content = GetString(item, "summary", "content"),
                Score = GetString(item, "score"),
                Area = GetString(item, "area"),
                Lang = GetString(item, "lang"),
                Actor = JoinList(item, "actors", "actor"),
                Director = JoinList(item, "directors", "director")
            };

            var forcedType = GetString(item, "type");
            if (item.TryGetProperty("playLines", out var lines) && lines.ValueKind == JsonValueKind.Array)
            {
                foreach (var line in lines.EnumerateArray())
                {
                    var source = new PlaySource
                    {
                        Name = GetString(line, "name")
                    };

                    if (line.TryGetProperty("episodes", out var episodes) && episodes.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ep in episodes.EnumerateArray())
                        {
                            var url = GetString(ep, "url");
                            if (string.IsNullOrWhiteSpace(url))
                            {
                                continue;
                            }

                            int? number = null;
                            if (ep.TryGetProperty("number", out var n))
                            {
                                if (n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var parsed))
                                {
                                    number = parsed;
                                }
                                else if (n.ValueKind == JsonValueKind.String
                                         && int.TryParse(n.GetString(), out parsed)
                                         && parsed > 0)
                                {
                                    // tolerant of numeric strings ("3") — many sources emit them
                                    number = parsed;
                                }
                            }

                            source.Episodes.Add(new PlayEpisode
                            {
                                Name = GetString(ep, "name"),
                                Url = url,
                                Number = number
                            });
                        }
                    }

                    if (source.Episodes.Count > 0)
                    {
                        entry.PlaySources.Add(source);
                    }
                }
            }

            // forced layout type is honored by the writer (movie/series/auto)
            entry.EntryType = forcedType;

            return entry;
        }

        private static string JoinList(JsonElement item, params string[] names)
        {
            foreach (var name in names)
            {
                if (!item.TryGetProperty(name, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Array)
                {
                    return string.Join(" / ", value.EnumerateArray()
                        .Select(v => v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty) : v.GetRawText())
                        .Where(v => v.Length > 0));
                }

                return GetString(item, name);
            }

            return string.Empty;
        }

        private static string GetString(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var value))
                {
                    return value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString() ?? string.Empty,
                        JsonValueKind.Number => value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => string.Empty,
                        _ => string.Empty
                    };
                }
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// One parsed feed document: header metadata plus items.
    /// </summary>
    public sealed class FeedData
    {
        public const string ModeDelta = "delta";

        /// <summary>采集程序名称 / 版本（generator 字段），仅作展示。</summary>
        public string Generator { get; set; } = string.Empty;

        /// <summary>数据版本号（version 字段），语义由采集程序自定。</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>"full"（默认）或 "delta"。</summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>整份数据的产出时间（ISO-8601），仅作展示。</summary>
        public string Updated { get; set; } = string.Empty;

        public List<VodEntry> Items { get; set; } = new List<VodEntry>();

        /// <summary>
        /// delta = 只含变更条目（配合 deleted 墓碑表达下架），此时禁止全量清理，
        /// 否则会把未出现在本次 feed 里的正常条目误删。
        /// </summary>
        public bool IsDelta => string.Equals(Mode, ModeDelta, StringComparison.OrdinalIgnoreCase);
    }
}
