using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    public static void BuildAndroid()
    {
        // Get the scenes from Build Settings
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("No scenes enabled in Build Settings.");
            return;
        }

        // Set up build options
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "build/Android/MobileUO.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        // Perform the build
        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log("Build succeeded: " + summary.totalSize + " bytes");
        }
        else if (summary.result == BuildResult.Failed)
        {
            Debug.LogError("Build failed");
            EditorApplication.Exit(1);
        }
    }
}
