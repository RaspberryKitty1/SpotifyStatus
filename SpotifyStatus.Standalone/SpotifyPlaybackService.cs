using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;
using SpotifyAPI.Web;
using System.Text.RegularExpressions;
using System.Globalization;
using SpotifyStatus.Standalone;

namespace SpotifyStatus
{
    internal sealed class SpotifyPlaybackService : WebSocketBehavior
    {
        private static readonly Regex spotifyUriEx = new(@"(?:spotify:|https?:\/\/open\.spotify\.com(\/.*)+?)(episode|track)[:\/]([0-9A-z]+)");

        private readonly List<ChangeTracker> contextChangeTrackers;
        private CurrentlyPlayingContext lastPlayingContext;
        private Dictionary<SpotifyResource, int> lastQueue = new();

        public SpotifyPlaybackService()
        {
            contextChangeTrackers = new List<ChangeTracker>()
            {
                new ChangeTracker(nC => handleChangedResource(SpotifyInfo.Playable, nC.Item.GetResource()),
                    (oC, nC) => !oC.Item.GetResource().Equals(nC.Item.GetResource())),

                new ChangeTracker(nC => handleChangedResources(SpotifyInfo.Creator, nC.Item.GetCreators()),
                    (oC, nC) =>
                    {
                        var oCreators = oC.Item.GetCreators();
                        var nCreators = nC.Item.GetCreators();

                        return oCreators.Count() != nCreators.Count()
                        || oCreators.Except(nCreators).Any()
                        || nCreators.Except(oCreators).Any();
                    }),

                new ChangeTracker(nC => sendMessage(SpotifyInfo.Cover, nC.Item.GetCover()),
                    (oC, nC) => oC.Item.GetCover() != nC.Item.GetCover()),

                new ChangeTracker(nC => handleChangedResource(SpotifyInfo.Grouping, nC.Item.GetGrouping()),
                    (oC, nC) => !oC.Item.GetGrouping().Equals(nC.Item.GetGrouping())),

                new ChangeTracker(nC => handleChangedInt(SpotifyInfo.Progress, nC.ProgressMs),
                    (oC, nC) => oC.ProgressMs != nC.ProgressMs),

                new ChangeTracker(nC => handleChangedInt(SpotifyInfo.Duration, nC.Item.GetDuration()),
                    (oC, nC) => oC.Item.GetDuration() != nC.Item.GetDuration()),

                new ChangeTracker(nC => handleChangedBool(SpotifyInfo.IsPlaying, nC.IsPlaying),
                    (oC, nC) => oC.IsPlaying != nC.IsPlaying),

                new ChangeTracker(nC => handleChangedInt(SpotifyInfo.RepeatState, (int)SpotifyHelper.GetState(nC.RepeatState)),
                    (oC, nC) => oC.RepeatState != nC.RepeatState),

                new ChangeTracker(nC => handleChangedBool(SpotifyInfo.IsShuffled, nC.ShuffleState),
                    (oC, nC) => oC.ShuffleState != nC.ShuffleState),
            };
        }

        protected override void OnClose(CloseEventArgs e)
        {
            SpotifyTracker.PlayingContextUpdated -= sendOutContextUpdates;
            SpotifyTracker.QueueUpdated -= SendOutQueueUpdates;

            Console.WriteLine($"A connection was closed! Reason: {e.Reason}, Code: {e.Code}");
        }

