using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpotifyStatus.Standalone
{
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    internal class Line
    {
        [JsonProperty("words")]
        private readonly string _words;

        [JsonProperty("endTimeMs")]
        public int EndTime { get; }

        [JsonProperty("startTimeMs")]
        public int StartTime { get; }

        [JsonProperty("syllables")]
        public string[] Syllables { get; }

        public string Words => string.IsNullOrWhiteSpace(_words) ? "♪" : _words;

        [JsonConstructor]
        public Line(int startTime, int endTime, string words, string[] syllables)
        {
            StartTime = startTime;
            EndTime = endTime;
            _words = words;
            Syllables = syllables;
        }

        public override string ToString()
            => $"{StartTime.ToString(CultureInfo.InvariantCulture)}|{EndTime.ToString(CultureInfo.InvariantCulture)}|{Words}";
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    internal class SongLyrics
    {
        [JsonProperty("error")]
        public bool Error { get; }

        [JsonProperty("lines")]
        public Line[] Lines { get; }

        [JsonProperty("syncType")]
        public string SyncType { get; }

        [JsonConstructor]
        public SongLyrics(bool error, string syncType, Line[] lines)
        {
            Error = error;
            Lines = lines;
            SyncType = syncType;
        }
    }
}