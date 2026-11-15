package com.chaskha.d4overlay.data

import android.content.Context
import java.io.File

/**
 * Données de l'utilisateur, dans le stockage privé de l'appli (équivalent de %AppData%\D4WorldBossOverlay) :
 * alerts.json (même format que sur Windows), schedule.json (dernière réponse de l'API), et quelques préférences.
 */
object Store {
    private const val PREFS = "d4overlay"
    private const val KEY_LANGUAGE = "language"
    private const val KEY_BANNER = "banner"
    private const val KEY_LAST_HANDLED = "alerts.lastHandledMs"
    private const val KEY_FETCHED_AT = "schedule.fetchedAtMs"
    private const val KEY_NEXT_ALERT = "alerts.nextAtMs"
    private const val KEY_ALERTS_ON = "alerts.on"

    private fun prefs(ctx: Context) = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
    private fun alertsFile(ctx: Context) = File(ctx.filesDir, "alerts.json")
    private fun scheduleFile(ctx: Context) = File(ctx.filesDir, "schedule.json")

    // ── alerts.json ──

    fun loadAlerts(ctx: Context): Map<EventKind, EventAlertConfig> {
        val f = alertsFile(ctx)
        return if (f.exists()) AlertConfigJson.parse(f.readText()) else AlertConfigJson.defaults()
    }

    fun saveAlerts(ctx: Context, map: Map<EventKind, EventAlertConfig>) =
        writeAtomic(alertsFile(ctx), AlertConfigJson.toJson(map))

    // ── schedule.json (cache de l'API) ──

    fun loadCachedScheduleJson(ctx: Context): String? =
        scheduleFile(ctx).takeIf { it.exists() }?.readText()

    fun saveScheduleJson(ctx: Context, json: String, fetchedAtMs: Long) {
        writeAtomic(scheduleFile(ctx), json)
        prefs(ctx).edit().putLong(KEY_FETCHED_AT, fetchedAtMs).apply()
    }

    fun fetchedAtMs(ctx: Context): Long = prefs(ctx).getLong(KEY_FETCHED_AT, 0L)

    // ── préférences ──

    /** "" = langue du système. */
    fun language(ctx: Context): String = prefs(ctx).getString(KEY_LANGUAGE, "") ?: ""
    fun setLanguage(ctx: Context, code: String) = prefs(ctx).edit().putString(KEY_LANGUAGE, code).apply()

    /** Bannière d'en-tête : nom du fichier dans assets/banners, sans « .png ». */
    fun banner(ctx: Context): String = prefs(ctx).getString(KEY_BANNER, "metal") ?: "metal"
    fun setBanner(ctx: Context, id: String) = prefs(ctx).edit().putString(KEY_BANNER, id).apply()

    /** Interrupteur général : false = aucune alarme ni notification, quels que soient les réglages des évènements. */
    fun alertsOn(ctx: Context): Boolean = prefs(ctx).getBoolean(KEY_ALERTS_ON, true)
    fun setAlertsOn(ctx: Context, on: Boolean) = prefs(ctx).edit().putBoolean(KEY_ALERTS_ON, on).apply()

    fun lastHandledMs(ctx: Context): Long = prefs(ctx).getLong(KEY_LAST_HANDLED, 0L)
    fun setLastHandledMs(ctx: Context, ms: Long) = prefs(ctx).edit().putLong(KEY_LAST_HANDLED, ms).apply()

    /** Heure de la prochaine alerte programmée (0 = aucune), pour l'affichage. */
    fun nextAlertAtMs(ctx: Context): Long = prefs(ctx).getLong(KEY_NEXT_ALERT, 0L)
    fun setNextAlertAtMs(ctx: Context, ms: Long) = prefs(ctx).edit().putLong(KEY_NEXT_ALERT, ms).apply()

    /** Écriture atomique (fichier temporaire puis renommage), comme ConfigStore côté Windows. */
    private fun writeAtomic(target: File, text: String) {
        val tmp = File(target.parentFile, target.name + ".tmp")
        tmp.writeText(text)
        if (!tmp.renameTo(target)) {
            target.delete()
            tmp.renameTo(target)
        }
    }
}
