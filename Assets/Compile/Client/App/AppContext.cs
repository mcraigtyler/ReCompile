using Compile.Client.Cards;
using Compile.Client.Table;
using Compile.Client.UI;
using Compile.Engine.Cards;
using Compile.Net;
using UnityEngine;

namespace Compile.Client.App
{
    // Shared services for screens.
    public sealed class AppContext
    {
        public Theme Theme;
        public CardArtLibrary Art;
        public CardDatabase Cards;
        public string RulesText;

        public ISessionService Session;
        public FakeSessionService FakeSession;     // null once the real service is used

        public Camera Camera;
        public CameraRig CameraRig;
        public Transform Table;
        public MatView Mat;
        public Hud Hud;
        public Navigator Nav;
        public PlayerProfile Profile;

        public MatchInfo Match;
    }

    public sealed class MatchInfo
    {
        public MatchStart Start { get; }
        public int WinnerSeat { get; set; } = -1;

        // Each player's drafted Protocols in their own left → right order, set when the draft finishes.
        public string[] LocalProtocols { get; set; }
        public string[] OpponentProtocols { get; set; }

        public MatchInfo(MatchStart start) => Start = start;

        public bool LocalWon => WinnerSeat == Start.LocalSeat;
        public bool IsLocal(int seat) => seat == Start.LocalSeat;
        public string NameOf(int seat) => IsLocal(seat) ? Start.LocalName : Start.OpponentName;
    }

    public sealed class PlayerProfile
    {
        const string NameKey = "Compile.DisplayName";
        public const int MaxNameLength = 16;

        public string DisplayName
        {
            get
            {
                var name = PlayerPrefs.GetString(NameKey, "");
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"Player{Random.Range(100, 1000)}";
                    PlayerPrefs.SetString(NameKey, name);
                }
                return name;
            }
            set
            {
                PlayerPrefs.SetString(NameKey, (value ?? "").Trim());
                PlayerPrefs.Save();
            }
        }
    }
}
