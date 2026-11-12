package com.chaskha.d4overlay.data

import org.json.JSONArray
import org.json.JSONObject

/** Un palier d'alerte : [minutes] avant l'évènement, joue [sound] à [volume] (0-100). */
data class AlertLevel(val minutes: Int, val sound: String = "exclamation", val volume: Int = 100)

/**
 * Réglages d'un évènement. Même sens que la version Windows :
 * visible = false → timer masqué (sur mobile, ses alertes continuent) ; enabled = false → aucune alerte.
 * [onlyInGame] n'a pas de sens sur téléphone : il est seulement conservé pour rester compatible avec alerts.json.
 */
data class EventAlertConfig(
    val enabled: Boolean = true,
    val visible: Boolean = true,
    val onlyInGame: Boolean = false,
    val levels: List<AlertLevel> = emptyList(),
)

/** Lecture / écriture au format de alerts.json (clés « World Boss », « Helltide », « Legion », propriétés en PascalCase). */
object AlertConfigJson {

    /** Mêmes valeurs par défaut que ConfigStore.CreateDefaults. */
    fun defaults(): Map<EventKind, EventAlertConfig> = mapOf(
        EventKind.WORLD_BOSS to EventAlertConfig(levels = listOf(
            AlertLevel(8, "asterisk"), AlertLevel(4, "exclamation"), AlertLevel(2, "hand"))),
        EventKind.HELLTIDE to EventAlertConfig(levels = listOf(
            AlertLevel(8, "asterisk"), AlertLevel(4, "exclamation"), AlertLevel(2, "hand"))),
        EventKind.LEGION to EventAlertConfig(levels = listOf(
            AlertLevel(4, "exclamation"), AlertLevel(2, "hand"))),
    )

    fun parse(json: String): Map<EventKind, EventAlertConfig> {
        val result = defaults().toMutableMap()
        val root = try { JSONObject(json) } catch (_: Exception) { return result }
        for (kind in EventKind.entries) {
            val o = root.optJSONObject(kind.label) ?: continue
            val levels = mutableListOf<AlertLevel>()
            val arr = o.optJSONArray("Levels")
            if (arr != null) {
                for (i in 0 until arr.length()) {
                    val l = arr.optJSONObject(i) ?: continue
                    levels += AlertLevel(
                        minutes = l.optInt("Minutes", 0),
                        sound = l.optString("Sound", "exclamation"),
                        volume = l.optInt("Volume", 100).coerceIn(0, 100),
                    )
                }
            }
            result[kind] = EventAlertConfig(
                enabled = o.optBoolean("Enabled", true),
                visible = o.optBoolean("Visible", true),
                onlyInGame = o.optBoolean("OnlyInGame", false),
                levels = levels,
            )
        }
        return result
    }

    fun toJson(map: Map<EventKind, EventAlertConfig>): String {
        val root = JSONObject()
        for (kind in EventKind.entries) {
            val cfg = map[kind] ?: continue
            val levels = JSONArray()
            cfg.levels.forEach { l ->
                levels.put(JSONObject().put("Minutes", l.minutes).put("Sound", l.sound).put("Volume", l.volume))
            }
            root.put(kind.label, JSONObject()
                .put("Enabled", cfg.enabled)
                .put("Visible", cfg.visible)
                .put("OnlyInGame", cfg.onlyInGame)
                .put("Levels", levels))
        }
        return root.toString(2)
    }
}
