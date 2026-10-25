using Compile.Client.Cards;
using Compile.Engine;
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
        public CardCatalog Catalog;         // the cards as the rules engine sees them
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
        public bool Hotseat { get; }                // two people at one screen
        public int WinnerSeat { get; set; } = -1;   // -1 for a draw
        public EndReason EndReason { get; set; }
        public MatchController Controller { get; set; }   // created by the draft
        public ReplayController Replay { get; set; }      // set when watching a loaded game rather than playing

        public MatchInfo(MatchStart start, bool hotseat = false)
        {
            Start = start;
            Hotseat = hotseat;
        }

        // Names by seat (the local player's MatchStart seat is "local"; in hot-seat that's seat 0).
        public string SeatName(int seat) => IsLocal(seat) ? Start.LocalName : Start.OpponentName;

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
