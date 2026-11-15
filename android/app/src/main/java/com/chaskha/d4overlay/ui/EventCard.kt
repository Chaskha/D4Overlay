package com.chaskha.d4overlay.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Slider
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.chaskha.d4overlay.data.AlertLevel
import com.chaskha.d4overlay.data.EventAlertConfig
import com.chaskha.d4overlay.data.EventKind
import com.chaskha.d4overlay.data.Schedule
import com.chaskha.d4overlay.i18n.Loc

private const val MAX_MINUTES = 120

/** Onglet d'un évènement de la version Windows, en carte : affichage, alarmes, paliers. */
@Composable
fun EventCard(
    kind: EventKind,
    cfg: EventAlertConfig,
    schedule: Schedule,
    nowMs: Long,
    sounds: List<String>,
    onChange: (EventAlertConfig) -> Unit,
    onTest: (AlertLevel) -> Unit,
) {
    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(Loc.eventName(kind), color = Color(kind.color), style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.Bold)

            val upcoming = schedule.spawns(kind, nowMs).filter { it.startMs > nowMs }.take(3)
                .joinToString("  ·  ") { s -> formatClock(s.startMs) + (s.name?.let { " $it" } ?: "") }
            if (upcoming.isNotEmpty()) Text(Loc.t("m.upcoming", upcoming), style = MaterialTheme.typography.bodyMedium)

            SwitchRow(Loc.t("m.visible"), cfg.visible) { onChange(cfg.copy(visible = it)) }
            SwitchRow(Loc.t("ev.alarms"), cfg.enabled) { onChange(cfg.copy(enabled = it)) }
            if (!cfg.enabled) Hint(Loc.t("ev.hint.silent"))

            if (cfg.enabled) {
                HorizontalDivider()
                Text(Loc.t("ev.levels", Loc.eventName(kind)), style = MaterialTheme.typography.titleSmall)
                cfg.levels.forEachIndexed { index, level ->
                    // La clé force un nouvel état local quand la liste change (suppression d'un palier).
                    androidx.compose.runtime.key(index, level.minutes) {
                        LevelRow(
                            level = level,
                            sounds = sounds,
                            usedMinutes = cfg.levels.map { it.minutes }.toSet() - level.minutes,
                            onChange = { new -> onChange(cfg.copy(levels = cfg.levels.toMutableList().also { it[index] = new })) },
                            onRemove = { onChange(cfg.copy(levels = cfg.levels.filterIndexed { i, _ -> i != index })) },
                            onTest = { onTest(it) },
                        )
                    }
                }
                OutlinedButton(onClick = {
                    val used = cfg.levels.map { it.minutes }.toSet()
                    val minutes = (listOf(5, 10, 3, 1, 15, 20, 30) + (1..MAX_MINUTES)).firstOrNull { it !in used }
                    if (minutes != null) onChange(cfg.copy(levels = (cfg.levels + AlertLevel(minutes)).sortedByDescending { it.minutes }))
                }) { Text(Loc.t("ev.addLevel")) }
            }
        }
    }
}

@Composable
private fun SwitchRow(label: String, checked: Boolean, enabled: Boolean = true, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(label, Modifier.weight(1f), style = MaterialTheme.typography.bodyLarge)
        Switch(checked = checked, onCheckedChange = onChange, enabled = enabled)
    }
}

@Composable
private fun Hint(text: String) =
    Text(text, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)

@Composable
private fun LevelRow(
    level: AlertLevel,
    sounds: List<String>,
    usedMinutes: Set<Int>,
    onChange: (AlertLevel) -> Unit,
    onRemove: () -> Unit,
    onTest: (AlertLevel) -> Unit,
) {
    Column(Modifier.fillMaxWidth().padding(vertical = 4.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(Loc.t("lvl.warn"))
            // Les boutons sautent les durées déjà prises par un autre palier (doublons interdits, comme sur Windows).
            TextButton(onClick = {
                (level.minutes - 1 downTo 1).firstOrNull { it !in usedMinutes }?.let { onChange(level.copy(minutes = it)) }
            }) { Text("−", style = MaterialTheme.typography.titleLarge) }
            Text("${level.minutes}", Modifier.widthIn(min = 28.dp), style = MaterialTheme.typography.titleMedium)
            TextButton(onClick = {
                (level.minutes + 1..MAX_MINUTES).firstOrNull { it !in usedMinutes }?.let { onChange(level.copy(minutes = it)) }
            }) { Text("+", style = MaterialTheme.typography.titleLarge) }
            Text(Loc.t("lvl.minBefore"), Modifier.weight(1f))
            TextButton(onClick = onRemove) { Text("✕") }
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            SoundPicker(level.sound, sounds, Modifier.weight(1f)) { onChange(level.copy(sound = it)) }
            TextButton(onClick = { onTest(level) }) { Text(Loc.t("lvl.test")) }
        }
        var volume by remember(level.volume) { mutableFloatStateOf(level.volume.toFloat()) }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(Loc.t("lvl.vol"))
            Spacer(Modifier.width(8.dp))
            Slider(
                value = volume,
                onValueChange = { volume = it },
                onValueChangeFinished = { onChange(level.copy(volume = volume.toInt())) },
                valueRange = 0f..100f,
                modifier = Modifier.weight(1f),
            )
            Text("${volume.toInt()} %", Modifier.widthIn(min = 48.dp))
        }
    }
}

fun soundLabel(id: String): String =
    if (id.endsWith(".mp3", true)) Loc.t("m.sound.voice", id.removeSuffix(".mp3").removeSuffix(".MP3"))
    else Loc.t("sound.$id")

@Composable
private fun SoundPicker(current: String, sounds: List<String>, modifier: Modifier, onPick: (String) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Box(modifier) {
        OutlinedButton(onClick = { open = true }, Modifier.fillMaxWidth()) {
            Text(if (current in sounds) soundLabel(current) else Loc.t("common.notFound", current), maxLines = 1)
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            sounds.forEach { s ->
                DropdownMenuItem(text = { Text(soundLabel(s)) }, onClick = { open = false; onPick(s) })
            }
        }
    }
}
