package com.chaskha.d4overlay.i18n

import android.content.Context
import com.chaskha.d4overlay.data.EventKind
import com.chaskha.d4overlay.data.Store
import org.json.JSONObject
import java.util.Locale

/**
 * Localisation. Réutilise les 12 fichiers JSON de la version Windows (dossier Shared/Assets/Lang, copiés au build
 * dans assets/lang), complétés par les textes propres au mobile (dossier assets/lang-mobile).
 * Repli : langue choisie → anglais → clé.
 */
object Loc {
    private const val DEFAULT = "en"
    private var texts: Map<String, String> = emptyMap()
    private var fallback: Map<String, String> = emptyMap()

    /** Code de la langue affichée (en, fr…). */
    var code: String = DEFAULT
        private set

    /** Langues disponibles : code → nom (champ « _language » du fichier). */
    var available: List<Pair<String, String>> = emptyList()
        private set

    fun init(ctx: Context) {
        val assets = ctx.assets
        available = (assets.list("lang") ?: emptyArray())
            .filter { it.endsWith(".json") && !it.startsWith("_") }
            .map { it.removeSuffix(".json") }
            .map { c -> c to (load(ctx, c)["_language"] ?: c) }
            .sortedBy { it.second.lowercase() }
        fallback = load(ctx, DEFAULT)
        apply(ctx, Store.language(ctx))
    }

    /** [choice] = "" pour la langue du système. */
    fun apply(ctx: Context, choice: String) {
        val wanted = choice.ifBlank { Locale.getDefault().language }
        code = if (available.any { it.first == wanted }) wanted else DEFAULT
        texts = if (code == DEFAULT) fallback else load(ctx, code)
    }

    private fun load(ctx: Context, c: String): Map<String, String> {
        val map = mutableMapOf<String, String>()
        for (dir in listOf("lang", "lang-mobile")) {
            try {
                val json = ctx.assets.open("$dir/$c.json").bufferedReader(Charsets.UTF_8).use { it.readText() }
                val o = JSONObject(json)
                for (k in o.keys()) map[k] = o.optString(k)
            } catch (_: Exception) {
            }
        }
        return map
    }

    /**
     * Texte traduit. Les paramètres {0}, {1}… sont remplacés ; un format .NET éventuel ({0:HH:mm:ss})
     * est ignoré : l'appelant fournit la valeur déjà formatée.
     */
    fun t(key: String, vararg args: Any): String {
        var s = texts[key] ?: fallback[key] ?: key
        args.forEachIndexed { i, a ->
            s = s.replace(Regex("\\{$i(:[^}]*)?\\}"), Regex.escapeReplacement(a.toString()))
        }
        return s
    }

    fun eventName(kind: EventKind): String = t(kind.locKey)
}
