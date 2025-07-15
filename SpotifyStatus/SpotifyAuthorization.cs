using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;

namespace SpotifyStatus
{
    internal static class SpotifyAuthorization
    {
        private static readonly ManualResetEventSlim authorizationReceived = new ManualResetEventSlim();
        private static string code;
        private static EmbedIOAuthServer server;

        public static async Task<string> RequestAuthorization()
        {
            authorizationReceived.Reset();

            SpotifyStatus.Msg("Starting embedded server");
            server = new EmbedIOAuthServer(new Uri("http://127.0.0.1:5000/callback"), 5000);
            await server.Start();
            SpotifyStatus.Msg("Started server");

            server.AuthorizationCodeReceived += OnAuthorizationCodeReceived;
            server.ErrorReceived += OnErrorReceived;

            var request = new LoginRequest(server.BaseUri, SpotifyStatus.Config.GetValue(SpotifyStatus.ClientId), LoginRequest.ResponseType.Code)
            {
                Scope = new[] { Scopes.UserReadCurrentlyPlaying, Scopes.UserModifyPlaybackState,
                    Scopes.UserReadPlaybackState, Scopes.UserReadPlaybackPosition,
                    Scopes.UserLibraryRead, Scopes.UserReadPrivate }
            };
            SpotifyStatus.Msg("Attempting to open: " + request.ToString());
            BrowserUtil.Open(request.ToUri());

            authorizationReceived.Wait();
            SpotifyStatus.Msg("Received code: " + code);
            return code;
        }

        private static async Task OnAuthorizationCodeReceived(object sender, AuthorizationCodeResponse response)
        {
            code = response.Code;
            authorizationReceived.Set();

            await server.Stop();

            SpotifyStatus.Msg("Received authorization callback, stopped server.");
        }

        private static async Task OnErrorReceived(object sender, string error, string state)
        {
            await server.Stop();

            SpotifyStatus.Msg($"Aborting authorization, error received: {error}");
        }
    }
}