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
using Jellyfin.Plugin.ContentBridge.Compat;
using Jellyfin.Plugin.ContentBridge.Models;

namespace Jellyfin.Plugin.ContentBridge.Services
{
    /// <summary>
    /// Writes virtual library entries (.strm + .nfo + poster) for Jellyfin to scrape.
    /// </summary>
    public sealed class StrmLibraryWriter
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        // Explicit episode markers, matched anywhere in the name and in priority order,
        // so noise like a year ("2024年…第3集") or a resolution ("1080P正片") never wins.
        private static readonly Regex EpisodeKeywordRegex = new Regex(
            @"第?\s*(\d{1,4})\s*[集话話回]",
            RegexOptions.Compiled);

        private static readonly Regex EpisodeEpRegex = new Regex(
            @"[Ee][Pp]?(?:isode)?\s*[-_.]?\s*(\d{1,4})",
            RegexOptions.Compiled);

        private static readonly Regex EpisodeLeadingIndexRegex = new Regex(
            @"^\s*(\d{1,4})\s*[\.、，,_\-—\)）\]]",
            RegexOptions.Compiled);

        private static readonly Regex EpisodeWholeNumberRegex = new Regex(
            @"^\s*(\d{1,4})\s*[\.、。]?\s*$",
            RegexOptions.Compiled);

        private static readonly Regex HtmlTagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

        private readonly ILogSink _log;
        private readonly StreamProxyService _proxy;

        public StrmLibraryWriter(ILogSink log, StreamProxyService proxy)
        {
            _log = log ?? NullLogSink.Instance;
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

            // drop episodes whose urls can never play (not absolute http/https) before anything else
            var playableSources = new List<PlaySource>();
            var droppedUrls = 0;
            foreach (var line in entry.PlaySources)
            {
                var playable = line.Episodes.Where(e => IsPlayableUrl(e.Url)).ToList();
                droppedUrls += line.Episodes.Count - playable.Count;
                if (playable.Count > 0)
                {
                    playableSources.Add(new PlaySource { Name = line.Name, Episodes = playable });
                }
            }

            var playLine = PickPlaySource(playableSources, config.PreferredQuality);
            var episodes = playLine?.Episodes ?? new List<PlayEpisode>();
            if (episodes.Count == 0)
            {
                _log.Debug($"Skip {entry.Name}: no playable url");
                return written;
            }

            if (droppedUrls > 0)
            {
                _log.Warn(
                    $"Entry {entry.Name}: skipped {droppedUrls} episode url(s) that are not absolute http(s) links");
            }

            var displayName = CleanTitle(entry.Name);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = "未命名";
            }

            var title = Sanitize(displayName);
            var year = string.IsNullOrWhiteSpace(entry.Year)
                ? string.Empty
                : Sanitize(entry.Year.Trim());
            var folderName = string.IsNullOrEmpty(year) ? title : $"{title} ({year})";
            var sourceRoot = Path.Combine(config.LibraryRoot, Sanitize(source.Name));
            var titleRoot = Path.Combine(sourceRoot, folderName);

            string Resolve(string url) => source.UseProxy ? _proxy.BuildProxyUrl(source.Name, url) : url;

            Directory.CreateDirectory(titleRoot);

            var isSeries = episodes.Count > 1
                           || string.Equals(entry.EntryType, "series", StringComparison.OrdinalIgnoreCase);

            if (!isSeries)
            {
                // movie
                var strmName = $"{folderName}.strm";
                await File.WriteAllTextAsync(
                    Path.Combine(titleRoot, strmName),
                    Resolve(episodes[0].Url) + "\n",
                    new UTF8Encoding(false),
                    ct).ConfigureAwait(false);
                written.Add(Rel(config.LibraryRoot, Path.Combine(titleRoot, strmName)));

                if (config.WriteNfo)
                {
                    var nfoPath = Path.Combine(titleRoot, "movie.nfo");
                    await File.WriteAllTextAsync(
                        nfoPath,
                        BuildMovieNfo(entry, displayName),
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
                        BuildTvShowNfo(entry, displayName),
                        new UTF8Encoding(false),
                        ct).ConfigureAwait(false);
                    written.Add(Rel(config.LibraryRoot, nfoPath));
                }

                var usedNumbers = new HashSet<int>();
                for (var i = 0; i < episodes.Count; i++)
                {
                    var episode = episodes[i];
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
                    _log.Warn($"Failed to delete {rel}: {ex.Message}");
                }
            }
        }

        private static PlaySource PickPlaySource(IReadOnlyList<PlaySource> sources, string preferredQuality)
        {
            if (sources.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(preferredQuality))
            {
                var keyword = preferredQuality.Trim();
                var match = sources.FirstOrDefault(
                    s => s.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                         || s.Episodes.Any(e => e.Url.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0));
                if (match != null)
                {
                    return match;
                }
            }

            return sources.FirstOrDefault(s => s.Episodes.Count > 0);
        }

        private static int ResolveEpisodeNumber(PlayEpisode episode, int fallback, HashSet<int> used)
        {
            var number = fallback;

            // explicit number from the feed standard wins
            if (episode?.Number is int explicitNumber && explicitNumber > 0)
            {
                number = explicitNumber;
            }
            else
            {
                number = ParseEpisodeNumber(episode?.Name) ?? fallback;
            }

            // keep numbering strictly increasing to avoid collisions
            while (used.Contains(number))
            {
                number++;
            }

            used.Add(number);
            return number;
        }

        /// <summary>
        /// Parse an episode number from a display name. Explicit markers ("第N集" / "EP N") win
        /// anywhere in the string; bare digits are only trusted as a leading index ("03.标题")
        /// or when they are the whole name ("03") — never a year ("2024年…") or a
        /// resolution ("1080P正片").
        /// </summary>
        private static int? ParseEpisodeNumber(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var text = name.Trim();

            foreach (var regex in new[] { EpisodeKeywordRegex, EpisodeEpRegex })
            {
                var match = regex.Match(text);
                if (match.Success
                    && int.TryParse(match.Groups[1].Value, out var parsed)
                    && parsed > 0)
                {
                    return parsed;
                }
            }

            var leading = EpisodeLeadingIndexRegex.Match(text);
            if (leading.Success
                && int.TryParse(leading.Groups[1].Value, out var leadingParsed)
                && leadingParsed > 0)
            {
                return leadingParsed;
            }

            var whole = EpisodeWholeNumberRegex.Match(text);
            if (whole.Success
                && int.TryParse(whole.Groups[1].Value, out var wholeParsed)
                && wholeParsed > 0)
            {
                return wholeParsed;
            }

            return null;
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
            var episodeName = CleanTitle(episode?.Name);
            var root = new XElement(
                "episodedetails",
                new XElement("title", string.IsNullOrWhiteSpace(episodeName) ? $"第{number}集" : episodeName),
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
            root.Add(new XElement("sorttitle", title));
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

        /// <summary>
        /// Clean a display name: strip HTML tags (no space substitution — titles want
        /// "铁血战士</b>：狩猎" to become "铁血战士：狩猎", not "铁血战士 ：狩猎"),
        /// decode entities and collapse whitespace.
        /// </summary>
        private static string CleanTitle(string value)
        {
            var text = HtmlTagRegex.Replace(value ?? string.Empty, string.Empty);
            text = System.Net.WebUtility.HtmlDecode(text);
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static bool IsPlayableUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
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
                _log.Debug($"Poster download failed for {url}: {ex.Message}");
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
