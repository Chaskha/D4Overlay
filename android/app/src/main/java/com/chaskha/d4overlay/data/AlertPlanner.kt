package com.chaskha.d4overlay.data

/** Une alerte à déclencher : le palier [level] de l'apparition [spawn], à l'instant [atMs]. */
data class AlertTrigger(val spawn: Spawn, val level: AlertLevel) {
    val atMs: Long get() = spawn.startMs - level.minutes * 60_000L
}

/**
 * Calcul des alertes, sans rien d'Android (testable seul).
 *
 * Android ne garde qu'UNE alarme programmée pour l'appli : la prochaine. À son réveil, on joue ce qui est dû,
 * puis on programme la suivante. [lastHandledMs] mémorise jusqu'où les alertes ont été traitées : c'est l'équivalent
 * des paliers « déjà tirés » de la version Windows (un palier dépassé à l'ouverture ne sonne jamais).
 */
object AlertPlanner {
    /** Une alerte en retard de plus de 10 min (téléphone en veille profonde, éteint…) est abandonnée. */
    const val MAX_LATE_MS = 10 * 60_000L
    /** Les alertes à moins de 30 s d'écart sont jouées ensemble, au même réveil. */
    const val GROUP_MS = 30_000L

    fun allTriggers(
        schedule: Schedule,
        configs: Map<EventKind, EventAlertConfig>,
        nowMs: Long,
        horizonMs: Long = 24 * 3_600_000L,
    ): List<AlertTrigger> {
        val out = mutableListOf<AlertTrigger>()
        for (kind in EventKind.entries) {
            val cfg = configs[kind] ?: continue
            // Sur mobile, masquer un timer ne coupe pas ses alertes (différence voulue avec Windows).
            if (!cfg.enabled) continue
            val levels = cfg.levels.filter { it.minutes > 0 }.distinctBy { it.minutes }
            if (levels.isEmpty()) continue
            for (spawn in schedule.spawns(kind, nowMs - MAX_LATE_MS, horizonMs + MAX_LATE_MS)) {
                for (level in levels) out += AlertTrigger(spawn, level)
            }
        }
        return out.sortedBy { it.atMs }
    }

    /** Prochaine alerte encore à jouer (strictement après [lastHandledMs], pas trop en retard, évènement pas commencé). */
    fun next(triggers: List<AlertTrigger>, lastHandledMs: Long, nowMs: Long): AlertTrigger? =
        triggers.firstOrNull { isPending(it, lastHandledMs, nowMs) }

    /**
     * Alertes dues maintenant. Si plusieurs paliers du même évènement sont dus (réveil tardif), seul le plus proche
     * de l'évènement est gardé, comme sur Windows (« un seul son : le plus proche »).
     */
    fun due(triggers: List<AlertTrigger>, lastHandledMs: Long, nowMs: Long): List<AlertTrigger> =
        triggers
            .filter { isPending(it, lastHandledMs, nowMs) && it.atMs <= nowMs + GROUP_MS }
            .groupBy { it.spawn.kind to it.spawn.startMs }
            .map { (_, group) -> group.minBy { it.level.minutes } }
            .sortedBy { it.atMs }

    private fun isPending(t: AlertTrigger, lastHandledMs: Long, nowMs: Long): Boolean =
        t.atMs > lastHandledMs && t.atMs >= nowMs - MAX_LATE_MS && t.spawn.startMs > nowMs
}
