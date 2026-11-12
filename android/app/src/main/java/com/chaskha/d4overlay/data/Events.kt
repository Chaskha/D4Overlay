package com.chaskha.d4overlay.data

/**
 * Les trois évènements suivis, comme dans la version Windows (OverlayForm.TrackedEvent).
 * [label] sert de clé dans alerts.json (même format que %AppData%\D4WorldBossOverlay\alerts.json).
 */
enum class EventKind(
    val label: String,
    val locKey: String,
    /** Clés possibles dans la réponse de /api/schedule. */
    val apiKeys: List<String>,
    /** Durée pendant laquelle l'évènement est « actif » après son début (Helltide : 55 min). */
    val activeWindowMs: Long,
    /** Couleur par défaut du timer (OverlayLayout.ColorFor). */
    val color: Long,
) {
    WORLD_BOSS("World Boss", "event.worldboss", listOf("world_boss", "boss", "worldBoss"),
        10_000L, 0xFFFF4500),
    HELLTIDE("Helltide", "event.helltide", listOf("helltide"),
        55 * 60_000L, 0xFF9370DB),
    LEGION("Legion", "event.legion", listOf("legion"),
        10_000L, 0xFF00BFFF);

    companion object {
        fun fromLabel(label: String): EventKind? = entries.firstOrNull { it.label == label }

        /** Ordre d'affichage des timers, celui de l'overlay Windows : World Boss, Legion, Helltide. */
        val displayOrder = listOf(WORLD_BOSS, LEGION, HELLTIDE)
    }
}

/** Une apparition d'évènement. [name] = nom du boss pour le World Boss quand l'API le donne. */
data class Spawn(val kind: EventKind, val startMs: Long, val name: String? = null)

enum class DataSource { API, CACHE, CADENCE }
