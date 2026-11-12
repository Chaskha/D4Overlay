package com.chaskha.d4overlay.alerts

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import com.chaskha.d4overlay.R
import com.chaskha.d4overlay.data.AlertTrigger
import com.chaskha.d4overlay.i18n.Loc
import com.chaskha.d4overlay.ui.MainActivity
import com.chaskha.d4overlay.ui.formatClock

object Notifications {
    /** Canal sans son : l'appli joue elle-même le son du palier, avec son volume. */
    private const val CHANNEL_ALERTS = "alerts"

    fun createChannel(ctx: Context) {
        val channel = NotificationChannel(CHANNEL_ALERTS, Loc.t("m.notif.channel"), NotificationManager.IMPORTANCE_HIGH)
            .apply {
                setSound(null, null)
                enableVibration(true)
            }
        ctx.getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }

    fun permitted(ctx: Context): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU ||
            ContextCompat.checkSelfPermission(ctx, Manifest.permission.POST_NOTIFICATIONS) ==
            PackageManager.PERMISSION_GRANTED

    fun showAlert(ctx: Context, trigger: AlertTrigger, nowMs: Long) {
        if (!permitted(ctx)) return
        val spawn = trigger.spawn
        val name = Loc.eventName(spawn.kind)
        val title = if (spawn.name != null && spawn.name != spawn.kind.label) "$name — ${spawn.name}" else name
        val minutes = ((spawn.startMs - nowMs + 30_000L) / 60_000L).coerceAtLeast(1)
        val open = PendingIntent.getActivity(
            ctx, 0, Intent(ctx, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE,
        )
        val notification = NotificationCompat.Builder(ctx, CHANNEL_ALERTS)
            .setSmallIcon(R.drawable.ic_notification)
            .setColor(spawn.kind.color.toInt())
            .setContentTitle(title)
            .setContentText(Loc.t("m.notif.text", minutes, formatClock(spawn.startMs)))
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setCategory(NotificationCompat.CATEGORY_REMINDER)
            .setContentIntent(open)
            .setAutoCancel(true)
            .setSilent(true)
            .setTimeoutAfter(maxOf(60_000L, spawn.startMs - nowMs + spawn.kind.activeWindowMs))
            .build()
        try {
            // Un identifiant par évènement : le palier suivant remplace la notification précédente.
            NotificationManagerCompat.from(ctx).notify(spawn.kind.ordinal + 1, notification)
        } catch (_: SecurityException) {
        }
    }
}
