package com.chaskha.d4overlay.ui

import android.annotation.SuppressLint
import android.app.Activity
import android.util.Log
import android.view.ViewGroup
import android.webkit.JavascriptInterface
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.FrameLayout
import com.chaskha.d4overlay.data.ScheduleParser
import com.chaskha.d4overlay.data.ScheduleRepository
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.withTimeoutOrNull

/**
 * Repli quand l'appel HTTP direct est bloqué (vérification Cloudflare de helltides.com) :
 * un navigateur intégré invisible ouvre le site, passe la vérification, puis lit /api/schedule depuis la page.
 * Si rien n'est arrivé au bout de 12 s, il ouvre directement l'adresse de l'API et lit le texte affiché
 * (méthode de la version Windows). À appeler sur le fil principal, appli au premier plan.
 */
class WebScheduleFetcher(private val activity: Activity) {

    @SuppressLint("SetJavaScriptEnabled")
    suspend fun fetch(timeoutMs: Long = 30_000L): String? {
        val result = CompletableDeferred<String>()
        val root = activity.findViewById<ViewGroup>(android.R.id.content)
        val web = WebView(activity).apply {
            alpha = 0f
            settings.javaScriptEnabled = true
            settings.domStorageEnabled = true
            addJavascriptInterface(Bridge(result), "D4Bridge")
            webViewClient = object : WebViewClient() {
                override fun onPageFinished(view: WebView, url: String?) {
                    Log.i(TAG, "page $url")
                    view.evaluateJavascript(if (url?.contains("/api/") == true) READ_BODY else POLL_API, null)
                }
            }
        }
        // Une vraie taille (même invisible) : certains scripts de vérification ne s'exécutent pas dans 1×1.
        root.addView(web, FrameLayout.LayoutParams(400, 400))
        return try {
            web.loadUrl(ScheduleRepository.SITE_URL)
            withTimeoutOrNull(12_000L) { result.await() } ?: run {
                Log.i(TAG, "rien via la page, ouverture directe de l'API")
                web.loadUrl(ScheduleRepository.API_URL)
                withTimeoutOrNull(timeoutMs - 12_000L) { result.await() }
            }
        } finally {
            Log.i(TAG, "fin : ${if (result.isCompleted) "planning reçu" else "rien"}")
            root.removeView(web)
            web.destroy()
        }
    }

    /** Doit rester publique : le WebView appelle ces méthodes par réflexion. */
    class Bridge(private val result: CompletableDeferred<String>) {
        @JavascriptInterface
        fun onResult(status: Int, text: String?) {
            val json = ScheduleParser.extractJson(text)
            // Seul un planning vraiment lisible termine l'attente (une page Cloudflare contient aussi « helltides »).
            val ok = status in 200..299 && json != null && ScheduleParser.parse(json) != null
            Log.i(TAG, "réponse $status len=${text?.length ?: 0} ${if (ok) "OK" else "refusée"}")
            if (ok) result.complete(json!!)
        }
    }

    private companion object {
        const val TAG = "D4Web"

        /** Depuis la page du site : réessaie toutes les 4 s (la première page peut être celle de la vérification). */
        const val POLL_API = """
            (function () {
              if (window.__d4) return; window.__d4 = true;
              function go() {
                fetch('/api/schedule', { credentials: 'include', headers: { 'Accept': 'application/json' } })
                  .then(function (r) { return r.text().then(function (t) { D4Bridge.onResult(r.status, t); }); })
                  .catch(function () {});
              }
              go(); setInterval(go, 4000);
            })();
        """

        /** Page de l'API ouverte directement : lit le texte affiché, toutes les secondes. */
        const val READ_BODY = """
            (function () {
              if (window.__d4) return; window.__d4 = true;
              function go() { D4Bridge.onResult(200, document.body ? document.body.innerText : ''); }
              go(); setInterval(go, 1000);
            })();
        """
    }
}
