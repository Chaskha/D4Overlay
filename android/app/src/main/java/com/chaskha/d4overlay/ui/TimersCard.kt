package com.chaskha.d4overlay.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.chaskha.d4overlay.data.EventAlertConfig
import com.chaskha.d4overlay.data.EventKind
import com.chaskha.d4overlay.data.Schedule
import com.chaskha.d4overlay.i18n.Loc

/**
 * Les timers, une ligne par évènement affiché : nom en couleur (et nom du boss pour le World Boss)
 * à gauche, compte à rebours à droite. En bas : prochaine alerte et bouton Actualiser.
 */
@Composable
fun TimersCard(
    schedule: Schedule,
    configs: Map<EventKind, EventAlertConfig>,
    nowMs: Long,
    nextAlertAtMs: Long,
    refreshing: Boolean,
    onRefresh: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Card(modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            for (kind in EventKind.displayOrder) {
                if (configs[kind]?.visible == false) continue
                val spawn = schedule.next(kind, nowMs)
                val color = Color(kind.color)
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text(Loc.eventName(kind), color = color, style = MaterialTheme.typography.titleMedium,
                            fontWeight = FontWeight.Bold)
                        spawn.name?.takeIf { it != kind.label }?.let {
                            Text(it, style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                    Text(
                        timerValue(spawn, nowMs), color = color, fontWeight = FontWeight.Bold,
                        // Chiffres à largeur fixe : le compte à rebours ne « danse » pas chaque seconde.
                        style = MaterialTheme.typography.titleLarge.copy(fontFeatureSettings = "tnum"),
                    )
                }
            }
            HorizontalDivider()
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    if (nextAlertAtMs > 0) Loc.t("m.nextAlert", formatClock(nextAlertAtMs)) else Loc.t("m.noAlert"),
                    Modifier.weight(1f), style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                TextButton(onClick = onRefresh, enabled = !refreshing) {
                    Text(if (refreshing) Loc.t("m.refreshing") else Loc.t("m.refresh"))
                }
            }
        }
    }
}
