using Compile.Engine.Views;
using Newtonsoft.Json;

namespace Compile.Net
{
    // Everything the two browsers say to each other, as JSON. One flat shape for every kind of message;
    // only the fields a kind uses are written. Types are never named in the JSON, so a message can only
    // ever become these classes, whoever sent it.
    public sealed class MatchMessage
    {
        // Bumped whenever a message changes shape: both players must run the same build.
        public const int ProtocolVersion = 1;

        // Kinds. Guest to host:
        public const string Hello = "hello";        // Name: the guest's display name, Text: their player id (to know them if they come back)
        public const string Ready = "ready";        // Flag
        public const string Move = "move";          // Text: the move (GameRecord.Format)
        public const string Resync = "resync";      // send the whole view again
        // Host to guest:
        public const string Lobby = "lobby";        // LobbyData
        public const string Start = "start";        // StartData
        public const string Update = "update";      // UpdateData
        public const string Rejected = "rejected";  // Text: why the move wasn't allowed
        public const string Failed = "failed";      // Text: the match can't go on
        public const string GameLog = "log";        // Notation: the whole game's notation, once it's over (it replays into the full log)

        [JsonProperty("v")] public int Version { get; set; } = ProtocolVersion;
        [JsonProperty("k")] public string Kind { get; set; }
        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)] public string Text { get; set; }
        [JsonProperty("notation", NullValueHandling = NullValueHandling.Ignore)] public string Notation { get; set; }
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)] public string Name { get; set; }
        [JsonProperty("flag", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool Flag { get; set; }
        [JsonProperty("lobby", NullValueHandling = NullValueHandling.Ignore)] public LobbyMessage LobbyData { get; set; }
        [JsonProperty("start", NullValueHandling = NullValueHandling.Ignore)] public StartMessage StartData { get; set; }
        [JsonProperty("update", NullValueHandling = NullValueHandling.Ignore)] public MatchUpdate UpdateData { get; set; }

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            MaxDepth = 32,
        };

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None, Settings);

        // Null if the text isn't a message of this build's protocol.
        public static MatchMessage FromJson(string json)
        {
            try
            {
                var message = JsonConvert.DeserializeObject<MatchMessage>(json, Settings);
                return message?.Kind == null ? null : message;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    // The lobby as the host keeps it: both names, who's ready, and who goes first.
    public sealed class LobbyMessage
    {
        public string HostName { get; set; }
        public string GuestName { get; set; }       // null until the guest has said hello
        public bool HostReady { get; set; }
        public bool GuestReady { get; set; }
        public FirstPlayerChoice FirstPlayer { get; set; }
    }

    // The match is starting: the host is seat 0, the guest seat 1.
    public sealed class StartMessage
    {
        public int FirstSeat { get; set; }
        public int Seed { get; set; }
        public string HostName { get; set; }
        public string GuestName { get; set; }
    }
}