        protected override async void OnMessage(MessageEventArgs e)
        {
            /*
             * List of the prefixes/commands received from Neos via WebSockets:
             * 0 - Pause/Resume
             * 1 - Previous track
             * 2 - Next track
             * 3 - Re-request info
             * 4 - Repeat status change
             * 5 - Toggle shuffle
             * 6 - Seek to position
             * 7 - Add Item to Queue
            */
            //I'm not going to simply get the playback data on every message received
            //That would be a lot of unneeded requests
            //and I'm already getting ratelimited

            var dividerIndex = e.Data.IndexOf('|');

            int dataStart;
            SpotifyCommand commandCode;

            if (dividerIndex >= 0 && int.TryParse(e.Data[..dividerIndex], out var result))
            {
                commandCode = (SpotifyCommand)result;
                dataStart = dividerIndex + 1;
            }
            else if (int.TryParse(e.Data[..1], out result))
            {
                dataStart = 1;
                commandCode = (SpotifyCommand)result;
            }
            else
            {
                Console.WriteLine("Invalid Command received!");
                Console.WriteLine(e.Data);

                return;
            }

            var updateQueue = false;
            var commandData = e.Data[dataStart..];

            Console.WriteLine($"Command {commandCode} received, data: {commandData}");

            try
            {
                switch (commandCode)
                {
                    case SpotifyCommand.TogglePlayback:
                        if (lastPlayingContext != null && lastPlayingContext.IsPlaying)
                        {
                            await SpotifyTracker.Spotify.Player.PausePlayback();
                        }
                        else
                        {
                            await SpotifyTracker.Spotify.Player.ResumePlayback();
                        }
                        break;

                    case SpotifyCommand.Previous:
                        updateQueue = true;
                        await SpotifyTracker.Spotify.Player.SkipPrevious();
                        break;

                    case SpotifyCommand.Next:
                        updateQueue = true;
                        await SpotifyTracker.Spotify.Player.SkipNext();
                        break;

                    case SpotifyCommand.Refresh:
                        updateQueue = true;
                        lastPlayingContext = null;
                        break;

                    case SpotifyCommand.CycleRepeat:
                        if (lastPlayingContext != null)
                        {
                            var targetState = SpotifyHelper.GetState(lastPlayingContext.RepeatState).Next();

                            if (int.TryParse(commandData, out var repeatNum))
                                targetState = (PlayerSetRepeatRequest.State)repeatNum;

                            var repeatRequest = new PlayerSetRepeatRequest(targetState);
                            await SpotifyTracker.Spotify.Player.SetRepeat(repeatRequest);
                        }
                        break;

                    case SpotifyCommand.ToggleShuffle:
                        var playback2 = await SpotifyTracker.Spotify.Player.GetCurrentPlayback();
                        if (playback2 is null)
                        {
                            Console.WriteLine("No playback detected!");
                            break;
                        }

                        updateQueue = true;
                        var doShuffle = !playback2.ShuffleState;
                        var shuffleRequest = new PlayerShuffleRequest(doShuffle);
                        await SpotifyTracker.Spotify.Player.SetShuffle(shuffleRequest);
                        break;

                    case SpotifyCommand.SeekPlayback:
                        var seekRequest = new PlayerSeekToRequest(int.Parse(commandData));
                        await SpotifyTracker.Spotify.Player.SeekTo(seekRequest);
                        break;

                    case SpotifyCommand.QueueItem:
                        var match = spotifyUriEx.Match(commandData);
                        if (!match.Success)
                            break;

                        updateQueue = true;
                        var addRequest = new PlayerAddToQueueRequest($"spotify:{match.Groups[2]}:{match.Groups[3]}");
                        await SpotifyTracker.Spotify.Player.AddToQueue(addRequest);

                        // Send parse / add confirmation?
                        // Maybe general toast command
                        break;

                    default:
                        Console.WriteLine("Unknown Command");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception caught: {ex}");
            }

            await Task.Delay(500);

            SpotifyTracker.UpdatePlaybackAsync();

            if (updateQueue)
                SpotifyTracker.UpdateQueueAsync();
        }

        protected override void OnOpen()
        {
            SpotifyTracker.PlayingContextUpdated += sendOutContextUpdates;
            SpotifyTracker.QueueUpdated += SendOutQueueUpdates;

            Console.WriteLine("New connection opened, list of sessions:");
            Console.WriteLine($"    {string.Join(Environment.NewLine + "    ", Sessions.ActiveIDs)}");

            Task.Run(() => SpotifyTracker.UpdatePlaybackAsync());
        }

        private void handleChangedBool(SpotifyInfo info, bool value)
        {
            sendMessage(info, value.ToString(CultureInfo.InvariantCulture));
        }

        private void handleChangedInt(SpotifyInfo info, int value)
        {
            sendMessage(info, value.ToString(CultureInfo.InvariantCulture));
        }

        private void handleChangedResource(SpotifyInfo info, SpotifyResource resource)
        {
            sendMessage(info, $"{resource.Uri}|{resource.Name}");
            //sendMessage(info | SpotifyInfo.ResourceUri, resource.Uri);
        }

        private void handleChangedResources(SpotifyInfo info, IEnumerable<SpotifyResource> resources)
        {
            sendMessage(info, string.Join("\n", resources.Select(res => $"{res.Uri}|{res.Name}")));
            //sendMessage(info | SpotifyInfo.ResourceUri, string.Join(", ", resources.Select(res => res.Uri)));
        }

        private void sendMessage(SpotifyInfo info, string data = "")
        {
            var message = $"{info.ToUpdateInt()}|{data}";

            Console.WriteLine($"Sending {message} to {ID}");
            Task.Run(() => Send(message));
        }

        private void sendOutContextUpdates(CurrentlyPlayingContext newPlayingContext)
        {
            foreach (var changeTracker in contextChangeTrackers.Where(changeTracker => lastPlayingContext == null || changeTracker.TestChanged(lastPlayingContext, newPlayingContext)))
                changeTracker.InvokeEvent(newPlayingContext);

            lastPlayingContext = newPlayingContext;
        }

        private void SendOutQueueUpdates(List<IPlayableItem> updatedQueue)
        {
            if (updatedQueue.Count == 0)
            {
                lastQueue.Clear();
                sendMessage(SpotifyInfo.ClearQueue);

                return;
            }

            for (var i = 0; i < updatedQueue.Count; ++i)
            {
                var item = updatedQueue[i];
            }

            var newQueue = updatedQueue.Select((playable, idx) => (Resource: playable.GetResource(), Data: (Playable: playable, Index: idx)))
                .ToDictionary(item => item.Resource, item => item.Data);

            // deleted or moved
            foreach (var lastQueueItem in lastQueue)
            {
                if (newQueue.TryGetValue(lastQueueItem.Key, out var newData))
                {
                    sendMessage(SpotifyInfo.QueuePositionShift,
                        $"{lastQueueItem.Value.ToString(CultureInfo.InvariantCulture)}|{newData.Index.ToString(CultureInfo.InvariantCulture)}");
                }
                else
                {
                    sendMessage(SpotifyInfo.ClearQueue, lastQueueItem.Value.ToString(CultureInfo.InvariantCulture));
                }
            }

            foreach (var newQueueItem in newQueue)
            {
                // Already handled by position shifts on other loop
                if (lastQueue.ContainsKey(newQueueItem.Key))
                    continue;

                var item = newQueueItem.Value.Playable;

                handleChangedResource(SpotifyInfo.QueuedPlayable, item.GetResource());
                handleChangedResources(SpotifyInfo.QueuedCreator, item.GetCreators());
                sendMessage(SpotifyInfo.QueuedCover, item.GetCover());
                handleChangedResource(SpotifyInfo.QueuedGrouping, item.GetGrouping());
                handleChangedInt(SpotifyInfo.QueuedPosition, newQueueItem.Value.Index);

                sendMessage(SpotifyInfo.QueuedComplete);
            }

            sendMessage(SpotifyInfo.QueueUpdated);
        }

        private sealed class ChangeTracker
        {
            public Action<CurrentlyPlayingContext> InvokeEvent { get; }

            public Func<CurrentlyPlayingContext, CurrentlyPlayingContext, bool> TestChanged { get; }

            public ChangeTracker(Action<CurrentlyPlayingContext> invokeEvent, Func<CurrentlyPlayingContext, CurrentlyPlayingContext, bool> testChanged)
            {
                InvokeEvent = invokeEvent;
                TestChanged = testChanged;
            }
        }
    }
}