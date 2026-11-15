package com.chaskha.d4overlay.alerts

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import com.chaskha.d4overlay.data.AlertPlanner
import com.chaskha.d4overlay.data.AlertTrigger
import com.chaskha.d4overlay.data.ScheduleRepository
import com.chaskha.d4overlay.data.Store
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
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
                // Alertes coupées entre la programmation et le réveil : rien à jouer.
                val due = if (!Store.alertsOn(ctx)) emptyList()
                    else AlertPlanner.due(triggers, Store.lastHandledMs(ctx), now)
                if (due.isNotEmpty()) {
                    Store.setLastHandledMs(ctx, due.maxOf { it.atMs })
                    due.forEach { Notifications.showAlert(ctx, it, now) }
                    // Plusieurs évènements au même réveil : leurs sons à la suite, du plus proche au plus lointain.
                    // Ne pas déranger / vibreur / silencieux : notification seulement (Android décide de l'afficher).
                    val sound = Notifications.soundAllowed(ctx)
                    Log.i("D4Alert", "${due.size} alerte(s), son ${if (sound) "joué" else "coupé (ne pas déranger / silencieux)"}")
                    if (sound) playInSequence(ctx, due.sortedBy { it.spawn.startMs })
                }
            } finally {
                AlertScheduler.reschedule(ctx, prime = false)
                pending.finish()
            }
        }
    }

    /**
     * Joue les sons l'un après l'autre, avec une courte pause. Le réveil n'a qu'environ 10 s en tout :
     * un son qui ne tient plus dans ce temps est coupé ou sauté (sa notification est déjà affichée).
     */
    private suspend fun playInSequence(ctx: Context, triggers: List<AlertTrigger>) {
        val end = System.currentTimeMillis() + SoundPlayer.MAX_PLAY_MS
        triggers.forEachIndexed { i, t ->
            if (i > 0) delay(PAUSE_MS)
            if (!Store.alertsOn(ctx)) return // « Couper les alertes » appuyé pendant la lecture
            val left = end - System.currentTimeMillis()
            if (left < MIN_SOUND_MS) {
                Log.i("D4Alert", "plus le temps : ${triggers.size - i} son(s) sauté(s)")
                return
            }
            Log.i("D4Alert", "son ${i + 1}/${triggers.size} : ${t.spawn.kind} ${t.level.sound}")
            SoundPlayer.play(ctx, t.level.sound, t.level.volume, left)
        }
    }

    private companion object {
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
        const val PAUSE_MS = 400L
        const val MIN_SOUND_MS = 1_000L
    }
}
