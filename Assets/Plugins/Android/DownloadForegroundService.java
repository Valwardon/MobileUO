package com.mobileuo.mobileuo;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.os.Binder;
import android.os.Build;
import android.os.IBinder;
import android.util.Log;

import androidx.core.app.NotificationCompat;

/**
 * DownloadForegroundService
 *
 * An Android foreground service that keeps the MobileUO download process alive
 * even when the user switches to another app or turns off the screen.
 *
 * Unity's UnityWebRequest downloads are driven by the Unity player loop which
 * continues to run as long as the Unity Activity is not destroyed.  However,
 * Android may suspend or kill background activities to reclaim resources.
 * Running as a foreground service prevents this by:
 *   1. Posting a persistent notification so the OS knows the app is doing
 *      meaningful work.
 *   2. Keeping the process priority elevated to "foreground service" level,
 *      which Android will not kill while the notification is visible.
 *
 * Usage (called from C# via AndroidJavaObject):
 *   Start  → DownloadForegroundService.startService(context, totalFiles, serverName)
 *   Update → DownloadForegroundService.updateProgress(context, downloaded, total, currentFile)
 *   Stop   → DownloadForegroundService.stopService(context)
 */
public class DownloadForegroundService extends Service {

    private static final String TAG = "DownloadForegroundSvc";

    // -----------------------------------------------------------------------
    // Notification constants
    // -----------------------------------------------------------------------
    public static final String CHANNEL_ID   = "mobileuo_download_channel";
    public static final String CHANNEL_NAME = "MobileUO Shard Download";
    public static final int    NOTIF_ID     = 1001;

    // -----------------------------------------------------------------------
    // Intent action / extra keys
    // -----------------------------------------------------------------------
    public static final String ACTION_START  = "com.mobileuo.mobileuo.ACTION_START_DOWNLOAD";
    public static final String ACTION_UPDATE = "com.mobileuo.mobileuo.ACTION_UPDATE_DOWNLOAD";
    public static final String ACTION_STOP   = "com.mobileuo.mobileuo.ACTION_STOP_DOWNLOAD";

    public static final String EXTRA_TOTAL_FILES    = "total_files";
    public static final String EXTRA_DOWNLOADED     = "downloaded";
    public static final String EXTRA_CURRENT_FILE   = "current_file";
    public static final String EXTRA_SERVER_NAME    = "server_name";

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------
    private int  totalFiles    = 0;
    private int  downloaded    = 0;
    private String currentFile = "";
    private String serverName  = "";

    private NotificationManager notificationManager;

    // -----------------------------------------------------------------------
    // Binder (not used for cross-process, but required)
    // -----------------------------------------------------------------------
    public class LocalBinder extends Binder {
        DownloadForegroundService getService() { return DownloadForegroundService.this; }
    }
    private final IBinder binder = new LocalBinder();

    @Override
    public IBinder onBind(Intent intent) { return binder; }

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------
    @Override
    public void onCreate() {
        super.onCreate();
        notificationManager = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        createNotificationChannel();
        Log.d(TAG, "Service created");
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent == null) {
            // Restarted by system after being killed – just show the notification
            startForeground(NOTIF_ID, buildNotification());
            return START_STICKY;
        }

        final String action = intent.getAction();
        if (action == null) {
            startForeground(NOTIF_ID, buildNotification());
            return START_STICKY;
        }

