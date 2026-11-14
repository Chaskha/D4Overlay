package com.chaskha.d4overlay.alerts

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import com.chaskha.d4overlay.data.AlertPlanner
import com.chaskha.d4overlay.data.ScheduleRepository
import com.chaskha.d4overlay.data.Store
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

/** Réveil de l'alarme : notifie et joue les alertes dues, puis programme la suivante. */
class AlertReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        val ctx = context.applicationContext
        val pending = goAsync()
        scope.launch {
            try {
                val now = System.currentTimeMillis()
                val triggers = AlertPlanner.allTriggers(ScheduleRepository.current(ctx, now), Store.loadAlerts(ctx), now)
                val due = AlertPlanner.due(triggers, Store.lastHandledMs(ctx), now)
                if (due.isNotEmpty()) {
                    Store.setLastHandledMs(ctx, due.maxOf { it.atMs })
                    due.forEach { Notifications.showAlert(ctx, it, now) }
                    // Plusieurs alertes au même réveil : un seul son, celui de l'évènement le plus proche.
                    // Ne pas déranger / vibreur / silencieux : notification seulement (Android décide de l'afficher).
                    val sound = Notifications.soundAllowed(ctx)
                    Log.i("D4Alert", "${due.size} alerte(s), son ${if (sound) "joué" else "coupé (ne pas déranger / silencieux)"}")
                    if (sound) {
                        val loudest = due.minBy { it.spawn.startMs }
                        SoundPlayer.play(ctx, loudest.level.sound, loudest.level.volume)
                    }
                }
            } finally {
                AlertScheduler.reschedule(ctx, prime = false)
                pending.finish()
            }
        }
    }

    private companion object {
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    }
}
