package com.chaskha.d4overlay.data

/**
 * Planning complet : apparitions de l'API (si on en a) prolongées par la cadence de repli.
 * Tout est en millisecondes Unix (UTC) ; l'affichage se fait à l'heure locale du téléphone.
 */
class Schedule(
    private val apiSpawns: Map<EventKind, List<Spawn>>,
    val source: DataSource,
    /** Heure de la dernière réponse valide de l'API (0 = jamais). */
    val fetchedAtMs: Long,
) {
    /**
     * Apparitions encore utiles à [nowMs] (à venir, ou en cours dans leur fenêtre active), jusqu'à [horizonMs] devant.
     * L'API d'abord ; au-delà de sa dernière entrée, la cadence prend le relais.
     */
    fun spawns(kind: EventKind, nowMs: Long, horizonMs: Long = 24 * 3_600_000L): List<Spawn> {
        val until = nowMs + horizonMs
        val fromApi = apiSpawns[kind].orEmpty()
            .filter { it.startMs + kind.activeWindowMs > nowMs && it.startMs <= until }
        val lastApi = apiSpawns[kind]?.maxOfOrNull { it.startMs }
        val cadenceFrom = maxOf(nowMs, (lastApi ?: Long.MIN_VALUE / 2) + Cadence.periodOf(kind) / 2)
        val fromCadence = if (cadenceFrom > until) emptyList()
        else Cadence.upcoming(kind, cadenceFrom, until).filter { lastApi == null || it.startMs > lastApi }
        return fromApi + fromCadence
    }

    /** La prochaine apparition (ou celle en cours). Jamais null : la cadence couvre toujours. */
    fun next(kind: EventKind, nowMs: Long): Spawn =
        spawns(kind, nowMs).firstOrNull() ?: Spawn(kind, Cadence.next(kind, nowMs))

    /** true si l'API couvre encore cet instant pour au moins un évènement (sinon le cache est périmé). */
    fun hasFutureApiData(nowMs: Long): Boolean =
        apiSpawns.values.any { list -> list.any { it.startMs > nowMs } }

    companion object {
        fun cadenceOnly() = Schedule(emptyMap(), DataSource.CADENCE, 0L)
    }
}
