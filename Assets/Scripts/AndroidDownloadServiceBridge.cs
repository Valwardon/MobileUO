// AndroidDownloadServiceBridge.cs
// ================================
// Unity C# bridge that starts, updates, and stops the Android foreground
// service (DownloadForegroundService.java) during shard file downloads.
//
// On non-Android platforms (iOS, Editor, etc.) all calls are no-ops so the
// rest of the codebase does not need any platform guards.
//
// Usage:
//   AndroidDownloadServiceBridge.StartService(totalFiles, serverName);
//   AndroidDownloadServiceBridge.UpdateProgress(downloaded, total, currentFile);
//   AndroidDownloadServiceBridge.StopService();

using UnityEngine;

public static class AndroidDownloadServiceBridge
{
#if UNITY_ANDROID && !UNITY_EDITOR
    private const string ServiceClassName =
        "com.mobileuo.mobileuo.DownloadForegroundService";

    // Cached JNI references – obtained once and reused to avoid per-call overhead.
    private static AndroidJavaClass  _serviceClass;
    private static AndroidJavaObject _unityActivity;

    private static bool _initialised;

    // -----------------------------------------------------------------------
    // Initialisation
    // -----------------------------------------------------------------------
    private static bool EnsureInitialised()
    {
        if (_initialised) return true;

        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            _unityActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            _serviceClass  = new AndroidJavaClass(ServiceClassName);
            _initialised   = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[AndroidDownloadServiceBridge] Initialisation failed: {ex.Message}");
            return false;
        }

        return true;
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Start the foreground service.  Call this just before the first file
    /// download begins.
    /// </summary>
    /// <param name="totalFiles">Total number of shard files to download.</param>
    /// <param name="serverName">Human-readable shard / server name shown in
    ///   the notification.</param>
    public static void StartService(int totalFiles, string serverName)
    {
        if (!EnsureInitialised()) return;

        try
        {
            _serviceClass.CallStatic("startService", _unityActivity, totalFiles, serverName);
            Debug.Log($"[AndroidDownloadServiceBridge] Foreground service started – {totalFiles} files");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[AndroidDownloadServiceBridge] StartService failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Update the notification progress.  Call this each time a file finishes
    /// downloading.
    /// </summary>
    /// <param name="downloaded">Number of files downloaded so far.</param>
    /// <param name="total">Total number of files.</param>
    /// <param name="currentFile">Name of the file currently being downloaded
    ///   (shown in the notification body).</param>
    public static void UpdateProgress(int downloaded, int total, string currentFile)
    {
        if (!EnsureInitialised()) return;

        try
        {
            _serviceClass.CallStatic("updateProgress", _unityActivity, downloaded, total, currentFile);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[AndroidDownloadServiceBridge] UpdateProgress failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Stop the foreground service.  Call this when all downloads finish,
    /// are cancelled, or encounter a fatal error.
    /// </summary>
    public static void StopService()
    {
        if (!_initialised) return;   // nothing to stop if we never started

        try
        {
            _serviceClass.CallStatic("stopService", _unityActivity);
            Debug.Log("[AndroidDownloadServiceBridge] Foreground service stopped");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[AndroidDownloadServiceBridge] StopService failed: {ex.Message}");
        }
        finally
        {
            // Reset so the next download session re-initialises cleanly.
            _serviceClass?.Dispose();
            _unityActivity?.Dispose();
            _serviceClass  = null;
            _unityActivity = null;
            _initialised   = false;
        }
    }

#else
    // -----------------------------------------------------------------------
    // Stub implementations for non-Android / Editor builds
    // -----------------------------------------------------------------------
    public static void StartService(int totalFiles, string serverName)
    {
        Debug.Log($"[AndroidDownloadServiceBridge] (stub) StartService – {totalFiles} files, server: {serverName}");
    }

    public static void UpdateProgress(int downloaded, int total, string currentFile)
    {
        // Intentionally silent in editor/non-Android to avoid log spam.
    }

    public static void StopService()
    {
        Debug.Log("[AndroidDownloadServiceBridge] (stub) StopService");
    }
#endif
}
