package com.chaskha.d4overlay.ui

import android.Manifest
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.lifecycleScope
import com.chaskha.d4overlay.BuildConfig
import com.chaskha.d4overlay.alerts.AlertScheduler
import com.chaskha.d4overlay.alerts.Notifications
import com.chaskha.d4overlay.alerts.SoundPlayer
import com.chaskha.d4overlay.data.AlertConfigJson
import com.chaskha.d4overlay.data.AlertLevel
import com.chaskha.d4overlay.data.DataSource
import com.chaskha.d4overlay.data.EventAlertConfig
import com.chaskha.d4overlay.data.EventKind
import com.chaskha.d4overlay.data.Schedule
import com.chaskha.d4overlay.data.ScheduleRepository
import com.chaskha.d4overlay.data.Store
import com.chaskha.d4overlay.i18n.Loc
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {

    // État de l'écran (Compose se redessine quand une de ces valeurs change).
    private var configs by mutableStateOf<Map<EventKind, EventAlertConfig>>(emptyMap())
    private var schedule by mutableStateOf(Schedule.cadenceOnly())
    private var notificationsAllowed by mutableStateOf(true)
    private var exactAllowed by mutableStateOf(true)
    private var refreshing by mutableStateOf(false)
    private var nextAlertAt by mutableLongStateOf(0L)
    private var alertsOn by mutableStateOf(true)
    /** Incrémenté au changement de langue : force le recalcul de tous les textes. */
    private var languageVersion by mutableStateOf(0)
    private var banner by mutableStateOf(Banners.DEFAULT)

    private var refreshJob: Job? = null
    private var soundJob: Job? = null
    private lateinit var webFetcher: WebScheduleFetcher

    private val notificationPermission =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { updatePermissions() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        webFetcher = WebScheduleFetcher(this)
        configs = Store.loadAlerts(this)
        banner = Store.banner(this)
        schedule = ScheduleRepository.current(this)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU && !Notifications.permitted(this)) {
            notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
        }

        setContent {
            MaterialTheme(colorScheme = d4Colors) {
                // Changement de langue : on reconstruit tout l'écran pour que chaque texte soit relu.
                androidx.compose.runtime.key(languageVersion) { Screen() }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        updatePermissions()
        schedule = ScheduleRepository.current(this)
        // Relu à chaque retour : le bouton « Couper les alertes » d'une notification a pu le changer.
        alertsOn = Store.alertsOn(this)
        AlertScheduler.reschedule(this, prime = false)
        nextAlertAt = Store.nextAlertAtMs(this)
        if (ScheduleRepository.isStale(this)) refresh()
    }

    private fun changeAlertsOn(on: Boolean) {
        alertsOn = on
        AlertScheduler.setAlertsOn(this, on)
        nextAlertAt = Store.nextAlertAtMs(this)
    }

    private fun updatePermissions() {
        notificationsAllowed = Notifications.permitted(this)
        exactAllowed = AlertScheduler.canScheduleExact(this)
    }

    /** HTTP direct, puis navigateur intégré si Cloudflare bloque. Le cache et la cadence restent en filet de sécurité. */
    private fun refresh() {
        if (refreshJob?.isActive == true) return
        refreshJob = lifecycleScope.launch {
            refreshing = true
            try {
                if (!ScheduleRepository.fetchViaHttp(this@MainActivity)) {
                    ScheduleRepository.accept(this@MainActivity, webFetcher.fetch())
                }
                schedule = ScheduleRepository.current(this@MainActivity)
                AlertScheduler.reschedule(this@MainActivity, prime = false)
                nextAlertAt = Store.nextAlertAtMs(this@MainActivity)
            } finally {
                refreshing = false
            }
        }
    }

    private fun updateConfig(kind: EventKind, cfg: EventAlertConfig) = saveConfigs(configs + (kind to cfg))

    private fun saveConfigs(map: Map<EventKind, EventAlertConfig>) {
        configs = map
        Store.saveAlerts(this, map)
        // Comme à l'enregistrement sur Windows : les paliers déjà dépassés ne sonnent pas.
        AlertScheduler.reschedule(this, prime = true)
        nextAlertAt = Store.nextAlertAtMs(this)
    }

    private fun test(level: AlertLevel) {
        soundJob?.cancel()
        soundJob = lifecycleScope.launch { SoundPlayer.play(this@MainActivity, level.sound, level.volume) }
    }

    private fun setLanguage(code: String) {
        Store.setLanguage(this, code)
        Loc.apply(this, code)
        Notifications.createChannel(this) // renomme le canal dans les réglages Android
        languageVersion++
    }

    private fun openUrl(url: String) {
        try {
            startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
        } catch (_: Exception) {
        }
    }

    private fun openExactAlarmSettings() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            try {
                startActivity(Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM, Uri.parse("package:$packageName")))
            } catch (_: Exception) {
            }
        }
    }

    private fun openNotificationSettings() {
        try {
            startActivity(Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, packageName))
        } catch (_: Exception) {
        }
    }

    // ───────────── Interface ─────────────

    @Composable
    private fun Screen() {
        var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
        LaunchedEffect(Unit) {
            while (true) {
                now = System.currentTimeMillis()
                delay(1000L - now % 1000L) // tick calé sur la seconde, comme l'overlay
            }
        }
        val sounds = remember { SoundPlayer.builtIns + SoundPlayer.voices(this) }
        var confirmDefaults by remember { mutableStateOf(false) }

        Scaffold(containerColor = MaterialTheme.colorScheme.background) { insets ->
            LazyColumn(
                Modifier.fillMaxSize(),
                contentPadding = PaddingValues(
                    start = 16.dp, end = 16.dp,
                    top = insets.calculateTopPadding() + 12.dp,
                    bottom = insets.calculateBottomPadding() + 24.dp,
                ),
                verticalArrangement = Arrangement.spacedBy(12.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                item { TitleBar(banner, Modifier.widthIn(max = 560.dp)) }
                item {
                    TimersCard(
                        schedule, configs, now, nextAlertAt,
                        alertsOn = alertsOn, onAlertsOnChange = { changeAlertsOn(it) },
                        refreshing = refreshing, onRefresh = { refresh() },
                    )
                }
                if (!notificationsAllowed) item {
                    Banner(Loc.t("m.perm.notif")) {
                        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
                            shouldShowRequestPermissionRationale(Manifest.permission.POST_NOTIFICATIONS)
                        ) notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
                        else openNotificationSettings()
                    }
                }
                if (!exactAllowed) item { Banner(Loc.t("m.perm.exact")) { openExactAlarmSettings() } }

                for (kind in EventKind.displayOrder) {
                    item(key = kind.name) {
                        EventCard(
                            kind = kind,
                            cfg = configs[kind] ?: EventAlertConfig(),
                            schedule = schedule,
                            nowMs = now,
                            sounds = sounds,
                            onChange = { updateConfig(kind, it) },
                            onTest = { test(it) },
                        )
                    }
                }
                item { SettingsCard(onDefaults = { confirmDefaults = true }) }
                item { AboutCard() }
            }
        }

        if (confirmDefaults) {
            AlertDialog(
                onDismissRequest = { confirmDefaults = false },
                text = { Text(Loc.t("m.defaults.confirm")) },
                confirmButton = {
                    TextButton(onClick = { confirmDefaults = false; saveConfigs(AlertConfigJson.defaults()) }) {
                        Text(Loc.t("m.ok"))
                    }
                },
                dismissButton = { TextButton(onClick = { confirmDefaults = false }) { Text(Loc.t("m.cancel")) } },
            )
        }
    }

    @Composable
    private fun Banner(text: String, onGrant: () -> Unit) {
        Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = Color(0xFF4A1C16))) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(text, color = Color(0xFFFFDAD4))
                Button(onClick = onGrant) { Text(Loc.t("m.perm.grant")) }
            }
        }
    }

    @Composable
    private fun SettingsCard(onDefaults: () -> Unit) {
        SectionCard(Loc.t("m.settings")) {
            var open by remember { mutableStateOf(false) }
            val choice = Store.language(this@MainActivity)
            val current = if (choice.isBlank()) Loc.t("m.language.system")
            else Loc.available.firstOrNull { it.first == choice }?.second ?: choice
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(Loc.t("m.language"), Modifier.weight(1f))
                Box {
                    OutlinedButton(onClick = { open = true }) { Text(current) }
                    DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                        DropdownMenuItem(text = { Text(Loc.t("m.language.system")) },
                            onClick = { open = false; setLanguage("") })
                        Loc.available.forEach { (code, name) ->
                            DropdownMenuItem(text = { Text(name) }, onClick = { open = false; setLanguage(code) })
                        }
                    }
                }
            }
            Text(Loc.t("m.banner"))
            BannerPicker(banner) { id -> banner = id; Store.setBanner(this@MainActivity, id) }
            Text(Loc.t("m.soundNote"), style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            OutlinedButton(onClick = onDefaults) { Text(Loc.t("btn.defaults")) }
        }
    }

    @Composable
    private fun AboutCard() {
        SectionCard(Loc.t("info.about.title")) {
            Text(Loc.t("m.version", BuildConfig.VERSION_NAME))
            Text(Loc.t("info.credit"), style = MaterialTheme.typography.bodySmall)
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(Loc.t("info.dataBy"), style = MaterialTheme.typography.bodySmall)
                TextButton(onClick = { openUrl(ScheduleRepository.SITE_URL) }) { Text("helltides.com") }
            }
            val source = when (schedule.source) {
                DataSource.API -> Loc.t("info.source.api") + Loc.t("info.source.api.time", formatClock(schedule.fetchedAtMs, true))
                DataSource.CACHE -> Loc.t("info.source.cache")
                DataSource.CADENCE -> Loc.t("info.source.cadence")
            }
            Text(Loc.t("info.source", source), style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text(Loc.t("info.contact.text"), style = MaterialTheme.typography.bodySmall)
            TextButton(onClick = { openUrl("mailto:$CONTACT_EMAIL") }) { Text(CONTACT_EMAIL) }
            Text(Loc.t("info.donate.text"), style = MaterialTheme.typography.bodySmall)
            OutlinedButton(onClick = { openUrl(DONATE_URL) }) { Text(Loc.t("info.donate.button")) }
            Text(Loc.t("m.legal"), style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }

    @Composable
    private fun SectionCard(title: String, content: @Composable () -> Unit) {
        Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant)) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                content()
            }
        }
    }

    private companion object {
        // Mêmes valeurs que Shared/AppInfo.cs.
        const val CONTACT_EMAIL = "chasha_claude@gmail.com"
        const val DONATE_URL = "https://paypal.me/ArnaudHuet345"

        val d4Colors = darkColorScheme(
            primary = Color(0xFFD03A20),
            onPrimary = Color.White,
            secondary = Color(0xFFB0B0B8),
            background = Color(0xFF121014),
            surface = Color(0xFF121014),
            surfaceVariant = Color(0xFF221D20),
            onSurfaceVariant = Color(0xFFC9BFC2),
        )
    }
}
