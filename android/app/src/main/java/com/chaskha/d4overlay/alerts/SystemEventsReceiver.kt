package com.chaskha.d4overlay.alerts

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/** Redémarrage du téléphone, mise à jour de l'appli, changement d'heure ou de permission : on reprogramme. */
class SystemEventsReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val ctx = context.applicationContext
        // Après un redémarrage, les paliers manqués pendant que le téléphone était éteint ne sonnent pas.
        AlertScheduler.reschedule(ctx, prime = intent.action == Intent.ACTION_BOOT_COMPLETED)
        RefreshWorker.ensureScheduled(ctx)
    }
}
