package com.chaskha.d4overlay.data

import org.json.JSONArray
import org.json.JSONObject
import org.json.JSONTokener
import java.time.OffsetDateTime

/**
 * Lecture de la réponse de https://helltides.com/api/schedule.
 * Portage de OverlayForm.TryApplyJson & co : même tolérance (enveloppes data/schedule/result/payload,
 * JSON dans une chaîne, recherche en profondeur, dates en secondes, millisecondes ou ISO 8601).
 * Différence : on garde TOUTES les apparitions à venir, pour programmer les alarmes à l'avance.
 */
object ScheduleParser {

    /** true si le texte ressemble à un planning (même test que IsScheduleJson). */
    fun isScheduleJson(json: String): Boolean =
        json.contains("world_boss", ignoreCase = true) ||
            json.contains("helltide", ignoreCase = true) ||
            json.contains("\"legion\"", ignoreCase = true)

    /** Coupe ce qui précède le premier { ou [ (réponse emballée dans du HTML, préfixe de journal…). */
    fun extractJson(raw: String?): String? {
        if (raw.isNullOrBlank()) return null
        val obj = raw.indexOf('{')
        val arr = raw.indexOf('[')
        val i = when {
            obj >= 0 && arr >= 0 -> minOf(obj, arr)
            obj >= 0 -> obj
            arr >= 0 -> arr
            else -> return null
        }
        return raw.substring(i)
    }

    /**
     * Toutes les apparitions trouvées, triées, par évènement. Un évènement absent de la réponse n'a pas d'entrée.
     * Renvoie null si le texte n'est pas un planning exploitable.
     */
    fun parse(raw: String): Map<EventKind, List<Spawn>>? {
        val json = extractJson(raw) ?: return null
        if (!isScheduleJson(json)) return null
        val root = try {
            unwrapRoot(JSONTokener(json).nextValue())
        } catch (e: Exception) {
            return null
        }
        val result = mutableMapOf<EventKind, List<Spawn>>()
        for (kind in EventKind.entries) {
            val spawns = deepFind(root, kind)
            if (!spawns.isNullOrEmpty()) result[kind] = spawns.sortedBy { it.startMs }
        }
        return result.ifEmpty { null }
    }

    private fun unwrapRoot(root: Any?): Any? {
        if (root is String) {
            val inner = root.trim()
            if (inner.startsWith("{") || inner.startsWith("[")) return unwrapRoot(JSONTokener(inner).nextValue())
        }
        if (root is JSONObject) {
            for (wrap in listOf("data", "schedule", "result", "payload")) {
                val inner = root.opt(wrap)
                if (inner is JSONObject || inner is JSONArray) return inner
            }
        }
        return root
    }

    private fun deepFind(el: Any?, kind: EventKind): List<Spawn>? {
        findHere(el, kind)?.let { return it }
        when (el) {
            is JSONObject -> for (key in el.keys()) deepFind(el.opt(key), kind)?.let { return it }
            is JSONArray -> for (i in 0 until el.length()) deepFind(el.opt(i), kind)?.let { return it }
        }
        return null
    }

    private fun findHere(el: Any?, kind: EventKind): List<Spawn>? {
        if (el !is JSONObject) return null
        val data = byCandidates(el, kind.apiKeys) ?: return null
        val items = when (data) {
            is JSONArray -> (0 until data.length()).mapNotNull { data.opt(it) as? JSONObject }
            is JSONObject -> listOf(data)
            else -> emptyList()
        }
        val spawns = items.mapNotNull { item ->
            readStart(item)?.let { Spawn(kind, it, readName(item)) }
        }
        return spawns.ifEmpty { null }
    }

    private fun byCandidates(obj: JSONObject, candidates: List<String>): Any? {
        for (key in candidates) if (obj.has(key)) return obj.opt(key)
        for (name in obj.keys()) {
            val norm = name.replace("-", "_").replace(" ", "_")
            if (candidates.any { it.equals(norm, ignoreCase = true) }) return obj.opt(name)
        }
        return null
    }

    private fun readStart(item: JSONObject): Long? {
        for (key in listOf("timestamp", "expectedTime", "startTime", "start_time", "time", "start")) {
            if (!item.has(key)) continue
            when (val v = item.opt(key)) {
                is Number -> unixToMs(v.toDouble())?.let { return it }
                is String -> {
                    if (v.isBlank()) continue
                    try {
                        return OffsetDateTime.parse(v.trim()).toInstant().toEpochMilli()
                    } catch (_: Exception) {
                    }
                    v.trim().toDoubleOrNull()?.let { n -> unixToMs(n)?.let { return it } }
                }
            }
        }
        return null
    }

    /** Secondes Unix, ou millisecondes si le nombre est trop grand pour des secondes. */
    private fun unixToMs(n: Double): Long? {
        if (n <= 0) return null
        val seconds = if (n > 1e11) (n / 1000.0).toLong() else n.toLong()
        return seconds * 1000L
    }

    private fun readName(item: JSONObject): String? {
        for (key in listOf("boss", "name", "title")) {
            val v = item.opt(key)
            if (v is String && v.isNotBlank()) return v
        }
        return null
    }
}
