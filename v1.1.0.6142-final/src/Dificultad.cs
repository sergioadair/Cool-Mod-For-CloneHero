using System;
using System.Collections.Generic;
using System.Globalization;
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

        // CALIBRACION DEL PERFIL, medida sobre una biblioteca de 3631
        // canciones. El objetivo de cada barra es el mismo que el de la nota
        // global: mediana cerca de 45 y menos del 1 % topando en 100.
        //
        //   - Resistencia no puede usar ReferenceNps. La densidad MEDIANA de
        //     un chart es estructuralmente mucho menor que su pico, asi que con
        //     la misma referencia jamas pasaba de 74 y se amontonaba en 29.
        //     Con 0,65 la mediana sube a 45 y el reparto queda como el resto.
        //   - Tecnica topaba en el 3,6 % de las canciones con 1,5 trastes; 1,8
        //     baja el p95 de 94 a 78 y deja recorrido arriba.
        //   - Acordes desaprovechaba la mitad alta de la barra; 1,2 sube la
        //     mediana de 28 a 35.
        public const double ReferenciaResistencia = 0.65;
        public const double TopeAcordes = 1.2;
        public const double TopeTecnica = 1.8;

        public const float AverageWeight = 0.25f;
        public const float MaxWeight = 0.75f;
        public const double PeakWindow = 10.0;
        public const string IniKey = "diff_global";

        // Version del perfil. Si se toca cualquier formula hay que subirla:
        // una cancion cuyo song.ini traiga otra version no cuenta como
        // calculada, asi que se rehace sola en la siguiente pasada.
        public const int PerfilVersion = 2;

        // Todo lo que el mod escribe en song.ini. Se listan juntas porque al
        // reescribir hay que quitar las que ya estuvieran, y porque asi se ve
        // de un vistazo lo que ensuciamos del archivo del usuario.
        public static readonly string[] ClavesIni =
        {
            IniKey,
            "diff_speed", "diff_chords", "diff_tech", "diff_endurance",
            "diff_notes", "diff_nps_avg", "diff_nps_max", "diff_peak_at",
            "diff_prof_v"
        };

        // ------------------------------------------------------------ datos --
        private class ChartInfo
        {
            public int resolution = 192;
            public List<long> tempoTick = new List<long>();
            public List<double> tempoBpm = new List<double>();

            // instrumento -> dificultad(0..3) -> tick -> mascara de trastes
            //
            // La mascara es un bit por traste. Antes esto era un HashSet de
            // ticks: para la dificultad basta con saber que HAY nota, porque un
            // acorde se cuenta como una sola (penalizarlos empeora la
            // correlacion con diff_guitar). Pero el perfil necesita saber
            // CUANTAS notas y CUALES: sin eso no hay metrica de acordes ni de
            // desplazamiento de la mano.
            public Dictionary<string, Dictionary<int, Dictionary<long, int>>> tracks =
                new Dictionary<string, Dictionary<int, Dictionary<long, int>>>();

            public void Add(string inst, int diff, long tick, int fret)
            {
                if (!tracks.TryGetValue(inst, out Dictionary<int, Dictionary<long, int>> byDiff))
                {
                    byDiff = new Dictionary<int, Dictionary<long, int>>();
                    tracks[inst] = byDiff;
                }
                if (!byDiff.TryGetValue(diff, out Dictionary<long, int> notas))
                {
                    notas = new Dictionary<long, int>();
                    byDiff[diff] = notas;
                }
                if (fret < 0 || fret > 7)
                {
                    fret = 7;      // nota abierta y cualquier cosa rara
                }
                notas.TryGetValue(tick, out int mascara);
                notas[tick] = mascara | (1 << fret);
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
                info.Add(inst, diff, tick, fret);
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
                        info.Add(inst, dfi, noteTick[k], n - ranges[dfi * 2]);
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
        public static bool Calcular(string chartPath, bool esMidi, out Perfil perfil)
        {
            perfil = null;
            ChartInfo info = esMidi ? ParseMidi(chartPath) : ParseChart(chartPath);
            if (info == null || info.tracks.Count == 0)
            {
                return false;
            }

            TimeMap map = new TimeMap(info);
            List<double> instNps = new List<double>();
            double npsMax = 0.0;

            // El chart mas dificil de toda la cancion: es el que manda en la
            // nota global y del que se saca el perfil. No tiene sentido
            // perfilar la media de Easy y Expert.
            double[] tiemposDuros = null;
            int[] mascarasDuras = null;

            foreach (KeyValuePair<string, Dictionary<int, Dictionary<long, int>>> inst in info.tracks)
            {
                // NPS de cada dificultad presente, en orden ascendente
                List<double> ordered = new List<double>();
                for (int d = 0; d < 4; d++)
                {
                    if (!inst.Value.TryGetValue(d, out Dictionary<long, int> notas)
                        || notas.Count < 2)
                    {
                        continue;
                    }
                    List<long> ticks = new List<long>(notas.Keys);
                    ticks.Sort();
                    double[] times = new double[ticks.Count];
                    int[] mascaras = new int[ticks.Count];
                    for (int t = 0; t < ticks.Count; t++)
                    {
                        times[t] = map.ToSeconds(ticks[t]);
                        mascaras[t] = notas[ticks[t]];
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
                        tiemposDuros = times;
                        mascarasDuras = mascaras;
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

            perfil = new Perfil();
            perfil.global = Escalar(npsGlobal);
            Perfilar(perfil, tiemposDuros, mascarasDuras);
            return true;
        }

        // Todo lo que sabemos de una cancion. El global se calcula como
        // siempre; el resto describe COMO es dificil, no cuanto.
        public class Perfil
        {
            public int global;
            public int velocidad;      // que tan denso llega a ponerse
            public int acordes;        // cuanto se toca de mas de una nota
            public int tecnica;        // cuanto se mueve la mano
            public int resistencia;    // cuanto AGUANTA siendo denso
            public int notas;          // notas reales, acordes incluidos
            public double npsMedio;
            public double npsMax;
            public int picoSegundo;    // donde empieza el tramo mas denso
        }

        // 0..100 sobre la misma referencia que la nota global.
        private static int Escalar(double nps)
        {
            double v = nps / ReferenceNps * 100.0;
            if (v < 0.0) { v = 0.0; }
            if (v > 100.0) { v = 100.0; }
            return (int)Math.Round(v, MidpointRounding.AwayFromZero);
        }

        private static int Escalar100(double proporcion, double tope)
        {
            double v = proporcion / tope * 100.0;
            if (v < 0.0) { v = 0.0; }
            if (v > 100.0) { v = 100.0; }
            return (int)Math.Round(v, MidpointRounding.AwayFromZero);
        }

        // Densidad a lo largo del tiempo: cuantas notas por segundo hay en una
        // ventana que va deslizandose. De aqui salen tres cosas de golpe — el
        // pico (velocidad), la mediana (resistencia) y DONDE esta el pico, que
        // hasta ahora se calculaba y se tiraba.
        private static double[] SerieNps(double[] times, double ventana,
                                         double paso, out int indiceMax)
        {
            indiceMax = 0;
            double duracion = times[times.Length - 1] - times[0];
            int n = (int)(duracion / paso) + 1;
            if (n < 1)
            {
                n = 1;
            }
            double[] serie = new double[n];
            int desde = 0;
            int hasta = 0;
            double mejor = -1.0;
            for (int i = 0; i < n; i++)
            {
                double t0 = times[0] + i * paso;
                double t1 = t0 + ventana;
                while (desde < times.Length && times[desde] < t0) { desde++; }
                while (hasta < times.Length && times[hasta] < t1) { hasta++; }
                serie[i] = (hasta - desde) / ventana;
                if (serie[i] > mejor)
                {
                    mejor = serie[i];
                    indiceMax = i;
                }
            }
            return serie;
        }

        // Las cuatro barras del perfil, sobre el chart mas dificil.
        private static void Perfilar(Perfil perfil, double[] times, int[] mascaras)
        {
            if (times == null || mascaras == null || times.Length < 2)
            {
                return;
            }
            double duracion = times[times.Length - 1] - times[0];
            if (duracion <= 0.0)
            {
                return;
            }

            double[] serie = SerieNps(times, PeakWindow, 1.0, out int indiceMax);
            double[] ordenada = (double[])serie.Clone();
            Array.Sort(ordenada);

            // VELOCIDAD es el pico y RESISTENCIA la mediana, sobre la misma
            // escala. Se complementan: una cancion tranquila con un solo
            // brutal da velocidad alta y resistencia baja; un tema rapido de
            // principio a fin da las dos altas.
            perfil.velocidad = Escalar(serie[indiceMax]);
            perfil.resistencia = Escalar100(ordenada[ordenada.Length / 2],
                                            ReferenceNps * ReferenciaResistencia);
            perfil.picoSegundo = (int)(times[0] + indiceMax);
            perfil.npsMax = serie[indiceMax];
            perfil.npsMedio = times.Length / duracion;

            long extra = 0;      // notas de mas alla de la primera de cada golpe
            long total = 0;
            double movimiento = 0.0;
            double centroAnterior = -1.0;
            for (int i = 0; i < mascaras.Length; i++)
            {
                int cuantas = Bits(mascaras[i]);
                total += cuantas;
                extra += cuantas - 1;

                double centro = Centro(mascaras[i]);
                if (centroAnterior >= 0.0)
                {
                    movimiento += Math.Abs(centro - centroAnterior);
                }
                centroAnterior = centro;
            }
            perfil.notas = (int)total;

            // ACORDES: notas de mas por golpe. Todo notas sueltas da 0; todo
            // acordes de dos, 1; de tres, 2. Se topa en 1,5 porque un chart
            // entero de acordes de tres no existe.
            perfil.acordes = Escalar100((double)extra / mascaras.Length, TopeAcordes);

            // TECNICA: cuanto se desplaza la mano de un golpe al siguiente,
            // medido entre los centros de cada acorde. Un traste y medio de
            // media ya es un chart muy movido.
            perfil.tecnica = Escalar100(movimiento / (mascaras.Length - 1), TopeTecnica);
        }

        private static int Bits(int mascara)
        {
            int n = 0;
            while (mascara != 0)
            {
                mascara &= mascara - 1;
                n++;
            }
            return n;
        }

        // Centro de un acorde, en trastes. La nota abierta (bit 7) cuenta como
        // el traste 0: la mano no esta en ningun sitio concreto.
        private static double Centro(int mascara)
        {
            double suma = 0.0;
            int n = 0;
            for (int b = 0; b < 6; b++)
            {
                if ((mascara & (1 << b)) != 0)
                {
                    suma += b;
                    n++;
                }
            }
            return n == 0 ? 0.0 : suma / n;
        }

        // ----------------------------------------------------------- song.ini -
        // A nivel de bytes a proposito: en la biblioteca de referencia 56
        // song.ini no son UTF-8 valido, y reescribirlos como texto corrompe los
        // acentos de forma irreversible.
        public static void EscribirIni(string path, Perfil perfil)
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
                if (!EsClaveNuestra(lines[j]))
                {
                    kept.Add(lines[j]);
                }
            }
            while (kept.Count > 0 && EsBlanca(kept[kept.Count - 1]))
            {
                kept.RemoveAt(kept.Count - 1);
            }
            // Todo de una pasada: reescribir el archivo una vez por clave
            // multiplicaria por nueve el trabajo de disco de un calculo que ya
            // recorre miles de canciones.
            Anadir(kept, IniKey, perfil.global);
            Anadir(kept, "diff_speed", perfil.velocidad);
            Anadir(kept, "diff_chords", perfil.acordes);
            Anadir(kept, "diff_tech", perfil.tecnica);
            Anadir(kept, "diff_endurance", perfil.resistencia);
            Anadir(kept, "diff_notes", perfil.notas);
            Anadir(kept, "diff_nps_avg", Texto(perfil.npsMedio));
            Anadir(kept, "diff_nps_max", Texto(perfil.npsMax));
            Anadir(kept, "diff_peak_at", perfil.picoSegundo);
            Anadir(kept, "diff_prof_v", PerfilVersion);

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
        // Cache de perfiles por ruta de song.ini.
        //
        // La leen la etiqueta del panel de detalles y el panel de perfil. Antes
        // cada una abria el archivo por su cuenta —el mismo archivo— y ademas
        // el panel lo hacia en el instante de abrirse, que es justo cuando se
        // nota. Con esto se lee una vez por cancion resaltada y al abrir el
        // panel ya no hay disco de por medio.
        private static readonly Dictionary<string, Perfil> cachePerfiles =
            new Dictionary<string, Perfil>(StringComparer.OrdinalIgnoreCase);

        public static Perfil PerfilDe(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            if (cachePerfiles.TryGetValue(path, out Perfil p))
            {
                return p;
            }
            p = LeerPerfil(path);
            cachePerfiles[path] = p;
            return p;
        }

        // Tras recalcular, lo guardado ya no vale.
        public static void OlvidarPerfiles()
        {
            cachePerfiles.Clear();
        }

        // Lee de vuelta lo que escribimos. Devuelve null si la cancion no
        // tiene perfil todavia.
        public static Perfil LeerPerfil(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }
                string texto = Encoding.UTF8.GetString(File.ReadAllBytes(path));
                string[] lineas = texto.Split((char)10);
                Perfil p = new Perfil();
                bool alguna = false;
                for (int i = 0; i < lineas.Length; i++)
                {
                    int eq = lineas[i].IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    string clave = lineas[i].Substring(0, eq).Trim().ToLowerInvariant();
                    string valor = lineas[i].Substring(eq + 1).Trim();
                    switch (clave)
                    {
                        case IniKey: p.global = Entero(valor); alguna = true; break;
                        case "diff_chords": p.acordes = Entero(valor); break;
                        case "diff_tech": p.tecnica = Entero(valor); break;
                        case "diff_endurance": p.resistencia = Entero(valor); break;
                        case "diff_notes": p.notas = Entero(valor); break;
                        case "diff_peak_at": p.picoSegundo = Entero(valor); break;
                        case "diff_nps_avg": p.npsMedio = Decimal(valor); break;
                        case "diff_nps_max": p.npsMax = Decimal(valor); break;
                    }
                }
                return alguna ? p : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int Entero(string v)
        {
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out int n) ? n : 0;
        }

        // Con punto o con coma: hay song.ini escritos con la region del sistema.
        private static double Decimal(string v)
        {
            return double.TryParse(v.Replace(',', '.'), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out double d) ? d : 0.0;
        }

        // Al dia solo si el song.ini trae el perfil Y de la version actual.
        // Con solo diff_global no basta: se calculo con una version anterior.
        public static bool TienePerfil(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }
                byte[] raw = File.ReadAllBytes(path);
                string text = Encoding.UTF8.GetString(raw);
                string[] lines = text.Split((char)10);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!EsLineaDeClave(Encoding.UTF8.GetBytes(lines[i]), "diff_prof_v"))
                    {
                        continue;
                    }
                    int eq = lines[i].IndexOf('=');
                    return eq > 0
                        && int.TryParse(lines[i].Substring(eq + 1).Trim(), out int v)
                        && v == PerfilVersion;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

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

        private static void Anadir(List<byte[]> destino, string clave, int valor)
        {
            Anadir(destino, clave, valor.ToString(CultureInfo.InvariantCulture));
        }

        private static void Anadir(List<byte[]> destino, string clave, string valor)
        {
            destino.Add(Encoding.ASCII.GetBytes(clave + " = " + valor));
        }

        // Un decimal basta, y con punto siempre: el juego escribe algunos
        // valores con la coma de la region y luego no los sabe releer.
        private static string Texto(double v)
        {
            return v.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static bool EsClaveNuestra(byte[] line)
        {
            for (int i = 0; i < ClavesIni.Length; i++)
            {
                if (EsLineaDeClave(line, ClavesIni[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool EsLineaDeClave(byte[] line, string IniKey)
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
