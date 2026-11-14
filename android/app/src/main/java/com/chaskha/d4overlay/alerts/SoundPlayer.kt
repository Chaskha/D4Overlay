package com.chaskha.d4overlay.alerts

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioTrack
import android.media.MediaPlayer
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import kotlin.math.PI
import kotlin.math.min
import kotlin.math.pow
import kotlin.math.sin

/**
 * Lecture des sons d'alerte (portage de AlertSounds.cs) :
 * - bips intégrés synthétisés (mêmes fréquences, nombres et durées que sur Windows) ;
 * - voix fournies avec l'appli (Packaging/defaults/sounds, copiées au build dans assets/sounds).
 * Le volume suit la même courbe quadratique. Le son passe par le flux « notification » du téléphone :
 * il respecte le mode silencieux.
 */
object SoundPlayer {
    val builtIns = listOf("none", "asterisk", "exclamation", "hand", "question", "beep")

    /** Une alerte reçue en arrière-plan a environ 10 s pour se terminer : on coupe un peu avant. */
    const val MAX_PLAY_MS = 9_000L
    private const val SAMPLE_RATE = 44_100

    private val attributes = AudioAttributes.Builder()
        .setUsage(AudioAttributes.USAGE_NOTIFICATION_EVENT)
        .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
        .build()

    /** Voix disponibles (noms de fichiers, comme dans alerts.json : « WB-male.mp3 »…). */
    fun voices(ctx: Context): List<String> =
        (ctx.assets.list("sounds") ?: emptyArray()).filter { it.endsWith(".mp3", true) }.sorted()

    /** Joue un son et attend la fin (au plus [maxMs], et jamais plus de 9 s). */
    suspend fun play(ctx: Context, sound: String, volumePercent: Int, maxMs: Long = MAX_PLAY_MS) {
        if (sound.isBlank() || sound == "none") return
        val v = volumePercent.coerceIn(0, 100)
        if (v == 0) return
        val gain = (v / 100.0).pow(2).toFloat()
        withTimeoutOrNull(maxMs.coerceAtMost(MAX_PLAY_MS)) {
            if (sound.endsWith(".mp3", true) && sound in voices(ctx)) playAsset(ctx, sound, gain)
            else playTones(sound, gain)
        }
    }

    private suspend fun playAsset(ctx: Context, name: String, gain: Float) {
        val done = CompletableDeferred<Unit>()
        val player = MediaPlayer()
        try {
            ctx.assets.openFd("sounds/$name").use { fd ->
                player.setDataSource(fd.fileDescriptor, fd.startOffset, fd.length)
            }
            player.setAudioAttributes(attributes)
            player.setVolume(gain, gain)
            player.setOnCompletionListener { done.complete(Unit) }
            player.setOnErrorListener { _, _, _ -> done.complete(Unit); true }
            withContext(Dispatchers.IO) { player.prepare() }
            player.start()
            done.await()
        } catch (_: Exception) {
            playTones("exclamation", gain)
        } finally {
            player.release()
        }
    }

    private suspend fun playTones(sound: String, gain: Float) = withContext(Dispatchers.Default) {
        var freq = 880; var count = 2; var dur = 140; val gap = 80
        when (sound.trim().lowercase()) {
            "asterisk" -> { freq = 660; count = 2; dur = 160 }
            "exclamation" -> { freq = 880; count = 3; dur = 140 }
            "hand", "error" -> { freq = 1320; count = 4; dur = 110 }
            "question" -> { freq = 990; count = 2; dur = 180 }
            "beep" -> { freq = 1000; count = 1; dur = 220 }
        }
        val pcm = buildBeeps(freq, count, dur, gap, 0.8f * gain)
        val track = AudioTrack.Builder()
            .setAudioAttributes(attributes)
            .setAudioFormat(AudioFormat.Builder()
                .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                .setSampleRate(SAMPLE_RATE)
                .setChannelMask(AudioFormat.CHANNEL_OUT_MONO)
                .build())
            .setTransferMode(AudioTrack.MODE_STATIC)
            .setBufferSizeInBytes(pcm.size * 2)
            .build()
        try {
            track.write(pcm, 0, pcm.size)
            track.play()
            val totalMs = pcm.size * 1000L / SAMPLE_RATE
            Thread.sleep(totalMs + 50)
            track.stop()
        } catch (_: Exception) {
        } finally {
            track.release()
        }
    }

    /** Bips sinusoïdaux avec fondu de 5 ms (évite les clics), séparés par [gapMs] de silence. */
    private fun buildBeeps(freq: Int, count: Int, durMs: Int, gapMs: Int, amp: Float): ShortArray {
        val beep = SAMPLE_RATE * durMs / 1000
        val gap = SAMPLE_RATE * gapMs / 1000
        val fade = SAMPLE_RATE * 5 / 1000
        val out = ShortArray(count * beep + (count - 1) * gap)
        var pos = 0
        repeat(count) { n ->
            for (i in 0 until beep) {
                val env = min(1f, min(i, beep - 1 - i).toFloat() / fade)
                val s = sin(2 * PI * freq * i / SAMPLE_RATE).toFloat() * amp * env
                out[pos + i] = (s * Short.MAX_VALUE).toInt().toShort()
            }
            pos += beep
            if (n < count - 1) pos += gap
        }
        return out
    }
}
