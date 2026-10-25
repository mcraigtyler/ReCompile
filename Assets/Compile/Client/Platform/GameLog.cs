using System;
using System.IO;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using Compile.Engine;
using UnityEngine;

namespace Compile.Client.Platform
{
    // Saving a game's log (GameRecord) to share in a bug report: a download in the browser, a file under
    // Logs/Games/ in the Editor (the persistent data folder in other builds), or the clipboard.
    public static class GameLog
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void CompileDownloadText(string fileName, string text);
#endif

        // Saves the log and returns a message saying where it went.
        public static string Save(GameRecord record)
        {
            var text = Text(record);
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

        public static string Copy(GameRecord record)
        {
            Clipboard.Copy(Text(record));
            return "Game log copied. Paste it into a message to share it.";
        }

        static string Text(GameRecord record)
        {
            record.SetInfo("saved", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            return record.ToText();
        }
    }
}
