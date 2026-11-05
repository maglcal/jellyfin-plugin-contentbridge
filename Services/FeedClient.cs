using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentBridge.Models;
using Microsoft.Extensions.Logging;

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

        private readonly ILogger<FeedClient> _logger;

        public FeedClient(ILogger<FeedClient> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Load and merge all entries for a feed source.
        /// </summary>
        public async Task<List<VodEntry>> LoadAsync(SourceConfig source, CancellationToken ct)
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
                return ParseFeed(body);
            }

            if (File.Exists(location))
            {
                return ParseFeed(await File.ReadAllTextAsync(location, ct).ConfigureAwait(false));
            }

            if (Directory.Exists(location))
            {
                var merged = new Dictionary<string, VodEntry>(StringComparer.Ordinal);
                foreach (var file in Directory.GetFiles(location, "*.json").OrderBy(f => f, StringComparer.Ordinal))
                {
                    ct.ThrowIfCancellationRequested();
                    List<VodEntry> entries;
                    try
                    {
                        entries = ParseFeed(await File.ReadAllTextAsync(file, ct).ConfigureAwait(false));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning("Feed file {File} skipped: {Error}", file, ex.Message);
                        continue;
                    }

                    // later files (by name order) override earlier ones on id collision
                    foreach (var entry in entries)
                    {
                        merged[entry.Id] = entry;
                    }
                }

                return merged.Values.ToList();
            }

            throw new InvalidOperationException($"Feed 路径不存在：{location}");
        }

        /// <summary>
        /// Parse one feed document (contentbridge-feed/1).
        /// </summary>
        public static List<VodEntry> ParseFeed(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var schema = GetString(root, "schema");
            if (!string.Equals(schema, SchemaId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"schema 不匹配（期望 {SchemaId}，实际 {(string.IsNullOrEmpty(schema) ? "缺失" : schema)}）");
            }

            var result = new List<VodEntry>();
            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in items.EnumerateArray())
            {
                var entry = ParseItem(item);
                if (entry != null)
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        private static VodEntry ParseItem(JsonElement item)
        {
            var id = GetString(item, "id");
            var title = GetString(item, "title");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var entry = new VodEntry
            {
                Id = id,
                Name = title,
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
                            if (ep.TryGetProperty("number", out var n)
                                && n.ValueKind == JsonValueKind.Number
                                && n.TryGetInt32(out var parsed))
                            {
                                number = parsed;
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
}
