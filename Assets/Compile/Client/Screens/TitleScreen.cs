using Compile.Client.App;
using Compile.Client.UI;
using TMPro;
using UnityEngine;

namespace Compile.Client.Screens
{
    public sealed class TitleScreen : MenuScreen
    {
        TMP_InputField _name;
        ButtonSkin _host;
        ButtonSkin _join;

        string EnteredName => _name.text.Trim();

        protected override void Build(RectTransform panel)
        {
            Ui.Text(panel, ":\\> compile.exe --new-session", TextStyle.Label);
            Ui.Text(panel, "COMPILE", TextStyle.Title);
            Ui.Text(panel, "Two rogue AIs race to compile three Protocols and rewrite reality in their image.", TextStyle.Small);
            Ui.Spacer(panel, 8f);

            Ui.Text(panel, "Display name", TextStyle.Label);
            _name = Ui.Input(panel, "Your name", PlayerProfile.MaxNameLength);
            _name.text = App.Profile.DisplayName;
            _name.onValueChanged.AddListener(_ => Refresh());
            _name.onEndEdit.AddListener(_ => SaveName());

            var row = Ui.Row(panel, 24f);
            _host = Ui.Button(row, "Host game", ButtonStyle.Primary, () => Continue(new HostScreen()));
            _join = Ui.Button(row, "Join game", ButtonStyle.Secondary, () => Continue(new JoinScreen()));

            Ui.Button(panel, "How to play", ButtonStyle.Quiet, () => App.Nav.Go(new RulesScreen()));

            var version = Ui.Text(panel, $"v{Application.version} · offline preview: opponents are simulated",
                TextStyle.Small, TextAlignmentOptions.Center);
            version.fontSize = 24f;

            Refresh();
        }

        void Refresh()
        {
            var valid = EnteredName.Length > 0;
            _host.Interactable = valid;
            _join.Interactable = valid;
        }

        void SaveName()
        {
            if (EnteredName.Length > 0)
                App.Profile.DisplayName = EnteredName;
        }

        void Continue(TableScreen next)
        {
            SaveName();
            App.Nav.Go(next);
        }
    }
}
