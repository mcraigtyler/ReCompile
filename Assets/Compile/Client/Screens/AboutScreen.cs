using Compile.Client.App;
using Compile.Client.UI;
using UnityEngine;

namespace Compile.Client.Screens
{
    public sealed class AboutScreen : MenuScreen
    {
        const string Heading = "<color=#E58BFF><b>{0}</b></color>";

        protected override float Width => 1300f;

        protected override void Build(RectTransform panel)
        {
            Ui.Text(panel, ":\\> about", TextStyle.Label);
            Ui.Text(panel, "About", TextStyle.Heading);
            RulesScreen.BuildScrollView(panel, Text);
            Ui.Button(panel, "Back", ButtonStyle.Secondary, () => App.Nav.Go(new TitleScreen()));
        }

        static string Text =>
            $"<b>ReCompile</b> v{Application.version}\n" +
            "An unofficial, fan-made way to play Compile in the browser, at one screen or online with a room code. " +
            "It isn't affiliated with or endorsed by the game's publisher.\n\n" +
            string.Format(Heading, "COMPILE") + "\n" +
            "Compile © 2024 Flat River Group, LLC / Greater Than Games. Game design by Michael Yang. " +
            "The card art and rules belong to their owners. If you enjoy this, buy the game.\n\n" +
            string.Format(Heading, "CARD TEXT") + "\n" +
            "From the community database at github.com/chuckplayer/compile, building on Ryan Scherr's Compile database. " +
            "Rulings follow The Compile Codex.\n\n" +
            string.Format(Heading, "MADE WITH") + "\n" +
            "Unity, with Unity Gaming Services (Relay and Sessions) for online play. " +
            "Fonts: Chakra Petch and Share Tech Mono, both under the SIL Open Font License.\n\n" +
            string.Format(Heading, "SOURCE") + "\n" +
            "github.com/mcraigtyler/ReCompile";
    }
}
