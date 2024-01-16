using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpotifyStatus.Standalone
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

    /// <summary>
    /// Command Codes that the panel in game can send to the server.
    /// </summary>
    internal enum SpotifyCommand
    {
        /// <summary>
        /// Pauses or Resumes playback.
        /// </summary>
        TogglePlayback = 0,

        /// <summary>
        /// Skips back to the previous track.
        /// </summary>
        Previous = 1,

        /// <summary>
        /// Skips forward to the next track.
        /// </summary>
        Next = 2,

        /// <summary>
        /// Resends all information.
        /// </summary>
        Refresh = 3,

        /// <summary>
        /// Cycles the repeat state.
        /// </summary>
        CycleRepeat = 4,

        /// <summary>
        /// Toggles shuffle mode.
        /// </summary>
        ToggleShuffle = 5,

        /// <summary>
        /// Changes the playback position.
        /// </summary>
        SeekPlayback = 6,

        /// <summary>
        /// Adds an item to the queue.
        /// </summary>
        QueueItem = 7,

        /// <summary>
        /// Searches Spotify for different items.
        /// </summary>
        Search = 8,
    }
}