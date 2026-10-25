using System;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace Compile.Client.Platform
{
    // Getting a block of text from the player: in the browser, a page dialog to paste into or pick a file
    // with (the game can't read the browser's clipboard); elsewhere, the clipboard's contents. Poll() each
    // frame until it calls back.
    public static class TextDialog
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void CompileOpenTextDialog(string title, string hint);

        [DllImport("__Internal")]
        static extern int CompileTextDialogState();

        [DllImport("__Internal")]
        static extern string CompileTextDialogText();
#endif

        static Action<string> _onDone;

        public static bool Open => _onDone != null;

        // onDone gets the text, or null if the player cancelled.
        public static void Ask(string title, string hint, Action<string> onDone)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _onDone = onDone;
            WebGLInput.captureAllKeyboardInput = false;     // let the page's text box have the keys
            CompileOpenTextDialog(title, hint);
#else
            onDone(GUIUtility.systemCopyBuffer);
#endif
        }

        public static void Poll()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_onDone == null)
                return;
            var state = CompileTextDialogState();
            if (state == 0)
                return;
            var done = _onDone;
            _onDone = null;
            WebGLInput.captureAllKeyboardInput = true;
            done(state == 1 ? CompileTextDialogText() : null);
#endif
        }
    }
}
