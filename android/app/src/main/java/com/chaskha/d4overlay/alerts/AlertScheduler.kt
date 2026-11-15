package com.chaskha.d4overlay.alerts

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import com.chaskha.d4overlay.data.AlertPlanner
import com.chaskha.d4overlay.data.ScheduleRepository
import com.chaskha.d4overlay.data.Store

/**
 * Programme UNE alarme système : la prochaine alerte. AlertReceiver la joue puis rappelle [reschedule].
 * Alarme exacte si l'utilisateur l'autorise (Android 12+), sinon alarme approximative (quelques minutes de retard possibles).
 */
object AlertScheduler {
    private const val REQUEST_CODE = 4242

    fun canScheduleExact(ctx: Context): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.S ||
            ctx.getSystemService(AlarmManager::class.java).canScheduleExactAlarms()

    /**
     * @param prime true après un changement de réglages ou un redémarrage : les paliers déjà dépassés
     * sont considérés comme traités (ils ne sonneront pas), comme au lancement de l'overlay Windows.
     */
    fun reschedule(ctx: Context, prime: Boolean) {
        val now = System.currentTimeMillis()
        // Premier lancement (rien encore traité) : même règle qu'un amorçage.
        if (prime || Store.lastHandledMs(ctx) == 0L) Store.setLastHandledMs(ctx, maxOf(Store.lastHandledMs(ctx), now))
        // Interrupteur général coupé : aucune alarme programmée.
        val next = if (!Store.alertsOn(ctx)) null else AlertPlanner.next(
            AlertPlanner.allTriggers(ScheduleRepository.current(ctx, now), Store.loadAlerts(ctx), now),
            Store.lastHandledMs(ctx), now,
        )

        val am = ctx.getSystemService(AlarmManager::class.java)
        val pi = pendingIntent(ctx)
        if (next == null) {
            am.cancel(pi)
            Store.setNextAlertAtMs(ctx, 0L)
            return
        }
        val at = maxOf(next.atMs, now + 1_000L)
        if (canScheduleExact(ctx)) am.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pi)
        else am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pi)
        Store.setNextAlertAtMs(ctx, next.atMs)
    }

    /**
     * Active ou coupe toutes les alertes. À la réactivation, les paliers déjà dépassés ne sonnent pas
     * (pas de rafale de rattrapage).
     */
    fun setAlertsOn(ctx: Context, on: Boolean) {
        Store.setAlertsOn(ctx, on)
        reschedule(ctx, prime = true)
    }

    private fun pendingIntent(ctx: Context): PendingIntent =
        PendingIntent.getBroadcast(
            ctx, REQUEST_CODE, Intent(ctx, AlertReceiver::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
}
