package com.chaskha.d4overlay.data

import java.time.Instant
import kotlin.math.floor

/**
 * Cadences de repli quand l'API ne répond pas (copie de OverlayForm.NextWorldBoss / NextLegion / NextHelltide).
 * Elles peuvent dériver : l'API reste la référence.
 */
object Cadence {
    private val WB_ANCHOR = Instant.parse("2024-01-01T08:30:00Z").toEpochMilli()
    private const val WB_PERIOD = 210 * 60_000L          // 3 h 30
    private val LEGION_ANCHOR = Instant.parse("2026-10-26T05:40:00Z").toEpochMilli()
    private const val LEGION_PERIOD = 25 * 60_000L        // 25 min
    private const val HOUR = 60 * 60_000L

    fun periodOf(kind: EventKind): Long = when (kind) {
        EventKind.WORLD_BOSS -> WB_PERIOD
        EventKind.LEGION -> LEGION_PERIOD
        EventKind.HELLTIDE -> HOUR
    }

    /** Prochaine apparition (ou l'apparition en cours, tant qu'elle est dans sa fenêtre active). */
    fun next(kind: EventKind, nowMs: Long): Long = when (kind) {
        EventKind.HELLTIDE -> {
            val hourStart = nowMs - Math.floorMod(nowMs, HOUR)
            if (nowMs < hourStart + kind.activeWindowMs) hourStart else hourStart + HOUR
        }
        EventKind.WORLD_BOSS -> periodic(WB_ANCHOR, WB_PERIOD, nowMs)
        EventKind.LEGION -> periodic(LEGION_ANCHOR, LEGION_PERIOD, nowMs)
    }

    private fun periodic(anchor: Long, period: Long, nowMs: Long): Long {
        val n = floor((nowMs - anchor).toDouble() / period).toLong()
        var candidate = anchor + n * period
        if (candidate + 10_000L <= nowMs) candidate += period
        return candidate
    }

    /** Apparitions calculées entre [fromMs] (inclus, fenêtre active comprise) et [untilMs]. */
    fun upcoming(kind: EventKind, fromMs: Long, untilMs: Long): List<Spawn> {
        val out = mutableListOf<Spawn>()
        var t = next(kind, fromMs)
        val period = periodOf(kind)
        while (t <= untilMs) {
            out += Spawn(kind, t)
            t += period
        }
        return out
    }
}
