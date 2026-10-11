using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace ProjectBootstrap
{
    // Installs (and optionally removes) a fixed set of packages via the PackageManager
    // Client API, headless-safe.
    public static class PackageInstaller
    {
        static readonly string[] PackagesToAdd =
        {
            "com.unity.netcode.gameobjects",
            "com.unity.services.multiplayer",      // Sessions + Relay (WebSockets for WebGL)
            "com.unity.services.authentication",
            "com.unity.multiplayer.playmode",      // run host + guest in one Editor
        };

        // Template defaults this card game doesn't use; trimmed to keep the WebGL build small.
        static readonly string[] PackagesToRemove =
        {
            "com.unity.learn.iet-framework",
            "com.unity.visualscripting",
            "com.unity.collab-proxy",
            "com.unity.2d.animation",
            "com.unity.2d.aseprite",
            "com.unity.2d.psdimporter",
            "com.unity.2d.spriteshape",
            "com.unity.2d.tilemap.extras",
            "com.unity.2d.tilemap",
            "com.unity.timeline",
        };

        const double TimeoutSeconds = 600; // UPM resolution + downloads can be slow

        static AddAndRemoveRequest _request;
        static double _deadline;

        // Invoke with: -executeMethod ProjectBootstrap.PackageInstaller.Install  (NO -quit)
        public static void Install()
        {
            if (PackagesToAdd.Length == 0 && PackagesToRemove.Length == 0)
            {
                Debug.Log("[PackageInstaller] Nothing to do.");
                EditorApplication.Exit(0);
                return;
            }

            Debug.Log($"[PackageInstaller] Adding: {string.Join(", ", PackagesToAdd)}");
            _request = Client.AddAndRemove(packagesToAdd: PackagesToAdd, packagesToRemove: PackagesToRemove);
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (_request == null) return;

            if (!_request.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup > _deadline)
                {
                    EditorApplication.update -= Poll;
                    Debug.LogError("[PackageInstaller] Timed out waiting for UPM.");
                    EditorApplication.Exit(2);
                }
                return;
            }

            EditorApplication.update -= Poll;

            if (_request.Status == StatusCode.Success)
            {
                var names = _request.Result.Select(p => $"{p.name}@{p.version}");
                Debug.Log($"[PackageInstaller] Resolved: {string.Join(", ", names)}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[PackageInstaller] Failed: {_request.Error?.message}");
                EditorApplication.Exit(1);
            }
        }
    }
}
