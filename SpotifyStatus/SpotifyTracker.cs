using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using WebSocketSharp.Server;

namespace SpotifyStatus
{
    internal static class SpotifyTracker
    {
        private const string wsServiceName = "/neos-spotify-bridge";
        private static readonly OAuthClient oAuthClient = new OAuthClient(SpotifyClientConfig.CreateDefault());
        private static readonly ManualResetEventSlim spotifyClientAvailable = new ManualResetEventSlim(false);
        private static readonly Timer updateTimer = new Timer(Update);
        private static DateTime accessExpiry;
        private static AbsoluteTimer.AbsoluteTimer authTimer;
        private static WebSocketServer wsServer;
        public static CurrentlyPlayingContext LastPlayingContext { get; private set; }
        public static int Listeners => wsServer.WebSocketServices[wsServiceName].Sessions.Count;

        /// <summary>
        /// Gets or sets the playback refresh interval in milliseconds.
        /// </summary>
        public static int RefreshInterval { get; set; } = 30000;

        public static int RepeatNum { get; set; }

        public static SpotifyClient Spotify { get; private set; }

        public static void Start()
        {
            Task.Run(() =>
            {
                wsServer = new WebSocketServer(IPAddress.Loopback, SpotifyStatus.Config.GetValue(SpotifyStatus.Port), false);
                wsServer.AddWebSocketService<SpotifyPlaybackService>(wsServiceName);
                wsServer.Start();

                SpotifyStatus.Msg("WebSocket Server running at: " + wsServer.Address + ":" + wsServer.Port);
                handleAuthorization();

                spotifyClientAvailable.Wait();

                Update();
            });
        }

        public static async void Update(object _ = null)
        {
            spotifyClientAvailable.Wait();

            // Skip hitting the API when there's no client anyways
            if (Listeners == 0)
            {
                updateTimer.Change(5000, Timeout.Infinite);
                return;
            }

            var currentPlayback = await Spotify.Player.GetCurrentPlayback();

            if (currentPlayback == null || currentPlayback.Item == null) // move this to individual checks on trackers
            {
                wsServer.WebSocketServices[wsServiceName].Sessions.Broadcast("0");
                updateTimer.Change(RefreshInterval, Timeout.Infinite);
                return;
            }

            ContextUpdated?.Invoke(currentPlayback);

            LastPlayingContext = currentPlayback;
            updateTimer.Change(LastPlayingContext.Item != null ?
                Math.Min(RefreshInterval, LastPlayingContext.Item.GetDuration() - LastPlayingContext.ProgressMs + 1000) : RefreshInterval,
                Timeout.Infinite);
        }

        internal static void Stop()
        {
            updateTimer.Change(Timeout.Infinite, Timeout.Infinite);
            Task.Run(async () =>
            {
                await Task.Delay(5000);
                wsServer.Stop();
                wsServer = null;
            });
        }

        private static async Task gainAuthorization()
        {
            SpotifyStatus.Msg("Resetting client available");
            spotifyClientAvailable.Reset();
            SpotifyStatus.Msg("requesting auth");

            var code = await SpotifyAuthorization.RequestAuthorization();

            var tokenResponse = await oAuthClient.RequestToken(
              new AuthorizationCodeTokenRequest(
                SpotifyStatus.Config.GetValue(SpotifyStatus.ClientId), SpotifyStatus.Config.GetValue(SpotifyStatus.ClientSecret), code, new Uri("http://localhost:5000/callback")
              )
            );

            SpotifyStatus.Config.Set(SpotifyStatus.RefreshToken, tokenResponse.RefreshToken);
            SpotifyStatus.Config.Save(true);
            accessExpiry = DateTime.Now + TimeSpan.FromSeconds(tokenResponse.ExpiresIn);

            Spotify = new SpotifyClient(tokenResponse.AccessToken);

            spotifyClientAvailable.Set();
        }

        private static async void handleAuthorization(object _ = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SpotifyStatus.Config.GetValue(SpotifyStatus.RefreshToken)))
                {
                    // Get authorization if no refresh token can be loaded
                    SpotifyStatus.Msg("Attempting to gain auth");
                    await gainAuthorization();
                }
                else
                {
                    // Try using the refresh token for a new access token or get new auth
                    SpotifyStatus.Msg("Attempting to refresh auth");
                    await refreshAuthorization();
                }

                // Set new timer to refresh the access token before it expires
                // Wait until the token expires in two minutes
                var refreshAt = accessExpiry - TimeSpan.FromMinutes(2);

                authTimer?.Dispose();
                authTimer = new AbsoluteTimer.AbsoluteTimer(refreshAt, handleAuthorization, null);
            }
            catch (HttpRequestException)
            {
                SpotifyStatus.Msg("Failed to get auth. Retrying in a minute");
                Task.Run(async () =>
                {
                    await Task.Delay(60000);
                    handleAuthorization();
                });
            }
        }

        private static async Task refreshAuthorization()
        {
            spotifyClientAvailable.Reset();

            try
            {
                var refreshResponse = await oAuthClient.RequestToken(
                        new AuthorizationCodeRefreshRequest(
                                SpotifyStatus.Config.GetValue(SpotifyStatus.ClientId),
                                SpotifyStatus.Config.GetValue(SpotifyStatus.ClientSecret),
                                SpotifyStatus.Config.GetValue(SpotifyStatus.RefreshToken)));

                accessExpiry = DateTime.Now + TimeSpan.FromSeconds(refreshResponse.ExpiresIn);

                if (!string.IsNullOrWhiteSpace(refreshResponse.RefreshToken))
                {
                    SpotifyStatus.Config.Set(SpotifyStatus.RefreshToken, refreshResponse.RefreshToken);
                    SpotifyStatus.Config.Save(true);
                }

                Spotify = new SpotifyClient(refreshResponse.AccessToken);

                spotifyClientAvailable.Set();
            }
            catch (APIException)
            {
                // Get new authorization if refresh fails
                SpotifyStatus.Msg("Failed to refresh, attempting gaining auth");
                await gainAuthorization();
            }
        }

        public static event Action<CurrentlyPlayingContext> ContextUpdated;
    }
}