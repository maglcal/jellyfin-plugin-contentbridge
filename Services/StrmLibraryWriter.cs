using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Plugin.ContentBridge.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentBridge.Services
{
    /// <summary>
    /// Writes virtual library entries (.strm + .nfo + poster) for Jellyfin to scrape.
    /// </summary>
    public sealed class StrmLibraryWriter
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        private static readonly Regex EpisodeNumberRegex = new Regex(
            @"(?:第\s*(\d+)\s*[集话話回])|(?:[Ee][Pp]?\s*[-_]?\s*(\d+))|(?:^|\D)(\d{1,4})(?:\D|$)",
            RegexOptions.Compiled);

        private static readonly Regex HtmlTagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

        private readonly ILogger<StrmLibraryWriter> _logger;
        private readonly StreamProxyService _proxy;

        public StrmLibraryWriter(ILogger<StrmLibraryWriter> logger, StreamProxyService proxy)
        {
            _logger = logger;
            _proxy = proxy;
        }

        /// <summary>
        /// Pick the best play line and write the title under root/{sourceName}/{title}.
        /// Returns the relative file paths written (for manifest tracking).
        /// </summary>
        public async Task<IReadOnlyList<string>> WriteAsync(
            VodEntry entry,
            SourceConfig source,
            PluginConfiguration config,
            CancellationToken ct)
        {
            var written = new List<string>();
            var playLine = PickPlaySource(entry, config.PreferredQuality);
            if (playLine == null || playLine.Episodes.Count == 0)
            {
                _logger.LogDebug("Skip {Name}: no playable url", entry.Name);
                return written;
            }

            var title = Sanitize(string.IsNullOrWhiteSpace(entry.Name) ? "未命名" : entry.Name.Trim());
            var year = Sanitize((entry.Year ?? string.Empty).Trim());
            var folderName = string.IsNullOrEmpty(year) ? title : $"{title} ({year})";
            var sourceRoot = Path.Combine(config.LibraryRoot, Sanitize(source.Name));
            var titleRoot = Path.Combine(sourceRoot, folderName);

            string Resolve(string url) => source.UseProxy ? _proxy.BuildProxyUrl(source.Name, url) : url;

            Directory.CreateDirectory(titleRoot);

            var isSeries = playLine.Episodes.Count > 1
                           || string.Equals(entry.EntryType, "series", StringComparison.OrdinalIgnoreCase);

            if (!isSeries)
            {
                // movie
                var strmName = $"{folderName}.strm";
                await File.WriteAllTextAsync(
                    Path.Combine(titleRoot, strmName),
                    Resolve(playLine.Episodes[0].Url) + "\n",
                    new UTF8Encoding(false),
                    ct).ConfigureAwait(false);
                written.Add(Rel(config.LibraryRoot, Path.Combine(titleRoot, strmName)));

                if (config.WriteNfo)
                {
                    var nfoPath = Path.Combine(titleRoot, "movie.nfo");
                    await File.WriteAllTextAsync(
                        nfoPath,
                        BuildMovieNfo(entry, folderName),
                        new UTF8Encoding(false),
                        ct).ConfigureAwait(false);
                    written.Add(Rel(config.LibraryRoot, nfoPath));
                }
            }
            else
            {
                // series
                var seasonDir = Path.Combine(titleRoot, "Season 01");
                Directory.CreateDirectory(seasonDir);

                if (config.WriteNfo)
                {
                    var nfoPath = Path.Combine(titleRoot, "tvshow.nfo");
                    await File.WriteAllTextAsync(
                        nfoPath,
                        BuildTvShowNfo(entry, title),
                        new UTF8Encoding(false),
                        ct).ConfigureAwait(false);
                    written.Add(Rel(config.LibraryRoot, nfoPath));
                }

                var usedNumbers = new HashSet<int>();
                for (var i = 0; i < playLine.Episodes.Count; i++)
                {
                    var episode = playLine.Episodes[i];
                    var number = ResolveEpisodeNumber(episode, i + 1, usedNumbers);
                    var baseName = $"{title} - S01E{number:00}";
                    var strmPath = Path.Combine(seasonDir, baseName + ".strm");
                    await File.WriteAllTextAsync(
                        strmPath,
                        Resolve(episode.Url) + "\n",
                        new UTF8Encoding(false),
                        ct).ConfigureAwait(false);
                    written.Add(Rel(config.LibraryRoot, strmPath));

                    if (config.WriteNfo)
                    {
                        var epNfoPath = Path.Combine(seasonDir, baseName + ".nfo");
                        await File.WriteAllTextAsync(
                            epNfoPath,
                            BuildEpisodeNfo(entry, episode, number),
                            new UTF8Encoding(false),
                            ct).ConfigureAwait(false);
                        written.Add(Rel(config.LibraryRoot, epNfoPath));
                    }
                }
            }

            if (config.DownloadImages && !string.IsNullOrWhiteSpace(entry.Pic))
            {
                var posterPath = Path.Combine(titleRoot, "poster.jpg");
                if (await DownloadImageAsync(entry.Pic, posterPath, ct).ConfigureAwait(false))
                {
                    written.Add(Rel(config.LibraryRoot, posterPath));
                }
            }

            return written;
        }

        /// <summary>
        /// Delete generated files (and prune empty folders) for the given relative paths.
        /// </summary>
        public void Delete(IReadOnlyCollection<string> relativePaths, string libraryRoot)
        {
            foreach (var rel in relativePaths)
            {
                try
                {
                    var full = Path.GetFullPath(Path.Combine(libraryRoot, rel));
                    var rootFull = Path.GetFullPath(libraryRoot);
                    if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (File.Exists(full))
                    {
                        File.Delete(full);
                    }

                    PruneEmptyDirs(Path.GetDirectoryName(full), rootFull);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to delete {Path}: {Error}", rel, ex.Message);
                }
            }
        }

        private static PlaySource PickPlaySource(VodEntry entry, string preferredQuality)
        {
            if (entry.PlaySources.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(preferredQuality))
            {
                var keyword = preferredQuality.Trim();
                var match = entry.PlaySources.FirstOrDefault(
                    s => s.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                         || s.Episodes.Any(e => e.Url.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0));
                if (match != null)
                {
                    return match;
                }
            }

            return entry.PlaySources.FirstOrDefault(s => s.Episodes.Count > 0);
        }

        private static int ResolveEpisodeNumber(PlayEpisode episode, int fallback, HashSet<int> used)
        {
            var number = fallback;

            // explicit number from the feed standard wins
            if (episode.Number is int explicitNumber && explicitNumber > 0)
            {
                number = explicitNumber;
            }
            else
            {
                var match = EpisodeNumberRegex.Match(episode.Name ?? string.Empty);
                if (match.Success)
                {
                    foreach (Group group in match.Groups)
                    {
                        if (group.Success && int.TryParse(group.Value, out var parsed) && parsed > 0)
                        {
                            number = parsed;
                            break;
                        }
                    }
                }
            }

            // keep numbering strictly increasing to avoid collisions
            while (used.Contains(number))
            {
                number++;
            }

            used.Add(number);
            return number;
        }

        private static string BuildMovieNfo(VodEntry entry, string title)
        {
            var root = new XElement("movie");
            AppendCommonMetadata(root, entry, title);
            return ToXmlString(root);
        }

        private static string BuildTvShowNfo(VodEntry entry, string title)
        {
            var root = new XElement("tvshow");
            AppendCommonMetadata(root, entry, title);
            return ToXmlString(root);
        }

        private static string BuildEpisodeNfo(VodEntry entry, PlayEpisode episode, int number)
        {
            var root = new XElement(
                "episodedetails",
                new XElement("title", string.IsNullOrWhiteSpace(episode.Name) ? $"第{number}集" : episode.Name),
                new XElement("season", 1),
                new XElement("episode", number));
            if (!string.IsNullOrWhiteSpace(entry.Content))
            {
                root.Add(new XElement("plot", CleanText(entry.Content)));
            }

            return ToXmlString(root);
        }

        private static void AppendCommonMetadata(XElement root, VodEntry entry, string title)
        {
            root.Add(new XElement("title", title));
            root.Add(new XElement("sorttitle", entry.Name ?? title));
            if (!string.IsNullOrWhiteSpace(entry.Year))
            {
                root.Add(new XElement("year", entry.Year.Trim()));
                if (entry.Year.Trim().Length == 4)
                {
                    root.Add(new XElement("premiered", entry.Year.Trim() + "-01-01"));
                }
            }

            if (!string.IsNullOrWhiteSpace(entry.Content))
            {
                var plot = CleanText(entry.Content);
                root.Add(new XElement("plot", plot));
                root.Add(new XElement("outline", plot));
            }

            foreach (var genre in SplitList(entry.TypeName))
            {
                root.Add(new XElement("genre", genre));
            }

            foreach (var country in SplitList(entry.Area))
            {
                root.Add(new XElement("country", country));
            }

            foreach (var director in SplitList(entry.Director))
            {
                root.Add(new XElement("director", director));
            }

            foreach (var actorName in SplitList(entry.Actor))
            {
                root.Add(new XElement("actor", new XElement("name", actorName)));
            }

            if (!string.IsNullOrWhiteSpace(entry.Pic))
            {
                root.Add(new XElement("thumb", entry.Pic.Trim()));
                root.Add(new XElement("poster", entry.Pic.Trim()));
            }

            if (double.TryParse(entry.Score, out var score) && score > 0)
            {
                root.Add(new XElement("rating", score));
            }

            root.Add(new XElement("uniqueid", new XAttribute("type", "contentbridge"), entry.Id));
        }

        private static IEnumerable<string> SplitList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            return value
                .Split(new[] { '/', '、', ',', '，', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => v.Length > 0)
                .Distinct();
        }

        private static string CleanText(string value)
        {
            var text = HtmlTagRegex.Replace(value ?? string.Empty, " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static string ToXmlString(XElement root)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine
                 + root.ToString(SaveOptions.None) + Environment.NewLine;
        }

        private async Task<bool> DownloadImageAsync(string url, string targetPath, CancellationToken ct)
        {
            try
            {
                if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
                {
                    return true;
                }

                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var file = File.Create(targetPath);
                await stream.CopyToAsync(file, ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug("Poster download failed for {Url}: {Error}", url, ex.Message);
                return false;
            }
        }

        private static string Rel(string root, string fullPath)
        {
            return Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "未命名";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            foreach (var ch in name.Trim())
            {
                builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            }

            var result = builder.ToString().Trim().TrimEnd('.');
            return string.IsNullOrEmpty(result) ? "未命名" : result;
        }

        private static void PruneEmptyDirs(string directory, string root)
        {
            var current = directory;
            while (!string.IsNullOrEmpty(current)
                   && current.Length > root.Length
                   && current.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(current) && !Directory.EnumerateFileSystemEntries(current).Any())
                {
                    Directory.Delete(current);
                    current = Path.GetDirectoryName(current);
                }
                else
                {
                    break;
                }
            }
        }
    }
}
