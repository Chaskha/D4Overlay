package com.chaskha.d4overlay.data

import android.content.Context
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.net.HttpURLConnection
import java.net.URL

/**
 * Récupération du planning, dans le même ordre que la version Windows :
 * 1. HTTP direct ; 2. navigateur intégré (WebView, seulement appli ouverte : voir ui/WebScheduleFetcher) ;
 * 3. dernier planning en cache ; 4. calcul par cadence.
 */
object ScheduleRepository {
    const val SITE_URL = "https://helltides.com/"
    const val API_URL = "https://helltides.com/api/schedule"
    private const val TAG = "D4Schedule"

    /** Une réponse de l'API de moins de 20 min est affichée comme « API » (au-delà : « cache »). */
    private const val FRESH_MS = 20 * 60_000L
    /** Intervalle d'interrogation, comme sur Windows. */
    const val POLL_INTERVAL_MS = 10 * 60_000L

    /** Planning actuel, sans réseau : cache s'il couvre encore le futur, sinon cadence. */
    fun current(ctx: Context, nowMs: Long = System.currentTimeMillis()): Schedule {
        val json = Store.loadCachedScheduleJson(ctx) ?: return Schedule.cadenceOnly()
        val parsed = ScheduleParser.parse(json) ?: return Schedule.cadenceOnly()
        val fetchedAt = Store.fetchedAtMs(ctx)
        val schedule = Schedule(parsed, DataSource.CACHE, fetchedAt)
        if (!schedule.hasFutureApiData(nowMs)) return Schedule(parsed, DataSource.CADENCE, fetchedAt)
        val source = if (nowMs - fetchedAt < FRESH_MS) DataSource.API else DataSource.CACHE
        return Schedule(parsed, source, fetchedAt)
    }

    fun isStale(ctx: Context, nowMs: Long = System.currentTimeMillis()): Boolean =
        nowMs - Store.fetchedAtMs(ctx) >= POLL_INTERVAL_MS

    /** Enregistre une réponse si c'est bien un planning. true = acceptée. */
    fun accept(ctx: Context, raw: String?): Boolean {
        val json = ScheduleParser.extractJson(raw) ?: return false
        if (ScheduleParser.parse(json) == null) return false
        Store.saveScheduleJson(ctx, json, System.currentTimeMillis())
        return true
    }

    /** Étape 1 : HTTP direct. Échoue souvent quand Cloudflare demande une vérification (d'où le repli WebView). */
    suspend fun fetchViaHttp(ctx: Context): Boolean = withContext(Dispatchers.IO) {
        try {
            val conn = (URL(API_URL).openConnection() as HttpURLConnection).apply {
                connectTimeout = 15_000
                readTimeout = 15_000
                setRequestProperty("User-Agent",
                    "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Mobile Safari/537.36")
                setRequestProperty("Accept", "application/json, text/plain, */*")
                setRequestProperty("Referer", SITE_URL)
            }
            try {
                val code = conn.responseCode
                val body = (if (code in 200..299) conn.inputStream else conn.errorStream)
                    ?.bufferedReader()?.use { it.readText() }.orEmpty()
                Log.i(TAG, "http $code len=${body.length}")
                code in 200..299 && accept(ctx, body)
            } finally {
                conn.disconnect()
            }
        } catch (e: Exception) {
            Log.i(TAG, "http KO: ${e.message}")
            false
        }
    }
}
