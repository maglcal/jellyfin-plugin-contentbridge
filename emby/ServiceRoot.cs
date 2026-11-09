using Jellyfin.Plugin.ContentBridge.Compat;
using Jellyfin.Plugin.ContentBridge.Services;
using MediaBrowser.Controller;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.ContentBridge.EmbyBinding
{
    /// <summary>
    /// Composition root for the Emby build (Emby 无 DI 容器注入). Emby binds services and
    /// scheduled tasks from its own container; the plugin's shared core graph is assembled
    /// here once and reused by every binder. On Jellyfin the DI container owns the graph,
    /// so this class is not used there.
    /// </summary>
    public static class ServiceRoot
    {
        private static readonly object Gate = new object();

        private static ILogSink _log = NullLogSink.Instance;
        private static IServerApplicationHost _appHost;
        private static ContentApiClient _client;
        private static FeedClient _feed;
        private static StrmLibraryWriter _writer;
        private static StreamProxyService _proxy;
        private static SyncService _sync;

        /// <summary>
        /// Gets the task manager captured from the host.
        /// </summary>
        public static ITaskManager TaskManager { get; private set; }

        /// <summary>
        /// Gets the application host captured from the host.
        /// </summary>
        public static IServerApplicationHost AppHost
        {
            get { lock (Gate) { return _appHost; } }
        }

        /// <summary>
        /// Attach host services. Called first from the Emby binders (task / API services);
        /// idempotent, first attachment wins.
        /// </summary>
        /// <param name="logManager">Emby log manager.</param>
        /// <param name="appHost">Emby application host.</param>
        /// <param name="taskManager">Emby task manager.</param>
        public static void EnsureAttached(ILogManager logManager, IServerApplicationHost appHost, ITaskManager taskManager)
        {
            lock (Gate)
            {
                if (_appHost == null && appHost != null)
                {
                    _appHost = appHost;
                }

                if (logManager != null && _log is NullLogSink)
                {
                    _log = new EmbyLogSink(logManager.GetLogger("ContentBridge"));
                }

                if (taskManager != null && TaskManager == null)
                {
                    TaskManager = taskManager;
                }
            }
        }

        /// <summary>Gets the shared content API client.</summary>
        public static ContentApiClient ContentApiClient
        {
            get { lock (Gate) { return _client ??= new ContentApiClient(_log); } }
        }

        /// <summary>Gets the shared feed client.</summary>
        public static FeedClient FeedClient
        {
            get { lock (Gate) { return _feed ??= new FeedClient(_log); } }
        }

        /// <summary>Gets the shared stream proxy service.</summary>
        public static StreamProxyService StreamProxyService
        {
            get { lock (Gate) { return _proxy ??= new StreamProxyService(_appHost, _log); } }
        }

        /// <summary>Gets the shared strm library writer.</summary>
        public static StrmLibraryWriter StrmLibraryWriter
        {
            get { lock (Gate) { return _writer ??= new StrmLibraryWriter(_log, StreamProxyService); } }
        }

        /// <summary>Gets the shared sync service.</summary>
        public static SyncService SyncService
        {
            get { lock (Gate) { return _sync ??= new SyncService(ContentApiClient, FeedClient, StrmLibraryWriter, _log); } }
        }
    }
}
