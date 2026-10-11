#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace Compile.Client.Platform
{
    public static class Clipboard
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void CompileCopyToClipboard(string text);
#endif

        // In WebGL builds GUIUtility.systemCopyBuffer doesn't reach the system clipboard, so this goes through the browser.
        public static void Copy(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            CompileCopyToClipboard(text);
#else
            GUIUtility.systemCopyBuffer = text;
#endif
        }
    }
}
