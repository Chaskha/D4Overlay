package com.chaskha.d4overlay.ui

import android.content.Context
import android.graphics.BitmapFactory
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp

/**
 * Bannières d'en-tête : les fichiers PNG (fond transparent) de assets/banners.
 * « metal » (la barre Diablo IV Events d'origine) d'abord, les classes ensuite par ordre alphabétique.
 * Ajouter une bannière = déposer un PNG dans ce dossier : elle apparaît dans le choix.
 */
object Banners {
    const val DEFAULT = "metal"
    private const val DIR = "banners"

    fun list(ctx: Context): List<String> {
        val ids = (ctx.assets.list(DIR) ?: emptyArray())
            .filter { it.endsWith(".png") }
            .map { it.removeSuffix(".png") }
            .sorted()
        return ids.filter { it == DEFAULT } + ids.filter { it != DEFAULT }
    }

    /** [sample] = 1 pour l'en-tête (1200 px), 4 pour les miniatures (≈ 300 px, 16 fois moins de mémoire). */
    fun load(ctx: Context, id: String, sample: Int = 1): ImageBitmap? = try {
        val opts = BitmapFactory.Options().apply { inSampleSize = sample }
        ctx.assets.open("$DIR/$id.png").use { BitmapFactory.decodeStream(it, null, opts)?.asImageBitmap() }
    } catch (_: Exception) {
        null
    }
}

/** En-tête de l'écran : la bannière choisie, sur toute la largeur, à sa proportion. Repli : la bannière Métal. */
@Composable
fun TitleBar(bannerId: String, modifier: Modifier = Modifier) {
    val ctx = LocalContext.current
    val image = remember(bannerId) { Banners.load(ctx, bannerId) ?: Banners.load(ctx, Banners.DEFAULT) } ?: return
    Image(
        image, contentDescription = "Diablo IV Events",
        modifier = modifier.fillMaxWidth().aspectRatio(image.width.toFloat() / image.height),
        contentScale = ContentScale.Fit,
    )
}

/** Rangée de miniatures qui défile ; la bannière choisie est entourée. */
@Composable
fun BannerPicker(selected: String, onSelect: (String) -> Unit) {
    val ctx = LocalContext.current
    val banners = remember { Banners.list(ctx).mapNotNull { id -> Banners.load(ctx, id, sample = 4)?.let { id to it } } }
    val shape = RoundedCornerShape(8.dp)
    LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        items(banners, key = { it.first }) { (id, thumb) ->
            val isSelected = id == selected
            Image(
                thumb, contentDescription = id,
                modifier = Modifier
                    .width(180.dp)
                    .aspectRatio(thumb.width.toFloat() / thumb.height)
                    .clip(shape)
                    .border(
                        if (isSelected) BorderStroke(2.dp, MaterialTheme.colorScheme.primary)
                        else BorderStroke(1.dp, Color.White.copy(alpha = 0.15f)),
                        shape,
                    )
                    .clickable { onSelect(id) }
                    .padding(4.dp),
                contentScale = ContentScale.Fit,
            )
        }
    }
}
