package com.chaskha.d4overlay.alerts

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.widget.Toast
import androidx.core.app.NotificationManagerCompat
import com.chaskha.d4overlay.i18n.Loc

/**
 * Bouton « Couper les alertes » d'une notification : coupe l'interrupteur général sans ouvrir l'appli,
 * retire les notifications affichées et arrête le son en cours. On réactive depuis l'appli.
 */
class MuteReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val ctx = context.applicationContext
        AlertScheduler.setAlertsOn(ctx, false)
        SoundPlayer.stopAll()
        NotificationManagerCompat.from(ctx).cancelAll()
        Toast.makeText(ctx, Loc.t("m.muted"), Toast.LENGTH_LONG).show()
    }
}
