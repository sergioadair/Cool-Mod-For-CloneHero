using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CloneHeroMod
{
    // Calculo de la dificultad global 0-100 de una cancion.
    //
    // Portado tal cual desde la version de dnSpy (v1.0.0.4080): es logica pura,
    // no toca el juego ni Unity, asi que puede correr en un hilo aparte. Se
    // quitaron los rodeos que exigia el compilador de dnSpy (File.ReadAllLines,
    // Array.Sort de un argumento, Math.Round...) porque aqui ya no aplican.
    //
    // La formula y su calibracion estan documentadas en REPLICAR-MOD.md §11:
    // pico de notas por segundo en ventana de 10 s, acordes = 1 nota, 75 % del
    // peso al chart mas duro y 25 % al promedio de instrumentos, 14 NPS = 100.
    public static class Dificultad
    {
        // NPS que equivale a 100/100. Configurable desde settings.ini, seccion
        // [mods], clave difficulty_reference_nps. Subirlo comprime la escala
        // (todo baja), bajarlo la expande. Referencia medida sobre la
        // biblioteca grande: con 14 la mediana queda en 42 y solo el 0,4 % de
        // las canciones topa en 100.
        public static float ReferenceNps { get { return Ajustes.ReferenceNps; } }

        public const float AverageWeight = 0.25f;
        public const float MaxWeight = 0.75f;
        public const double PeakWindow = 10.0;
        public const string IniKey = "diff_global";

        // ------------------------------------------------------------ datos --
        private class ChartInfo
        {
            public int resolution = 192;
            public List<long> tempoTick = new List<long>();
            public List<double> tempoBpm = new List<double>();

            // instrumento -> dificultad(0..3) -> ticks con nota
            public Dictionary<string, Dictionary<int, HashSet<long>>> tracks =
                new Dictionary<string, Dictionary<int, HashSet<long>>>();

            public void Add(string inst, int diff, long tick)
            {
                if (!tracks.TryGetValue(inst, out Dictionary<int, HashSet<long>> byDiff))
                {
                    byDiff = new Dictionary<int, HashSet<long>>();
                    tracks[inst] = byDiff;
                }
                if (!byDiff.TryGetValue(diff, out HashSet<long> set))
                {
                    set = new HashSet<long>();
                    byDiff[diff] = set;
                }
                // Un acorde comparte tick, asi que el HashSet lo cuenta una vez.
                // Es intencionado: penalizar acordes empeora la correlacion.
                set.Add(tick);
            }
        }

        // ----------------------------------------------------------- .chart --
        private static readonly string[] ChartDiffNames = { "Easy", "Medium", "Hard", "Expert" };

        private static readonly string[] ChartInstruments =
        {
            "Single", "DoubleGuitar", "DoubleBass", "DoubleRhythm",
            "Drums", "Keyboard", "GHLGuitar", "GHLBass"
        };

        private static ChartInfo ParseChart(string path)
        {
            string[] lines;
            try
            {
                byte[] raw = File.ReadAllBytes(path);
                string text = Encoding.UTF8.GetString(raw, 0, raw.Length);
                if (text.Length > 0 && text[0] == '﻿')
                {
                    text = text.Substring(1);   // el BOM romperia la deteccion de [Song]
                }
                lines = text.Split('\n');
            }
            catch (Exception)
            {
                return null;
            }

            ChartInfo info = new ChartInfo();
            int mode = 0;           // 0=nada 1=Song 2=SyncTrack 3=notas
            string inst = null;
            int diff = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line == "{" || line == "}")
                {
                    continue;
                }
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    string section = line.Substring(1, line.Length - 2);
                    mode = 0;
                    inst = null;
                    diff = -1;
                    if (section == "Song")
                    {
                        mode = 1;
                    }
                    else if (section == "SyncTrack")
                    {
                        mode = 2;
                    }
                    else
                    {
                        for (int d = 0; d < ChartDiffNames.Length; d++)
                        {
                            if (!section.StartsWith(ChartDiffNames[d], StringComparison.Ordinal))
                            {
                                continue;
                            }
                            string rest = section.Substring(ChartDiffNames[d].Length);
                            for (int k = 0; k < ChartInstruments.Length; k++)
                            {
                                if (rest == ChartInstruments[k])
                                {
                                    mode = 3;
                                    inst = rest;
                                    diff = d;
                                    break;
                                }
                            }
                            break;
                        }
                    }
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }
                if (mode == 1)
                {
                    string key = line.Substring(0, eq).Trim();
                    if (string.Equals(key, "Resolution", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(line.Substring(eq + 1).Trim(), out int res) && res > 0)
                    {
                        info.resolution = res;
                    }
                    continue;
                }
                if (mode != 2 && mode != 3)
                {
                    continue;
                }
                if (!long.TryParse(line.Substring(0, eq).Trim(), out long tick))
                {
                    continue;
                }
                string[] parts = line.Substring(eq + 1)
                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }
                if (mode == 2)
                {
                    if (parts[0] == "B" && long.TryParse(parts[1], out long milli) && milli > 0)
                    {
                        info.tempoTick.Add(tick);
                        info.tempoBpm.Add(milli / 1000.0);
                    }
                    continue;
                }
                if (parts[0] != "N" || !int.TryParse(parts[1], out int fret))
                {
                    continue;
                }
                if (fret == 5 || fret == 6)
                {
                    continue;      // 5 = forced, 6 = tap: modificadores, no notas
                }
                info.Add(inst, diff, tick);
            }
            return info;
        }

        // ------------------------------------------------------------- .mid --
        private static readonly int[] Range5Fret = { 60, 64, 72, 76, 84, 88, 96, 100 };
        private static readonly int[] RangeDrums = { 60, 65, 72, 77, 84, 89, 96, 101 };
        private static readonly int[] RangeGhl = { 58, 64, 70, 76, 82, 88, 94, 100 };

        private static bool MidiTrackInfo(string name, out string inst, out int[] ranges)
        {
            inst = null;
            ranges = null;
            if (name == null)
            {
                return false;
            }
            switch (name.Trim().ToUpperInvariant())
            {
                case "PART GUITAR":
                case "T1 GEMS":
                    inst = "Single"; ranges = Range5Fret; break;
                case "PART GUITAR COOP":
                    inst = "DoubleGuitar"; ranges = Range5Fret; break;
                case "PART BASS":
                    inst = "DoubleBass"; ranges = Range5Fret; break;
                case "PART RHYTHM":
                    inst = "DoubleRhythm"; ranges = Range5Fret; break;
                case "PART KEYS":
                    inst = "Keyboard"; ranges = Range5Fret; break;
                case "PART DRUMS":
                    inst = "Drums"; ranges = RangeDrums; break;
                case "PART GUITAR GHL":
                    inst = "GHLGuitar"; ranges = RangeGhl; break;
                case "PART BASS GHL":
                    inst = "GHLBass"; ranges = RangeGhl; break;
            }
            return inst != null;
        }

        private static int ReadInt32BE(byte[] d, int i)
        {
            return (d[i] << 24) | (d[i + 1] << 16) | (d[i + 2] << 8) | d[i + 3];
        }

        private static ChartInfo ParseMidi(string path)
        {
            byte[] d;
            try
            {
                d = File.ReadAllBytes(path);
            }
            catch (Exception)
            {
                return null;
            }
            if (d.Length < 14 || d[0] != 77 || d[1] != 84 || d[2] != 104 || d[3] != 100)
            {
                return null;      // no es "MThd"
            }
            int headerLen = ReadInt32BE(d, 4);
            int division = (d[12] << 8) | d[13];
            if ((division & 0x8000) != 0 || division == 0)
            {
                return null;      // SMPTE no soportado
            }

            ChartInfo info = new ChartInfo { resolution = division };
            int pos = 8 + headerLen;
            while (pos + 8 <= d.Length)
            {
                if (d[pos] != 77 || d[pos + 1] != 84 || d[pos + 2] != 114 || d[pos + 3] != 107)
                {
                    break;        // no es "MTrk"
                }
                int len = ReadInt32BE(d, pos + 4);
                int start = pos + 8;
                long endL = (long)start + len;
                int end = (endL > d.Length || len < 0) ? d.Length : (int)endL;
                ParseMidiTrack(d, start, end, info);
                pos = end;
            }
            return info;
        }

        private static void ParseMidiTrack(byte[] d, int start, int end, ChartInfo info)
        {
            int i = start;
            long tick = 0L;
            int running = -1;
            string name = null;
            List<long> noteTick = new List<long>();
            List<int> noteNum = new List<int>();

            while (i < end)
            {
                long delta = 0L;
                while (i < end)
                {
                    byte b2 = d[i];
                    i++;
                    delta = (delta << 7) | (long)(b2 & 0x7F);
                    if ((b2 & 0x80) == 0)
                    {
                        break;
                    }
                }
                tick += delta;
                if (i >= end)
                {
                    break;
                }

                int b = d[i];
                if (b == 0xFF)
                {
                    i++;
                    if (i >= end) { break; }
                    int mtype = d[i];
                    i++;
                    long mlen = 0L;
                    while (i < end)
                    {
                        byte c = d[i];
                        i++;
                        mlen = (mlen << 7) | (long)(c & 0x7F);
                        if ((c & 0x80) == 0)
                        {
                            break;
                        }
                    }
                    int payload = i;
                    i += (int)mlen;
                    if (i > end)
                    {
                        break;
                    }
                    if (mtype == 0x03 && name == null && mlen > 0L)
                    {
                        name = Encoding.ASCII.GetString(d, payload, (int)mlen);
                    }
                    else if (mtype == 0x51 && mlen == 3L)
                    {
                        int mpqn = (d[payload] << 16) | (d[payload + 1] << 8) | d[payload + 2];
                        if (mpqn > 0)
                        {
                            info.tempoTick.Add(tick);
                            info.tempoBpm.Add(60000000.0 / mpqn);
                        }
                    }
                    running = -1;
                    continue;
                }
                if (b == 0xF0 || b == 0xF7)
                {
                    i++;
                    long slen = 0L;
                    while (i < end)
                    {
                        byte c = d[i];
                        i++;
                        slen = (slen << 7) | (long)(c & 0x7F);
                        if ((c & 0x80) == 0)
                        {
                            break;
                        }
                    }
                    i += (int)slen;
                    running = -1;
                    continue;
                }

                int status;
                if ((b & 0x80) != 0)
                {
                    status = b;
                    i++;
                    running = status;
                }
                else
                {
                    status = running;
                    if (status < 0)
                    {
                        break;
                    }
                }
                int hi = status & 0xF0;
                if (hi == 0x80 || hi == 0x90 || hi == 0xA0 || hi == 0xB0 || hi == 0xE0)
                {
                    if (i + 1 >= end)
                    {
                        break;
                    }
                    int d1 = d[i];
                    int d2 = d[i + 1];
                    i += 2;
                    if (hi == 0x90 && d2 > 0)
                    {
                        noteTick.Add(tick);
                        noteNum.Add(d1);
                    }
                }
                else if (hi == 0xC0 || hi == 0xD0)
                {
                    i++;
                }
                else
                {
                    break;
                }
            }

            if (!MidiTrackInfo(name, out string inst, out int[] ranges))
            {
                return;
            }
            for (int k = 0; k < noteTick.Count; k++)
            {
                int n = noteNum[k];
                for (int dfi = 0; dfi < 4; dfi++)
                {
                    if (n >= ranges[dfi * 2] && n <= ranges[dfi * 2 + 1])
                    {
                        info.Add(inst, dfi, noteTick[k]);
                        break;
                    }
                }
            }
        }

        // ----------------------------------------------- tick -> segundo -----
        private class TimeMap
        {
            private readonly long[] tick;
            private readonly double[] sec;
            private readonly double[] bpm;
            private readonly int resolution;

            public TimeMap(ChartInfo info)
            {
                resolution = info.resolution;
                int n = info.tempoTick.Count;
                long[] tk = new long[n];
                double[] bp = new double[n];
                for (int i = 0; i < n; i++)
                {
                    tk[i] = info.tempoTick[i];
                    bp[i] = info.tempoBpm[i];
                }
                Array.Sort(tk, bp);

                List<long> ticks = new List<long>();
                List<double> bpms = new List<double>();
                for (int j = 0; j < n; j++)
                {
                    if (ticks.Count > 0 && ticks[ticks.Count - 1] == tk[j])
                    {
                        bpms[bpms.Count - 1] = bp[j];
                    }
                    else
                    {
                        ticks.Add(tk[j]);
                        bpms.Add(bp[j]);
                    }
                }
                if (ticks.Count == 0 || ticks[0] != 0L)
                {
                    ticks.Insert(0, 0L);
                    bpms.Insert(0, bpms.Count > 0 ? bpms[0] : 120.0);
                }

                int m = ticks.Count;
                tick = new long[m];
                bpm = new double[m];
                sec = new double[m];
                double acc = 0.0;
                for (int k = 0; k < m; k++)
                {
                    tick[k] = ticks[k];
                    bpm[k] = bpms[k];
                    if (k > 0)
                    {
                        acc += (tick[k] - tick[k - 1]) * 60.0 / (bpm[k - 1] * resolution);
                    }
                    sec[k] = acc;
                }
            }

            public double ToSeconds(long t)
            {
                int lo = 0;
                int hi = tick.Length - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (tick[mid] <= t)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid - 1;
                    }
                }
                return sec[lo] + (t - tick[lo]) * 60.0 / (bpm[lo] * resolution);
            }
        }

        // Notas del tramo mas denso: el mayor numero de notas que caben en una
        // ventana deslizante de 'window' segundos, dividido por la ventana.
        private static double PeakNps(double[] times, double window)
        {
            if (times.Length < 2)
            {
                return 0.0;
            }
            int best = 0;
            int j = 0;
            for (int i = 0; i < times.Length; i++)
            {
                while (times[i] - times[j] > window)
                {
                    j++;
                }
                int count = i - j + 1;
                if (count > best)
                {
                    best = count;
                }
            }
            return best / window;
        }

        // ----------------------------------------------------------- formula -
        public static bool Calcular(string chartPath, bool esMidi, out int resultado)
        {
            resultado = 0;
            ChartInfo info = esMidi ? ParseMidi(chartPath) : ParseChart(chartPath);
            if (info == null || info.tracks.Count == 0)
            {
                return false;
            }

            TimeMap map = new TimeMap(info);
            List<double> instNps = new List<double>();
            double npsMax = 0.0;

            foreach (KeyValuePair<string, Dictionary<int, HashSet<long>>> inst in info.tracks)
            {
                // NPS de cada dificultad presente, en orden ascendente
                List<double> ordered = new List<double>();
                for (int d = 0; d < 4; d++)
                {
                    if (!inst.Value.TryGetValue(d, out HashSet<long> set) || set.Count < 2)
                    {
                        continue;
                    }
                    List<long> ticks = new List<long>(set);
                    ticks.Sort();
                    double[] times = new double[ticks.Count];
                    for (int t = 0; t < ticks.Count; t++)
                    {
                        times[t] = map.ToSeconds(ticks[t]);
                    }
                    if (times[times.Length - 1] - times[0] <= 0.5)
                    {
                        continue;
                    }
                    double nps = PeakNps(times, PeakWindow);
                    ordered.Add(nps);
                    if (nps > npsMax)
                    {
                        npsMax = nps;
                    }
                }
                if (ordered.Count == 0)
                {
                    continue;
                }
                // pesos 1,2,3... segun la posicion de la dificultad presente:
                // manda Expert, pero las de abajo matizan.
                double num = 0.0;
                double den = 0.0;
                for (int k = 0; k < ordered.Count; k++)
                {
                    double w = k + 1;
                    num += ordered[k] * w;
                    den += w;
                }
                instNps.Add(num / den);
            }

            if (instNps.Count == 0)
            {
                return false;
            }
            double sum = 0.0;
            for (int i = 0; i < instNps.Count; i++)
            {
                sum += instNps[i];
            }
            double npsAvg = sum / instNps.Count;
            double npsGlobal = AverageWeight * npsAvg + MaxWeight * npsMax;
            double scaled = npsGlobal / ReferenceNps * 100.0;

            if (scaled < 0.0) { scaled = 0.0; }
            if (scaled > 100.0) { scaled = 100.0; }
            resultado = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
            return true;
        }

        // ----------------------------------------------------------- song.ini -
        // A nivel de bytes a proposito: en la biblioteca de referencia 56
        // song.ini no son UTF-8 valido, y reescribirlos como texto corrompe los
        // acentos de forma irreversible.
        public static void EscribirIni(string path, int valor)
        {
            byte[] raw = File.ReadAllBytes(path);
            List<byte[]> lines = new List<byte[]>();
            bool crlf = false;
            int start = 0;

            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] != 10)
                {
                    continue;
                }
                int end = i;
                if (end > start && raw[end - 1] == 13)
                {
                    end--;
                    crlf = true;
                }
                byte[] line = new byte[end - start];
                Array.Copy(raw, start, line, 0, end - start);
                lines.Add(line);
                start = i + 1;
            }
            if (start < raw.Length)
            {
                byte[] tail = new byte[raw.Length - start];
                Array.Copy(raw, start, tail, 0, raw.Length - start);
                lines.Add(tail);
            }

            List<byte[]> kept = new List<byte[]>();
            for (int j = 0; j < lines.Count; j++)
            {
                if (!EsLineaDiffGlobal(lines[j]))
                {
                    kept.Add(lines[j]);
                }
            }
            while (kept.Count > 0 && EsBlanca(kept[kept.Count - 1]))
            {
                kept.RemoveAt(kept.Count - 1);
            }
            kept.Add(Encoding.ASCII.GetBytes(IniKey + " = " + valor.ToString()));

            byte[] sep = crlf ? new byte[] { 13, 10 } : new byte[] { 10 };
            using (MemoryStream ms = new MemoryStream())
            {
                for (int k = 0; k < kept.Count; k++)
                {
                    if (k > 0)
                    {
                        ms.Write(sep, 0, sep.Length);
                    }
                    ms.Write(kept[k], 0, kept[k].Length);
                }
                ms.Write(sep, 0, sep.Length);
                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        // Lee diff_global de un song.ini. -1 si no esta.
        public static int LeerIni(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return -1;
                }
                byte[] raw = File.ReadAllBytes(path);
                string text = Encoding.UTF8.GetString(raw, 0, raw.Length);
                string[] lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    if (!string.Equals(line.Substring(0, eq).Trim(), IniKey,
                                       StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    return int.TryParse(line.Substring(eq + 1).Trim(), out int v) ? v : -1;
                }
            }
            catch (Exception)
            {
            }
            return -1;
        }

        private static bool EsBlanca(byte[] line)
        {
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] != 32 && line[i] != 9 && line[i] != 13)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool EsLineaDiffGlobal(byte[] line)
        {
            int i = 0;
            // salta espacios y BOM
            while (i < line.Length && (line[i] == 32 || line[i] == 9
                   || line[i] == 0xEF || line[i] == 0xBB || line[i] == 0xBF))
            {
                i++;
            }
            int k = 0;
            while (i < line.Length && k < IniKey.Length)
            {
                int c = line[i];
                if (c >= 65 && c <= 90)
                {
                    c += 32;      // a minusculas
                }
                if (c != IniKey[k])
                {
                    return false;
                }
                i++;
                k++;
            }
            if (k != IniKey.Length)
            {
                return false;
            }
            while (i < line.Length && (line[i] == 32 || line[i] == 9))
            {
                i++;
            }
            return i < line.Length && line[i] == 61;   // '='
        }
    }
}
