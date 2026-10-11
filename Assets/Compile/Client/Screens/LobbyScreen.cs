using System.Collections.Generic;
using Compile.Client.App;
using Compile.Client.UI;
using Compile.Net;
using TMPro;
using UnityEngine;

namespace Compile.Client.Screens
{
    public sealed class LobbyScreen : MenuScreen
    {
        static readonly (FirstPlayerChoice choice, string label)[] FirstPlayerOptions =
        {
            (FirstPlayerChoice.Random, "Random"),
            (FirstPlayerChoice.Host, "Host"),
            (FirstPlayerChoice.Guest, "Guest"),
        };

        TextMeshProUGUI _room;
        RectTransform _players;
        readonly List<ButtonSkin> _firstPlayer = new List<ButtonSkin>();
        TextMeshProUGUI _hint;
        ButtonSkin _ready;
        ButtonSkin _start;

        protected override float Width => 1040f;

        protected override void Build(RectTransform panel)
        {
            _room = Ui.Text(panel, "", TextStyle.Label);
            Ui.Text(panel, "Ready up to start", TextStyle.Heading);

            _players = Ui.Rect("Players", panel);
            Ui.VStack(_players, 12f);

            Ui.Spacer(panel, 4f);
            Ui.Text(panel, "First player", TextStyle.Label);
            var choices = Ui.Row(panel, 16f, "FirstPlayer");
            foreach (var (choice, label) in FirstPlayerOptions)
                _firstPlayer.Add(Ui.Button(choices, label, ButtonStyle.Secondary, () => App.Session.SetFirstPlayer(choice)));

            _hint = Ui.Text(panel, "", TextStyle.Small, TextAlignmentOptions.Center);

            var row = Ui.Row(panel, 24f);
            Ui.Button(row, "Leave", ButtonStyle.Quiet, Leave);
            _ready = Ui.Button(row, "I'm ready", ButtonStyle.Secondary, ToggleReady);
            _start = Ui.Button(row, "Start", ButtonStyle.Primary, () => App.Session.StartMatch());

            App.Session.Changed += Refresh;
            App.Session.MatchStarted += OnMatchStarted;
            Refresh();
        }

        protected override void OnExit()
        {
            App.Session.Changed -= Refresh;
            App.Session.MatchStarted -= OnMatchStarted;
            base.OnExit();
        }

        void Refresh()
        {
            var session = App.Session;
            var lobby = session.Lobby;
            if (lobby == null)
                return;

            _room.text = $":\\> lobby · room {lobby.RoomCode}";
            RebuildPlayers(lobby);

            for (var i = 0; i < _firstPlayer.Count; i++)
            {
                _firstPlayer[i].Selected = lobby.FirstPlayer == FirstPlayerOptions[i].choice;
                _firstPlayer[i].Interactable = session.IsHost;
            }

            var localReady = lobby.Local?.IsReady ?? false;
            _ready.SetLabel(localReady ? "Not ready" : "I'm ready");

            _start.gameObject.SetActive(session.IsHost);
            _start.Interactable = lobby.AllReady;

            _hint.text = !lobby.IsFull ? "Waiting for an opponent to join."
                : session.IsHost
                    ? lobby.AllReady ? "Everyone's ready. Start when you like." : "The game starts once both players are ready."
                    : localReady ? "Waiting for the host to start." : "The host picks who goes first.";
        }

        void RebuildPlayers(LobbyState lobby)
        {
            for (var i = _players.childCount - 1; i >= 0; i--)
            {
                var child = _players.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }

            foreach (var player in lobby.Players)
                PlayerRow(player.DisplayName + (player.IsLocal ? " (you)" : ""), player.IsHost ? "HOST" : "GUEST",
                    player.IsReady ? "READY" : "NOT READY", player.IsReady ? Palette.Success : Palette.Muted);

            if (!lobby.IsFull)
                PlayerRow("Waiting...", "", "", Palette.Muted);
        }

        void PlayerRow(string name, string role, string status, Color statusColor)
        {
            var row = Ui.Rect("Player", _players);
            Ui.Backdrop(row, Palette.PanelInset, Color.clear, shadow: false);
            var layout = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 20, 20);
            layout.spacing = 24f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;

            var nameText = Ui.Text(row, name, TextStyle.Body, TextAlignmentOptions.MidlineLeft);
            nameText.textWrappingMode = TextWrappingModes.NoWrap;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            Ui.Size(nameText).flexibleWidth = 1f;

            var roleText = Ui.Text(row, role, TextStyle.Label, TextAlignmentOptions.MidlineRight);
            roleText.fontSize = 24f;
            roleText.color = Palette.Cyan;

            var statusText = Ui.Text(row, status, TextStyle.Label, TextAlignmentOptions.MidlineRight);
            statusText.fontSize = 24f;
            statusText.color = statusColor;
            Ui.Size(statusText, width: 190f);
        }

        void ToggleReady()
        {
            var lobby = App.Session.Lobby;
            if (lobby?.Local != null)
                App.Session.SetReady(!lobby.Local.IsReady);
        }

        void OnMatchStarted(MatchStart start)
        {
            App.Match = new MatchInfo(start);
            App.Nav.Go(new DraftScreen());
        }

        void Leave()
        {
            App.Session.Leave();
            App.Nav.Go(new TitleScreen());
        }
    }
}
