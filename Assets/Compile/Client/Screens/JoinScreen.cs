using Compile.Client.App;
using Compile.Client.UI;
using Compile.Net;
using TMPro;
using UnityEngine;

namespace Compile.Client.Screens
{
    public sealed class JoinScreen : MenuScreen
    {
        TMP_InputField _code;
        ButtonSkin _join;
        TextMeshProUGUI _error;

        bool Joining => App.Session.Status == SessionStatus.Joining;

        protected override void Build(RectTransform panel)
        {
            Ui.Text(panel, ":\\> join --session", TextStyle.Label);
            Ui.Text(panel, "Enter the room code", TextStyle.Heading);
            Ui.Text(panel, "Six letters and numbers, from the player who is hosting.", TextStyle.Small);

            _code = Ui.Input(panel, "ABC234", RoomCode.Length);
            StyleAsCode(_code);
            _code.onValidateInput = (_, _, c) => char.IsLetterOrDigit(c) && c < 128 ? char.ToUpperInvariant(c) : '\0';
            _code.onValueChanged.AddListener(_ => Refresh());
            _code.onSubmit.AddListener(_ => Join());

            _error = Ui.Text(panel, "", TextStyle.Body);
            _error.color = Palette.Danger;
            _error.gameObject.SetActive(false);

            var hint = Ui.Text(panel, "Offline preview: codes starting with X or F show the \"room not found\" and \"room full\" errors.",
                TextStyle.Small);
            hint.fontSize = 24f;

            var row = Ui.Row(panel, 24f);
            Ui.Button(row, "Back", ButtonStyle.Quiet, Back);
            _join = Ui.Button(row, "Join", ButtonStyle.Primary, Join);

            App.Session.Changed += Refresh;
            App.Session.Failed += OnFailed;
            Refresh();
        }

        protected override void OnExit()
        {
            App.Session.Changed -= Refresh;
            App.Session.Failed -= OnFailed;
            base.OnExit();
        }

        static void StyleAsCode(TMP_InputField input)
        {
            foreach (var text in new[] { (TextMeshProUGUI)input.textComponent, (TextMeshProUGUI)input.placeholder })
            {
                text.font = Ui.Theme.monoFont;
                text.fontSize = 64f;
                text.characterSpacing = 20f;
                text.alignment = TextAlignmentOptions.Center;
            }
            input.fontAsset = Ui.Theme.monoFont;
            input.pointSize = 64f;
            Ui.Size(input, height: 128f);
        }

        void Refresh()
        {
            if (App.Session.Status == SessionStatus.InLobby)
            {
                App.Nav.Go(new LobbyScreen());
                return;
            }

            _join.SetLabel(Joining ? "Joining..." : "Join");
            _join.Interactable = !Joining && RoomCode.IsValid(_code.text);
            _code.interactable = !Joining;
        }

        void Join()
        {
            if (!_join.Interactable)
                return;
            _error.gameObject.SetActive(false);
            App.Session.Join(_code.text, App.Profile.DisplayName);
        }

        void OnFailed(SessionError error)
        {
            _error.text = error switch
            {
                SessionError.RoomNotFound => "Room not found. Check the code with the host and try again.",
                SessionError.RoomFull => "That room already has two players.",
                SessionError.InvalidCode => "Room codes are 6 letters and numbers.",
                _ => "Couldn't connect. Check your connection and try again.",
            };
            _error.gameObject.SetActive(true);
            Refresh();
        }

        void Back()
        {
            App.Session.Leave();
            App.Nav.Go(new TitleScreen());
        }
    }
}
