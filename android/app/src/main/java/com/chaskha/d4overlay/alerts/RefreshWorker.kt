package com.chaskha.d4overlay.alerts

import android.content.Context
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import com.chaskha.d4overlay.data.ScheduleRepository
import java.util.concurrent.TimeUnit

/**
 * Actualisation en arrière-plan (appli fermée) : toutes les 30 min environ, Android choisit le moment exact.
 * HTTP direct seulement : le repli par navigateur intégré ne tourne que quand l'appli est ouverte.
 * Si l'API ne répond pas, les alertes continuent sur le cache puis sur la cadence.
 */
class RefreshWorker(ctx: Context, params: WorkerParameters) : CoroutineWorker(ctx, params) {

    override suspend fun doWork(): Result {
        ScheduleRepository.fetchViaHttp(applicationContext)
        AlertScheduler.reschedule(applicationContext, prime = false)
        return Result.success()
    }

    companion object {
        private const val NAME = "schedule-refresh"

        fun ensureScheduled(ctx: Context) {
            val request = PeriodicWorkRequestBuilder<RefreshWorker>(30, TimeUnit.MINUTES)
                .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
                .build()
            WorkManager.getInstance(ctx).enqueueUniquePeriodicWork(NAME, ExistingPeriodicWorkPolicy.KEEP, request)
        }
    }
}
