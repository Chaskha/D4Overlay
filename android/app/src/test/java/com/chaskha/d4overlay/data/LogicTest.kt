package com.chaskha.d4overlay.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

/** Tests de la logique pure (sans Android). Échantillon : vraie réponse de l'API du 2026-11-12. */
class LogicTest {
    private val sample = javaClass.classLoader!!.getResource("schedule-sample.json")!!.readText()
    private fun ms(iso: String) = Instant.parse(iso).toEpochMilli()

    @Test
    fun parsesRealApiResponse() {
        val map = ScheduleParser.parse(sample)
        assertNotNull(map)
        val wb = map!![EventKind.WORLD_BOSS]!!
        assertEquals(ms("2026-11-12T13:30:00Z"), wb.first().startMs)
        assertEquals("Wandering Death", wb.first().name)
        assertTrue(map[EventKind.LEGION]!!.isNotEmpty())
        assertEquals(ms("2026-11-12T10:00:00Z"), map[EventKind.HELLTIDE]!!.first().startMs)
        // La Legion n'a pas de nom de boss dans l'API.
        assertNull(map[EventKind.LEGION]!!.first().name)
    }

    @Test
    fun acceptsWrappedAndPrefixedJson() {
        val wrapped = "garbage <pre>{\"data\":{\"legion\":[{\"timestamp\":1794479700}]}}"
        val map = ScheduleParser.parse(wrapped)!!
        assertEquals(1794479700_000L, map[EventKind.LEGION]!!.single().startMs)
        // Millisecondes au lieu de secondes.
        val msJson = "{\"legion\":[{\"timestamp\":1794479700000}]}"
        assertEquals(1794479700_000L, ScheduleParser.parse(msJson)!![EventKind.LEGION]!!.single().startMs)
    }

    @Test
    fun rejectsNonSchedule() {
        assertNull(ScheduleParser.parse("<!DOCTYPE html><title>Just a moment...</title>"))
        assertNull(ScheduleParser.parse("{\"foo\":1}"))
    }

    @Test
    fun cadenceMatchesApi() {
        // World Boss toutes les 3 h 30 depuis 2024-01-01 08:30 UTC : l'API donne 13:30 le 2026-11-12.
        assertEquals(ms("2026-11-12T13:30:00Z"), Cadence.next(EventKind.WORLD_BOSS, ms("2026-11-12T11:00:00Z")))
        // Legion toutes les 25 min : 10:35, 11:00, 11:25…
        assertEquals(ms("2026-11-12T11:00:00Z"), Cadence.next(EventKind.LEGION, ms("2026-11-12T10:40:00Z")))
        // Helltide : en cours jusqu'à :55, puis l'heure suivante.
        assertEquals(ms("2026-11-12T10:00:00Z"), Cadence.next(EventKind.HELLTIDE, ms("2026-11-12T10:54:00Z")))
        assertEquals(ms("2026-11-12T11:00:00Z"), Cadence.next(EventKind.HELLTIDE, ms("2026-11-12T10:56:00Z")))
    }

    @Test
    fun scheduleExtendsApiWithCadence() {
        val s = Schedule(ScheduleParser.parse(sample)!!, DataSource.API, 0L)
        val now = ms("2026-11-12T11:00:00Z")
        val wb = s.spawns(EventKind.WORLD_BOSS, now, 48 * 3_600_000L)
        // Pas de doublon ni de trou à la jonction API → cadence : écart constant de 3 h 30.
        wb.zipWithNext().forEach { (a, b) -> assertEquals(210 * 60_000L, b.startMs - a.startMs) }
        assertTrue(wb.last().startMs > ms("2026-11-13T03:30:00Z"))
    }

    @Test
    fun plannerSkipsPassedLevelsAndKeepsClosest() {
        val s = Schedule.cadenceOnly()
        val cfg = AlertConfigJson.defaults()
        val now = ms("2026-11-12T13:26:30Z") // World Boss à 13:30 : paliers 8 et 4 min déjà dépassés
        val triggers = AlertPlanner.allTriggers(s, cfg, now)

        // Ouverture de l'appli à 13:26:30 (prime) : seul le palier 2 min (13:28) du World Boss reste à jouer.
        val next = AlertPlanner.next(triggers.filter { it.spawn.kind == EventKind.WORLD_BOSS }, now, now)!!
        assertEquals(2, next.level.minutes)

        // Réveil tardif à 13:26:40 sans traitement depuis 13:20 : 8 et 4 min sont dus, on ne joue que 4 min.
        val late = ms("2026-11-12T13:26:40Z")
        val due = AlertPlanner.due(triggers, ms("2026-11-12T13:20:00Z"), late)
            .filter { it.spawn.kind == EventKind.WORLD_BOSS }
        assertEquals(listOf(4), due.map { it.level.minutes })
    }

    @Test
    fun silentEventsHaveNoAlertsButHiddenOnesStillRing() {
        val cfg = AlertConfigJson.defaults().toMutableMap()
        cfg[EventKind.LEGION] = cfg[EventKind.LEGION]!!.copy(enabled = false)
        cfg[EventKind.HELLTIDE] = cfg[EventKind.HELLTIDE]!!.copy(visible = false)
        val triggers = AlertPlanner.allTriggers(Schedule.cadenceOnly(), cfg, ms("2026-11-12T11:00:00Z"))
        assertTrue(triggers.none { it.spawn.kind == EventKind.LEGION })
        assertTrue(triggers.any { it.spawn.kind == EventKind.HELLTIDE })
        assertTrue(triggers.any { it.spawn.kind == EventKind.WORLD_BOSS })
    }

    @Test
    fun alertsJsonRoundTripIsWindowsCompatible() {
        val windows = """{"World Boss":{"Enabled":true,"Visible":true,"OnlyInGame":true,
            "Levels":[{"Minutes":5,"Sound":"WB-male.mp3","Volume":40}]},"Legion":{"Enabled":false,"Visible":true,"Levels":[]}}"""
        val map = AlertConfigJson.parse(windows)
        assertEquals(AlertLevel(5, "WB-male.mp3", 40), map[EventKind.WORLD_BOSS]!!.levels.single())
        assertTrue(map[EventKind.WORLD_BOSS]!!.onlyInGame)
        assertEquals(false, map[EventKind.LEGION]!!.enabled)
        // Helltide absent du fichier : valeurs par défaut.
        assertEquals(3, map[EventKind.HELLTIDE]!!.levels.size)
        assertEquals(map, AlertConfigJson.parse(AlertConfigJson.toJson(map)))
    }
}
