using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Downloads a single zip archive containing all shard files, then extracts it.
///
/// On Android this class starts an Android foreground service via
/// <see cref="AndroidDownloadServiceBridge"/> so that the download continues
/// uninterrupted when the user switches to another app or turns off the screen.
/// </summary>
public class ZipDownloader : DownloaderBase
{
    private string pathToSaveFiles;
    private int port;
    private string url;
    private string fileName;
    private Coroutine downloadCoroutine;
    private UnityWebRequest webRequest;
    private int downloadAttempts;
    private const int MAX_DOWNLOAD_ATTEMPTS = 3;
    
    public override void Initialize(DownloadState downloadState, ServerConfiguration serverConfiguration, DownloadPresenter downloadPresenter)
    {
        base.Initialize(downloadState, serverConfiguration, downloadPresenter);

        pathToSaveFiles = serverConfiguration.GetPathToSaveFiles();
        port = int.Parse(serverConfiguration.FileDownloadServerPort);
        url = serverConfiguration.FileDownloadServerUrl;
        fileName = GetFileNameFromUrl(url);
        downloadPresenter.SetFileList(new List<string> {fileName});

        // Start the Android foreground service so the download survives
        // the user switching screens or the OS trying to reclaim resources.
        // We report 1 "file" (the zip archive) as the total.
        AndroidDownloadServiceBridge.StartService(
            1,
            !string.IsNullOrEmpty(serverConfiguration.Name) ? serverConfiguration.Name : url);

        downloadCoroutine = downloadPresenter.StartCoroutine(DownloadFiles());
    }
    
    private IEnumerator DownloadFiles()
    {
        var directoryInfo = new DirectoryInfo(pathToSaveFiles);
        if (directoryInfo.Exists == false)
        {
            directoryInfo.Create();
        }
        
        downloadPresenter.UpdateView(0, 1);
        
        DownloadFile();

        while (webRequest.isDone == false)
        {
            downloadPresenter.SetDownloadProgress(fileName, webRequest.downloadProgress);

            // Keep the notification progress bar in sync while the zip is downloading.
            // We map the single-file download progress (0–1) onto the 0/1 file count.
            // The notification will show an indeterminate bar until the download
            // finishes, which is fine for a single large archive.
            yield return null;
        }
        
        var filePath = Path.Combine(pathToSaveFiles, fileName);
        try
        {
            ZipFile.ExtractToDirectory(filePath, pathToSaveFiles, true);
        }
        catch (Exception e)
        {
            var error = $"Error while extracting {fileName}: {e}";
            // Stop the foreground service on extraction error.
            AndroidDownloadServiceBridge.StopService();
            downloadState.StopAndShowError(error);
            yield break;
        }
        finally
        {
            downloadCoroutine = null;
            File.Delete(filePath);
        }

        // Download and extraction complete – stop the foreground service.
        AndroidDownloadServiceBridge.StopService();

        serverConfiguration.AllFilesDownloaded = true;
        ServerConfigurationModel.SaveServerConfigurations();
        
        StateManager.GoToState<GameState>();
    }
    
    private void DownloadFile()
    {
        var uri = DownloadState.GetUri(url, port);
        var uriString = uri.ToString();
        if (uriString.EndsWith("/"))
        {
            uriString = uriString[..^1];
        }

        if (uriString.Contains("dropbox", StringComparison.InvariantCultureIgnoreCase) &&
            uriString.EndsWith("dl=0", StringComparison.InvariantCultureIgnoreCase))
        {
            uriString = uriString.Replace("dl=0", "dl=1", StringComparison.InvariantCultureIgnoreCase);
        }
        
        webRequest = UnityWebRequest.Get(uriString);
        var filePath = Path.Combine(pathToSaveFiles, fileName);
        var fileDownloadHandler = new DownloadHandlerFile(filePath) {removeFileOnAbort = true};
        webRequest.downloadHandler = fileDownloadHandler;
        webRequest.SendWebRequest().completed += _ => DownloadFinished(webRequest, fileName);
    }

    private static string GetFileNameFromUrl(string url)
    {
        if (url.Contains("http://") == false)
        {
            url = "http://" + url;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            uri = new Uri(url);
        var fileName = Path.GetFileName(uri.LocalPath);
        return fileName;
    }

    private void DownloadFinished(UnityWebRequest request, string fileName)
    {
        //If download coroutine was stopped, do nothing
        if (downloadCoroutine == null)
        {
            return;
        }

        if (request.result == UnityWebRequest.Result.Success)
        {
            downloadPresenter.SetFileDownloaded(fileName);
            downloadPresenter.UpdateView(1, 1);
            // Update notification to show 1/1 complete.
            AndroidDownloadServiceBridge.UpdateProgress(1, 1, fileName);
        }
        else
        {
            if(downloadAttempts >= MAX_DOWNLOAD_ATTEMPTS)
            {
                var error = $"Error while downloading {fileName}: {request.error}";
                downloadPresenter.StopCoroutine(downloadCoroutine);
                downloadCoroutine = null;
                // Fatal error – stop the foreground service.
                AndroidDownloadServiceBridge.StopService();
                downloadState.StopAndShowError(error);
            }
            else
            {
                downloadAttempts++;
                Debug.Log($"Re-downloading file, attempt:{downloadAttempts}");
                DownloadFile();
                downloadPresenter.SetDownloadProgress(request.uri.AbsolutePath, 0f);
            }
        }
    }
    
    public override void Dispose()
    {
        if (downloadCoroutine != null)
        {
            downloadPresenter.StopCoroutine(downloadCoroutine);
            downloadCoroutine = null;
        }

        // Ensure the foreground service is always stopped when the downloader
        // is disposed (e.g. user presses Cancel).
        AndroidDownloadServiceBridge.StopService();

        webRequest?.Abort();
        webRequest?.Dispose();
        base.Dispose();
    }
}