        switch (action) {
            case ACTION_START:
                totalFiles  = intent.getIntExtra(EXTRA_TOTAL_FILES, 0);
                downloaded  = 0;
                currentFile = "";
                serverName  = intent.getStringExtra(EXTRA_SERVER_NAME) != null
                              ? intent.getStringExtra(EXTRA_SERVER_NAME) : "";
                Log.d(TAG, "Starting foreground download – " + totalFiles + " files from " + serverName);
                startForeground(NOTIF_ID, buildNotification());
                break;

            case ACTION_UPDATE:
                downloaded  = intent.getIntExtra(EXTRA_DOWNLOADED, downloaded);
                totalFiles  = intent.getIntExtra(EXTRA_TOTAL_FILES, totalFiles);
                currentFile = intent.getStringExtra(EXTRA_CURRENT_FILE) != null
                              ? intent.getStringExtra(EXTRA_CURRENT_FILE) : currentFile;
                notificationManager.notify(NOTIF_ID, buildNotification());
                break;

            case ACTION_STOP:
                Log.d(TAG, "Stopping foreground service");
                stopForeground(true);
                stopSelf();
                break;

            default:
                startForeground(NOTIF_ID, buildNotification());
                break;
        }

        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        super.onDestroy();
        Log.d(TAG, "Service destroyed");
    }

    // -----------------------------------------------------------------------
    // Notification helpers
    // -----------------------------------------------------------------------
    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID,
                    CHANNEL_NAME,
                    NotificationManager.IMPORTANCE_LOW   // silent – no sound/vibration
            );
            channel.setDescription("Shows progress while MobileUO downloads shard files");
            channel.setShowBadge(false);
            notificationManager.createNotificationChannel(channel);
        }
    }

    private Notification buildNotification() {
        // Tapping the notification re-opens the game
        Intent openIntent = getPackageManager()
                .getLaunchIntentForPackage(getPackageName());
        if (openIntent == null) {
            openIntent = new Intent();
        }
        openIntent.setFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP);

        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            flags |= PendingIntent.FLAG_IMMUTABLE;
        }
        PendingIntent pendingIntent = PendingIntent.getActivity(this, 0, openIntent, flags);

        String title = "Downloading shard files" + (serverName.isEmpty() ? "" : " – " + serverName);
        String body;
        if (totalFiles > 0) {
            int pct = (int) ((downloaded / (float) totalFiles) * 100);
            body = downloaded + " / " + totalFiles + " files  (" + pct + "%)";
            if (!currentFile.isEmpty()) {
                body += "\n" + currentFile;
            }
        } else {
            body = "Preparing download…";
        }

        NotificationCompat.Builder builder = new NotificationCompat.Builder(this, CHANNEL_ID)
                .setSmallIcon(android.R.drawable.stat_sys_download)
                .setContentTitle(title)
                .setContentText(body)
                .setStyle(new NotificationCompat.BigTextStyle().bigText(body))
                .setContentIntent(pendingIntent)
                .setOngoing(true)           // cannot be dismissed by the user
                .setOnlyAlertOnce(true)     // don't re-alert on every update
                .setPriority(NotificationCompat.PRIORITY_LOW);

        // Show a determinate progress bar when we know the total
        if (totalFiles > 0) {
            builder.setProgress(totalFiles, downloaded, false);
        } else {
            builder.setProgress(0, 0, true);  // indeterminate
        }

        return builder.build();
    }

    // -----------------------------------------------------------------------
    // Static convenience helpers called from Unity C# via JNI
    // -----------------------------------------------------------------------

    /** Start the foreground service before downloads begin. */
    public static void startService(Context context, int totalFiles, String serverName) {
        Intent intent = new Intent(context, DownloadForegroundService.class);
        intent.setAction(ACTION_START);
        intent.putExtra(EXTRA_TOTAL_FILES, totalFiles);
        intent.putExtra(EXTRA_SERVER_NAME, serverName);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            context.startForegroundService(intent);
        } else {
            context.startService(intent);
        }
    }

    /** Update notification progress.  Call this whenever a file finishes downloading. */
    public static void updateProgress(Context context, int downloaded, int total, String currentFile) {
        Intent intent = new Intent(context, DownloadForegroundService.class);
        intent.setAction(ACTION_UPDATE);
        intent.putExtra(EXTRA_DOWNLOADED, downloaded);
        intent.putExtra(EXTRA_TOTAL_FILES, total);
        intent.putExtra(EXTRA_CURRENT_FILE, currentFile);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            context.startForegroundService(intent);
        } else {
            context.startService(intent);
        }
    }

    /** Stop the foreground service once all downloads are complete or cancelled. */
    public static void stopService(Context context) {
        Intent intent = new Intent(context, DownloadForegroundService.class);
        intent.setAction(ACTION_STOP);
        context.startService(intent);
    }
}
