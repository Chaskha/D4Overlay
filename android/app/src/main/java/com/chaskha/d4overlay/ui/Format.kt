package com.chaskha.d4overlay.ui

import com.chaskha.d4overlay.data.EventKind
import com.chaskha.d4overlay.data.Spawn
import com.chaskha.d4overlay.i18n.Loc
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

private val clock = DateTimeFormatter.ofPattern("HH:mm")
private val clockSeconds = DateTimeFormatter.ofPattern("HH:mm:ss")

/** Heure locale du téléphone, « 14:30 ». */
fun formatClock(ms: Long, seconds: Boolean = false): String =
    Instant.ofEpochMilli(ms).atZone(ZoneId.systemDefault()).format(if (seconds) clockSeconds else clock)

/** Même format que OverlayForm.Fmt : heures totales sur 2 chiffres. */
fun formatCountdown(ms: Long): String {
    val total = (ms / 1000).coerceAtLeast(0)
    return "%02d:%02d:%02d".format(total / 3600, (total / 60) % 60, total % 60)
}

/**
 * Valeur d'un timer, mêmes règles que OverlayForm.RefreshCountdowns : « 01:23:45 » avant le début,
 * pendant la fenêtre active : temps restant seul pour la Helltide, « ACTIF 00:00:07 » pour les autres ;
 * « APPARU » une fois la fenêtre passée.
 */
fun timerValue(spawn: Spawn, nowMs: Long): String {
    val kind: EventKind = spawn.kind
    val remaining = spawn.startMs - nowMs
    if (remaining > 0) return formatCountdown(remaining)
    val since = -remaining
    if (since >= kind.activeWindowMs) return Loc.t("overlay.spawned")
    val left = formatCountdown(kind.activeWindowMs - since)
    // Helltide : active 55 min sur 60, « ACTIF » va de soi (demande d'Arnaud) ; on garde le temps restant seul.
    return if (kind == EventKind.HELLTIDE) left else "${Loc.t("overlay.up")} $left"
}
