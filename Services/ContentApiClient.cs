using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Plugin.ContentBridge.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentBridge.Services
{
    /// <summary>
    /// Client for MaccCMS-style "provide vod" content interfaces.
    /// Supports JSON (?ac=detail&amp;at=json) with an XML fallback.
    /// </summary>
    public sealed class ContentApiClient
    {
        private static readonly HttpClient Http = CreateClient();

        private readonly ILogger<ContentApiClient> _logger;

        public ContentApiClient(ILogger<ContentApiClient> logger)
        {
            _logger = logger;
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ContentBridge/0.1");
            return client;
        }

        /// <summary>
        /// Fetch one page of titles (full detail records, including play urls).
        /// </summary>
        public async Task<VodPage> GetPageAsync(SourceConfig source, int page, CancellationToken ct)
        {
            var url = BuildUrl(source.ApiUrl, "detail", page, source.TypeId, null, null);
            var body = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            var pageData = Parse(body);
            pageData.Page = page;
            return pageData;
        }

        /// <summary>
        /// Search titles by keyword.
        /// </summary>
        public async Task<VodPage> SearchAsync(SourceConfig source, string keyword, CancellationToken ct)
        {
            var url = BuildUrl(source.ApiUrl, "detail", 1, source.TypeId, keyword, null);
            var body = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            return Parse(body);
        }

        /// <summary>
        /// Fetch a single title by id.
        /// </summary>
        public async Task<VodEntry> GetDetailAsync(SourceConfig source, string id, CancellationToken ct)
        {
            var url = BuildUrl(source.ApiUrl, "detail", null, source.TypeId, null, id);
            var body = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            var pageData = Parse(body);
            return pageData.Items.FirstOrDefault();
        }

        /// <summary>
        /// Fetch the full id listing (light records) for cleanup checks. Capped at 50 pages.
        /// </summary>
        public async Task<HashSet<string>> GetAllIdsAsync(SourceConfig source, CancellationToken ct)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var pageCount = 1;
            for (var page = 1; page <= Math.Min(pageCount, 50); page++)
            {
                var url = BuildUrl(source.ApiUrl, "list", page, source.TypeId, null, null);
                string body;
                try
                {
                    body = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("Content source {Source} id listing failed: {Error}", source.Name, ex.Message);
                    break;
                }

                var pageData = Parse(body);
                pageCount = Math.Max(1, pageData.PageCount);
                foreach (var item in pageData.Items)
                {
                    if (!string.IsNullOrEmpty(item.Id))
                    {
                        ids.Add(item.Id);
                    }
                }

                if (pageData.Items.Count == 0)
                {
                    break;
                }
            }

            return ids;
        }

        private static string BuildUrl(string apiBase, string ac, int? page, int typeId, string keyword, string ids)
        {
            var baseUrl = (apiBase ?? string.Empty).Trim();
            var sb = new StringBuilder(baseUrl);
            sb.Append(baseUrl.Contains('?') ? '&' : '?');
            sb.Append("ac=").Append(ac).Append("&at=json");
            if (page.HasValue)
            {
                sb.Append("&pg=").Append(page.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (typeId > 0)
            {
                sb.Append("&t=").Append(typeId.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                sb.Append("&wd=").Append(Uri.EscapeDataString(keyword.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(ids))
            {
                sb.Append("&ids=").Append(Uri.EscapeDataString(ids));
            }

            return sb.ToString();
        }

        private VodPage Parse(string body)
        {
            var trimmed = (body ?? string.Empty).TrimStart();
            if (trimmed.StartsWith("<", StringComparison.Ordinal))
            {
                return ParseXml(body);
            }

            return ParseJson(body);
        }

        private static VodPage ParseJson(string json)
        {
            var result = new VodPage();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            result.Page = GetInt(root, "page", 1);
            result.PageCount = GetInt(root, "pagecount", 1);

            if (!TryGetProperty(root, "list", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in list.EnumerateArray())
            {
                result.Items.Add(ParseJsonEntry(item));
            }

            return result;
        }

        private static VodEntry ParseJsonEntry(JsonElement item)
        {
            var entry = new VodEntry
            {
                Id = GetString(item, "vod_id", "id"),
                Name = GetString(item, "vod_name", "name"),
                TypeName = GetString(item, "type_name", "type"),
                Pic = GetString(item, "vod_pic", "pic"),
                Remarks = GetString(item, "vod_remarks", "remarks"),
                Year = GetString(item, "vod_year", "year"),
                Area = GetString(item, "vod_area", "area"),
                Lang = GetString(item, "vod_lang", "lang"),
                Actor = GetString(item, "vod_actor", "actor"),
                Director = GetString(item, "vod_director", "director"),
                Content = GetString(item, "vod_content", "content", "des"),
                Score = GetString(item, "vod_score", "score")
            };

            var from = GetString(item, "vod_play_from", "play_from");
            var play = GetString(item, "vod_play_url", "play_url");
            entry.PlaySources = ParsePlay(from, play);
            return entry;
        }

        private static VodPage ParseXml(string xml)
        {
            var result = new VodPage();
            var doc = XDocument.Parse(xml);
            var list = doc.Root?.Element("list");
            if (list == null)
            {
                return result;
            }

            result.Page = (int?)list.Attribute("page") ?? 1;
            result.PageCount = (int?)list.Attribute("pagecount") ?? 1;

            foreach (var video in list.Elements("video"))
            {
                var entry = new VodEntry
                {
                    Id = (string)video.Element("id") ?? string.Empty,
                    Name = (string)video.Element("name") ?? string.Empty,
                    TypeName = (string)video.Element("type") ?? string.Empty,
                    Pic = (string)video.Element("pic") ?? string.Empty,
                    Remarks = (string)video.Element("remarks") ?? ((string)video.Element("last") ?? string.Empty),
                    Year = (string)video.Element("year") ?? string.Empty,
                    Area = (string)video.Element("area") ?? string.Empty,
                    Lang = (string)video.Element("lang") ?? string.Empty,
                    Actor = (string)video.Element("actor") ?? string.Empty,
                    Director = (string)video.Element("director") ?? string.Empty,
                    Content = (string)video.Element("content") ?? ((string)video.Element("des") ?? string.Empty),
                    Score = (string)video.Element("score") ?? string.Empty
                };

                var from = (string)video.Element("playfrom") ?? string.Empty;
                var dl = video.Element("dl");
                var play = dl == null
                    ? string.Empty
                    : string.Join("$$$", dl.Elements("dd").Select(dd => (string)dd ?? string.Empty));
                if (dl != null && dl.Elements("dd").Count() > 0)
                {
                    // flags align with dd elements
                    from = string.Join("$$$", dl.Elements("dd").Select(dd => (string)dd.Attribute("flag") ?? string.Empty));
                }

                entry.PlaySources = ParsePlay(from, play);
                result.Items.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// Parse "flag1$$$flag2" + "ep1$url#ep2$url$$$ep1$url" into structured play sources.
        /// </summary>
        internal static List<PlaySource> ParsePlay(string from, string play)
        {
            var sources = new List<PlaySource>();
            if (string.IsNullOrWhiteSpace(play))
            {
                return sources;
            }

            var flags = (from ?? string.Empty).Split(new[] { "$$$" }, StringSplitOptions.None);
            var lines = play.Split(new[] { "$$$" }, StringSplitOptions.None);
            for (var i = 0; i < lines.Length; i++)
            {
                var source = new PlaySource
                {
                    Name = i < flags.Length ? flags[i].Trim() : string.Empty
                };

                foreach (var episodeText in lines[i].Split('#'))
                {
                    if (string.IsNullOrWhiteSpace(episodeText))
                    {
                        continue;
                    }

                    var parts = episodeText.Split('$');
                    var url = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                    if (string.IsNullOrEmpty(url))
                    {
                        continue;
                    }

                    source.Episodes.Add(new PlayEpisode
                    {
                        Name = parts[0].Trim(),
                        Url = url
                    });
                }

                if (source.Episodes.Count > 0)
                {
                    sources.Add(source);
                }
            }

            return sources;
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            return element.TryGetProperty(name, out value);
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
                        JsonValueKind.Null => string.Empty,
                        _ => string.Empty
                    };
                }
            }

            return string.Empty;
        }

        private static int GetInt(JsonElement element, string name, int fallback)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                return fallback;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            return value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }
    }
}
