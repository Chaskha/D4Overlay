using NAudio.Wave;

namespace D4Overlay.Shared;

public sealed record BuiltInSound(string Id, string Label);

/// <summary>
/// Liste des sons disponibles + lecture avec volume (utilisée par l'overlay et par le bouton « Tester »).
/// Les bips intégrés sont synthétisés (sinusoïdes), les .wav sont lus tels quels : dans les deux cas
/// le volume s'applique réellement, sans toucher au volume de Windows ni à celui du jeu.
/// </summary>
public static class AlertSounds
{
    public static readonly BuiltInSound[] BuiltIns =
    {
        new("none", "Aucun son"),
        new("asterisk", "2 bips graves"),
        new("exclamation", "3 bips moyens"),
        new("hand", "4 bips aigus"),
        new("question", "2 bips longs"),
        new("beep", "1 bip"),
    };

    private const int SampleRate = 44100;

    /// <summary>Libellé (dans la langue courante) d'un son intégré.</summary>
    public static string LabelFor(string id) => Loc.T("sound." + id);

    /// <summary>
    /// Formats de fichiers acceptés pour les sons personnalisés. WAV et MP3 sont lus directement ; les autres passent par
    /// Windows (Media Foundation), donc dépendent des codecs installés (présents sur Windows 10 / 11).
    /// </summary>
    public static readonly string[] SoundExtensions = { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac" };

    /// <summary>Durée maximale de lecture d'un fichier : un morceau entier importé par erreur ne doit pas jouer pendant des minutes.</summary>
    private const int MaxFileSeconds = 20;

    public static bool IsSoundFile(string name)
        => SoundExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ListSoundFiles()
    {
        try
        {
            if (!Directory.Exists(ConfigStore.SoundsDir)) return new List<string>();
            return Directory.EnumerateFiles(ConfigStore.SoundsDir)
                .Where(IsSoundFile)
                .Select(p => Path.GetFileName(p))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return new List<string>(); }
    }

    /// <summary>Joue un son sans bloquer. volumePercent : 0 (muet) à 100.</summary>
    public static void Play(string sound, int volumePercent = 100)
    {
        if (string.IsNullOrWhiteSpace(sound) || sound == "none") return;
        volumePercent = Math.Clamp(volumePercent, 0, 100);
        if (volumePercent == 0) return;

        // Courbe quadratique : 50 % du curseur ≈ la moitié du volume ressenti (l'oreille est logarithmique).
        float gain = (float)Math.Pow(volumePercent / 100.0, 2);

        if (IsSoundFile(sound) && TryPlayFile(sound, gain))
            return;

        PlayTones(sound, gain);
    }

    /// <summary>
    /// Petit son de confirmation (sourdine activée / désactivée) : un « whoosh » bref et doux, bien moins agressif qu'un bip.
    /// Joué à l'endroit quand les sons reviennent, à l'envers quand ils sont coupés : on entend la différence sans regarder.
    /// </summary>
    public static void PlayFeedback(bool reversed, int volumePercent = 60)
    {
        try
        {
            using var stream = typeof(AlertSounds).Assembly.GetManifestResourceStream("sfx:whoosh.wav");
            if (stream is null)
            {
                Play("beep", 25);
                return;
            }

            // Le son est court (0,24 s) : on le charge en entier pour pouvoir l'inverser et régler le volume.
            using var reader = new WaveFileReader(stream);
            var source = reader.ToSampleProvider();
            int channels = source.WaveFormat.Channels;

            var samples = new List<float>();
            var buffer = new float[source.WaveFormat.SampleRate * channels];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                for (int i = 0; i < read; i++) samples.Add(buffer[i]);

            float[] data = samples.ToArray();
            if (reversed)
            {
                int frames = data.Length / channels;
                var flipped = new float[data.Length];
                for (int f = 0; f < frames; f++)
                    Array.Copy(data, (frames - 1 - f) * channels, flipped, f * channels, channels);
                data = flipped;
            }

            float gain = (float)Math.Pow(Math.Clamp(volumePercent, 0, 100) / 100.0, 2);
            for (int i = 0; i < data.Length; i++) data[i] *= gain;

            var bytes = new byte[data.Length * sizeof(float)];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            var format = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, channels);
            var pcm = new RawSourceWaveStream(new MemoryStream(bytes), format);
            var output = new WaveOutEvent { DesiredLatency = 100 };
            output.Init(pcm);
            output.PlaybackStopped += (_, _) =>
            {
                output.Dispose();
                pcm.Dispose();
            };
            output.Play();
        }
        catch
        {
            Play("beep", 25); // jamais de silence total : un bip discret en dernier recours
        }
    }

