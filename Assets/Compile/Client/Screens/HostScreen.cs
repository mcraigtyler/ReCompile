using Compile.Client.App;
using Compile.Client.Platform;
using Compile.Client.UI;
using Compile.Net;
using TMPro;
using UnityEngine;

namespace Compile.Client.Screens
{
    public sealed class HostScreen : MenuScreen
    {
        TextMeshProUGUI _code;
        TextMeshProUGUI _waiting;
        float _elapsed;
        bool _failed;

        protected override void Build(RectTransform panel)
        {
            Ui.Text(panel, ":\\> host --session", TextStyle.Label);
            Ui.Text(panel, "Share this room code", TextStyle.Heading);
            Ui.Text(panel, "Your opponent enters it on their Join screen.", TextStyle.Small);
            _code = Ui.Text(panel, "------", TextStyle.Code, TextAlignmentOptions.Center);

            var row = Ui.Row(panel, 24f);
            Ui.Button(row, "Cancel", ButtonStyle.Quiet, Cancel);
            Ui.Button(row, "Copy code", ButtonStyle.Secondary, Copy);

            _waiting = Ui.Text(panel, "", TextStyle.Small, TextAlignmentOptions.Center);

            var notice = Ui.Text(panel, InMatch.HostingNotice, TextStyle.Small, TextAlignmentOptions.Center);
            notice.fontSize = 24f;

            App.Session.Changed += OnSessionChanged;
            App.Session.Failed += OnFailed;
            App.Session.Host(App.Profile.DisplayName);
        }

        protected override void OnExit()
        {
            App.Session.Changed -= OnSessionChanged;
            App.Session.Failed -= OnFailed;
            base.OnExit();
        }

        public override void Tick(float deltaTime)
        {
            if (_failed)
                return;
            _elapsed += deltaTime;
            var dots = new string('.', 1 + (int)(_elapsed * 2f) % 3);
            _waiting.text = App.Session.Lobby == null ? "Creating a room" + dots : "Waiting for an opponent" + dots;
        }

        void OnFailed(SessionError error)
        {
            _failed = true;
            _code.text = "------";
            _waiting.text = InMatch.SessionErrorText(error);
            _waiting.color = Palette.Danger;
        }

        void OnSessionChanged()
        {
            var session = App.Session;
            if (session.Status == SessionStatus.InLobby)
                App.Nav.Go(new LobbyScreen());
            else if (session.Lobby != null)
                _code.text = session.Lobby.RoomCode;
        }

        void Copy()
        {
            if (App.Session.Lobby == null)
                return;
            Clipboard.Copy(App.Session.Lobby.RoomCode);
            App.Hud.Toast("Room code copied.");
        }

        void Cancel()
        {
            App.Session.Leave();
            App.Nav.Go(new TitleScreen());
        }
    }
}
