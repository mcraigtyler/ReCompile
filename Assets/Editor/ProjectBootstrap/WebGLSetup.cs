using UnityEditor;
using UnityEngine;

namespace ProjectBootstrap
{
    // One-time project configuration for the WebGL target.
    public static class WebGLSetup
    {
        // Invoke with: -executeMethod ProjectBootstrap.WebGLSetup.Apply
        public static void Apply()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);

            PlayerSettings.productName = "Compile";
            PlayerSettings.runInBackground = true; // keep the network connection alive when the tab loses focus
            PlayerSettings.WebGL.template = "PROJECT:Compile";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true; // works on hosts that can't send Brotli headers
            PlayerSettings.WebGL.dataCaching = true;

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            Debug.Log("[WebGLSetup] WebGL target and player settings applied.");
            EditorApplication.Exit(0);
        }
    }
}