    private static bool TryPlayFile(string sound, float gain)
    {
        AudioFileReader? reader = null;
        WaveOutEvent? output = null;
        try
        {
            string path = Path.IsPathRooted(sound) ? sound : Path.Combine(ConfigStore.SoundsDir, sound);
            if (!File.Exists(path)) return false;

            reader = new AudioFileReader(path) { Volume = gain };
            output = new WaveOutEvent { DesiredLatency = 100 };
            output.Init(reader);

            var r = reader;
            var o = output;
            o.PlaybackStopped += (_, _) => { o.Dispose(); r.Dispose(); };
            o.Play();
            // Arrêt automatique : Stop() déclenche PlaybackStopped, qui libère tout. Sans effet si la lecture est déjà finie.
            _ = Task.Delay(TimeSpan.FromSeconds(MaxFileSeconds)).ContinueWith(_ =>
            {
                try { o.Stop(); } catch { }
            });
            return true;
        }
        catch
        {
            output?.Dispose();
            reader?.Dispose();
            return false;
        }
    }

    private static void PlayTones(string sound, float gain)
    {
        int freq = 880, count = 2, dur = 140, gap = 80;
        switch (sound.Trim().ToLowerInvariant())
        {
            case "asterisk": freq = 660; count = 2; dur = 160; break;
            case "exclamation": freq = 880; count = 3; dur = 140; break;
            case "hand":
            case "error": freq = 1320; count = 4; dur = 110; break;
            case "question": freq = 990; count = 2; dur = 180; break;
            case "beep": freq = 1000; count = 1; dur = 220; break;
        }

        RawSourceWaveStream? stream = null;
        WaveOutEvent? output = null;
        try
        {
            byte[] pcm = BuildBeeps(freq, count, dur, gap, 0.8f * gain);
            stream = new RawSourceWaveStream(new MemoryStream(pcm), new WaveFormat(SampleRate, 16, 1));
            output = new WaveOutEvent { DesiredLatency = 100 };
            output.Init(stream);

            var s = stream;
            var o = output;
            o.PlaybackStopped += (_, _) => { o.Dispose(); s.Dispose(); };
            o.Play();
        }
        catch
        {
            output?.Dispose();
            stream?.Dispose();
        }
    }

    /// <summary>PCM 16 bits mono : "count" bips sinusoïdaux séparés de "gapMs", avec une courte rampe pour éviter les claquements.</summary>
    private static byte[] BuildBeeps(int freq, int count, int durMs, int gapMs, float amplitude)
    {
        int toneSamples = SampleRate * durMs / 1000;
        int gapSamples = SampleRate * gapMs / 1000;
        int fade = Math.Max(1, Math.Min(SampleRate * 4 / 1000, toneSamples / 2));
        int tail = SampleRate / 10; // 100 ms de silence final pour ne pas couper le dernier bip
        int total = count * toneSamples + (count - 1) * gapSamples + tail;

        var pcm = new byte[total * 2];
        int pos = 0;
        for (int n = 0; n < count; n++)
        {
            for (int i = 0; i < toneSamples; i++)
            {
                double env = i < fade ? (double)i / fade
                           : i >= toneSamples - fade ? (double)(toneSamples - 1 - i) / fade
                           : 1.0;
                short v = (short)(Math.Sin(2 * Math.PI * freq * i / SampleRate) * env * amplitude * short.MaxValue);
                pcm[2 * pos] = (byte)(v & 0xFF);
                pcm[2 * pos + 1] = (byte)((v >> 8) & 0xFF);
                pos++;
            }
            if (n < count - 1) pos += gapSamples;
        }
        return pcm;
    }
}
