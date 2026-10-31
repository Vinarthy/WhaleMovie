package com.lollyproject.whalemovie.boot;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.os.Build;
import android.provider.Settings;
import android.util.Log;

/** Requests this application's normal launcher activity after a full system boot. */
public final class BootReceiver extends BroadcastReceiver {
    private static final String TAG = "WhaleMovieBoot";

    @Override
    public void onReceive(Context context, Intent intent) {
        if (intent == null || !Intent.ACTION_BOOT_COMPLETED.equals(intent.getAction())) {
            return;
        }

        String packageName = context.getPackageName();
        Log.i(TAG, "BOOT_RECEIVED package=" + packageName
                + " device=" + Build.MODEL + " sdk=" + Build.VERSION.SDK_INT);

        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q
                    && !Settings.canDrawOverlays(context)) {
                Log.w(TAG, "Background activity launch may be blocked: "
                        + "display-over-other-apps permission is not granted.");
            }

            // Resolve the real app entry point instead of replacing Unity's VR activity.
            Intent launchIntent = context.getPackageManager()
                    .getLaunchIntentForPackage(packageName);
            if (launchIntent == null) {
                Log.e(TAG, "NO_LAUNCHER: no enabled launcher activity for " + packageName);
                return;
            }

            launchIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK
                    | Intent.FLAG_ACTIVITY_RESET_TASK_IF_NEEDED);
            context.startActivity(launchIntent);

            // Android can silently deny background starts even without throwing an exception.
            Log.i(TAG, "START_REQUESTED component=" + launchIntent.getComponent()
                    + "; this does not confirm that the VR activity became visible.");
        } catch (RuntimeException exception) {
            Log.e(TAG, "START_FAILED: the system could not start the application.", exception);
        }
    }
}
