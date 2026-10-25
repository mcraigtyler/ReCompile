using System;
using System.IO;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using Compile.Engine;
using UnityEngine;

namespace Compile.Client.Platform
{
    // Saving a game's log (GameRecord, with the game's notation first) to share in a bug report: a download
    // in the browser, a file under Logs/Games/ in the Editor (the persistent data folder in other builds), or
    // the clipboard. The notation (GameNotation) on its own can be copied too.
    public static class GameLog
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void CompileDownloadText(string fileName, string text);
#endif

        // The header lines saying where the game was played.
        public static void Describe(GameRecord record, string mode)
        {
            record.SetInfo("build", $"{Application.version} (Unity {Application.unityVersion})");
            record.SetInfo("platform", Application.platform.ToString());
            record.SetInfo("mode", mode);
            record.SetInfo("no-text-yet", string.Join(", ", Compile.Engine.Cards.MainSet1.NotYetWritten));
        }

        // Saves the log and returns a message saying where it went.
        public static string Save(Game game)
        {
            var text = Text(game);
            var name = $"compile-log-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt";
#if UNITY_WEBGL && !UNITY_EDITOR
            CompileDownloadText(name, text);
            return $"Game log downloaded: {name}";
#else
            var folder = Application.isEditor
                ? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Logs", "Games")
                : Path.Combine(Application.persistentDataPath, "GameLogs");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            File.WriteAllText(path, text);
            Debug.Log($"Game log saved to {path}");
            return $"Game log saved to {path}";
#endif
        }

        public static string Copy(Game game)
        {
            Clipboard.Copy(Text(game));
            return "Game log copied. Paste it into a message to share it.";
        }

        public static string CopyNotation(Game game)
        {
            if (!GameNotation.TryWrite(game, out var notation, out var error))
                return $"Couldn't write the game notation: {error}";
            Clipboard.Copy(notation);
            return "Game notation copied.";
        }

        static string Text(Game game)
        {
            var record = game.Record;
            record.SetInfo("saved", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            // The log must still save if the notation can't be written (that's a bug to report too).
            var notation = GameNotation.TryWrite(game, out var text, out var error) ? text : $"; The notation couldn't be written: {error}{Environment.NewLine}";
            return record.ToText(notation);
        }
    }
}
