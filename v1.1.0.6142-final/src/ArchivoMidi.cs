using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CloneHeroMod
{
    // Lectura y escritura de .mid CONSERVANDO el archivo.
    //
    // Es la parte delicada de todo esto: un .mid de Guitar Hero no lleva solo
    // las notas. Lleva la letra, las secciones, la pista de camara, los eventos
    // del escenario, las animaciones de los personajes... Si se generase un
    // archivo nuevo con las notas y ya, la cancion perderia la mitad de lo que
    // tiene. Asi que se lee TODO, se anaden eventos, y se vuelve a serializar.
    //
    // Cada evento se guarda con su tiempo absoluto y sus bytes tal cual. Al
    // leer se deshace el "running status" (un evento puede omitir su cabecera y
    // heredar la del anterior) porque si no, insertar algo en medio corrompe
    // todo lo que venga detras. Al escribir no se vuelve a usar: es legal y el
    // archivo queda un poco mas grande, nada mas.
    //
    // Convenio de las notas, el mismo que ya usa Dificultad.cs:
    //     Easy 60-64   Medium 72-76   Hard 84-88   Expert 96-100
    // y dentro de cada rango los cinco trastes en orden.
    public class ArchivoMidi
    {
        public class Evento
        {
            public long tick;
            public int orden;          // desempate: conserva el orden original
            public byte[] datos;
        }

        public class Pista
        {
            public string nombre = "";
            public List<Evento> eventos = new List<Evento>();
        }

        public int formato = 1;
        public int division = 480;
        public List<Pista> pistas = new List<Pista>();

        public static readonly int[] BaseDificultad = { 60, 72, 84, 96 };

        // ------------------------------------------------------------ leer --
        public static ArchivoMidi Leer(string ruta)
        {
            byte[] d = File.ReadAllBytes(ruta);
            ArchivoMidi m = new ArchivoMidi();
            if (d.Length < 14 || d[0] != 'M' || d[1] != 'T' || d[2] != 'h' || d[3] != 'd')
            {
                throw new InvalidDataException("no es un MIDI");
            }
            int largo = Int32BE(d, 4);
            m.formato = (d[8] << 8) | d[9];
            m.division = (d[12] << 8) | d[13];
            int i = 8 + largo;

            while (i + 8 <= d.Length)
            {
                if (d[i] != 'M' || d[i + 1] != 'T' || d[i + 2] != 'r' || d[i + 3] != 'k')
                {
                    break;
                }
                int n = Int32BE(d, i + 4);
                int fin = i + 8 + n;
                if (fin > d.Length)
                {
                    fin = d.Length;
                }
                m.pistas.Add(LeerPista(d, i + 8, fin));
                i = fin;
            }
            return m;
        }

        private static Pista LeerPista(byte[] d, int i, int fin)
        {
            Pista p = new Pista();
            long tick = 0;
            byte estado = 0;
            int orden = 0;
            while (i < fin)
            {
                long delta;
                i = Vlq(d, i, out delta);
                tick += delta;
                if (i >= fin)
                {
                    break;
                }
                int ini = i;
                byte b0 = d[i];

                if (b0 == 0xFF)
                {
                    i++;
                    byte tipo = d[i];
                    i++;
                    long n;
                    i = Vlq(d, i, out n);
                    int inicioDatos = i;
                    i += (int)n;
                    if (tipo == 0x2F)
                    {
                        break;      // fin de pista: se vuelve a poner al escribir
                    }
                    if (tipo == 0x03 && p.nombre.Length == 0)
                    {
                        p.nombre = Encoding.UTF8.GetString(d, inicioDatos, (int)n);
                    }
                    Anadir(p, tick, ref orden, d, ini, i);
                    continue;
                }
                if (b0 == 0xF0 || b0 == 0xF7)
                {
                    i++;
                    long n;
                    i = Vlq(d, i, out n);
                    i += (int)n;
                    Anadir(p, tick, ref orden, d, ini, i);
                    continue;
                }

                // Canal. Si el byte no trae bit alto, hereda el estado anterior
                // y hay que reconstruir el evento entero.
                bool heredado = (b0 & 0x80) == 0;
                if (!heredado)
                {
                    estado = b0;
                    i++;
                }
                int nParams = ((estado & 0xF0) == 0xC0 || (estado & 0xF0) == 0xD0) ? 1 : 2;
                if (i + nParams > fin)
                {
                    break;
                }
                byte[] ev = new byte[1 + nParams];
                ev[0] = estado;
                for (int k = 0; k < nParams; k++)
                {
                    ev[1 + k] = d[i + k];
                }
                i += nParams;
                Evento e = new Evento();
                e.tick = tick;
                e.orden = orden++;
                e.datos = ev;
                p.eventos.Add(e);
            }
            return p;
        }

        private static void Anadir(Pista p, long tick, ref int orden, byte[] d,
                                   int ini, int fin)
        {
            byte[] ev = new byte[fin - ini];
            Array.Copy(d, ini, ev, 0, ev.Length);
            Evento e = new Evento();
            e.tick = tick;
            e.orden = orden++;
            e.datos = ev;
            p.eventos.Add(e);
        }

        // --------------------------------------------------------- escribir --
        public void Escribir(string ruta)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d' }, 0, 4);
                EscribirInt32BE(ms, 6);
                EscribirInt16BE(ms, formato);
                EscribirInt16BE(ms, pistas.Count);
                EscribirInt16BE(ms, division);

                for (int i = 0; i < pistas.Count; i++)
                {
                    byte[] cuerpo = CuerpoPista(pistas[i]);
                    ms.Write(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' }, 0, 4);
                    EscribirInt32BE(ms, cuerpo.Length);
                    ms.Write(cuerpo, 0, cuerpo.Length);
                }
                File.WriteAllBytes(ruta, ms.ToArray());
            }
        }

        private static byte[] CuerpoPista(Pista p)
        {
            List<Evento> ev = new List<Evento>(p.eventos);
            ev.Sort(delegate (Evento a, Evento b)
            {
                if (a.tick != b.tick)
                {
                    return a.tick.CompareTo(b.tick);
                }
                return a.orden.CompareTo(b.orden);
            });
            using (MemoryStream ms = new MemoryStream())
            {
                long previo = 0;
                for (int i = 0; i < ev.Count; i++)
                {
                    EscribirVlq(ms, ev[i].tick - previo);
                    previo = ev[i].tick;
                    ms.Write(ev[i].datos, 0, ev[i].datos.Length);
                }
                EscribirVlq(ms, 0);
                ms.Write(new byte[] { 0xFF, 0x2F, 0x00 }, 0, 3);
                return ms.ToArray();
            }
        }

        // ----------------------------------------------------------- notas --
        public Pista BuscarPista(string nombre)
        {
            for (int i = 0; i < pistas.Count; i++)
            {
                if (string.Equals(pistas[i].nombre.Trim(), nombre,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return pistas[i];
                }
            }
            return null;
        }

        public static List<ReduccionChart.Nota> Notas(Pista p, int dificultad)
        {
            List<ReduccionChart.Nota> salida = new List<ReduccionChart.Nota>();
            if (p == null || dificultad < 0 || dificultad > 3)
            {
                return salida;
            }
            int b = BaseDificultad[dificultad];
            Dictionary<long, int> mascara = new Dictionary<long, int>();
            Dictionary<long, long> inicio = new Dictionary<long, long>();
            Dictionary<int, long> abiertas = new Dictionary<int, long>();
            Dictionary<long, long> largo = new Dictionary<long, long>();

            List<Evento> ev = new List<Evento>(p.eventos);
            ev.Sort(delegate (Evento x, Evento y)
            {
                if (x.tick != y.tick)
                {
                    return x.tick.CompareTo(y.tick);
                }
                return x.orden.CompareTo(y.orden);
            });

            for (int i = 0; i < ev.Count; i++)
            {
                byte[] d = ev[i].datos;
                if (d.Length < 3)
                {
                    continue;
                }
                int tipo = d[0] & 0xF0;
                if (tipo != 0x90 && tipo != 0x80)
                {
                    continue;
                }
                int nota = d[1] - b;
                if (nota < 0 || nota > 4)
                {
                    continue;
                }
                bool on = tipo == 0x90 && d[2] > 0;
                if (on)
                {
                    int m;
                    mascara.TryGetValue(ev[i].tick, out m);
                    mascara[ev[i].tick] = m | (1 << nota);
                    abiertas[nota] = ev[i].tick;
                    if (!inicio.ContainsKey(ev[i].tick))
                    {
                        inicio[ev[i].tick] = ev[i].tick;
                    }
                }
                else
                {
                    long t0;
                    if (abiertas.TryGetValue(nota, out t0))
                    {
                        long dur = ev[i].tick - t0;
                        long v;
                        if (!largo.TryGetValue(t0, out v) || dur > v)
                        {
                            largo[t0] = dur;
                        }
                        abiertas.Remove(nota);
                    }
                }
            }

            List<long> ticks = new List<long>(mascara.Keys);
            ticks.Sort();
            for (int i = 0; i < ticks.Count; i++)
            {
                ReduccionChart.Nota n = new ReduccionChart.Nota();
                n.tick = ticks[i];
                n.trastes = mascara[ticks[i]];
                long v;
                n.sostenido = largo.TryGetValue(ticks[i], out v) ? v : 0;
                salida.Add(n);
            }
            return salida;
        }

        // Quita las notas de una dificultad y pone las nuevas. Solo toca el
        // rango de esa dificultad: lo que haya alrededor —star power, marcas,
        // animaciones— se queda donde estaba.
        public static void PonerNotas(Pista p, int dificultad,
                                      List<ReduccionChart.Nota> notas, int division)
        {
            if (p == null || dificultad < 0 || dificultad > 3)
            {
                return;
            }
            int b = BaseDificultad[dificultad];
            p.eventos.RemoveAll(delegate (Evento e)
            {
                if (e.datos.Length < 3)
                {
                    return false;
                }
                int tipo = e.datos[0] & 0xF0;
                if (tipo != 0x90 && tipo != 0x80)
                {
                    return false;
                }
                int d = e.datos[1] - b;
                return d >= 0 && d <= 4;
            });

            int orden = 1000000;      // detras de lo original en cada tiempo
            long minima = division / 8;
            if (minima < 1)
            {
                minima = 1;
            }
            for (int i = 0; i < notas.Count; i++)
            {
                long dur = notas[i].sostenido;
                // Un sostenido demasiado corto en el chart de origen se queda
                // en nota normal; el juego pinta cola a partir de cierto largo.
                if (dur < minima)
                {
                    dur = minima;
                }
                // y nunca puede solaparse con la siguiente
                if (i + 1 < notas.Count)
                {
                    long hueco = notas[i + 1].tick - notas[i].tick - 1;
                    if (hueco < minima)
                    {
                        hueco = minima;
                    }
                    if (dur > hueco)
                    {
                        dur = hueco;
                    }
                }
                for (int t = 0; t < 5; t++)
                {
                    if ((notas[i].trastes & (1 << t)) == 0)
                    {
                        continue;
                    }
                    Evento on = new Evento();
                    on.tick = notas[i].tick;
                    on.orden = orden++;
                    on.datos = new byte[] { 0x90, (byte)(b + t), 100 };
                    p.eventos.Add(on);

                    Evento off = new Evento();
                    off.tick = notas[i].tick + dur;
                    off.orden = orden++;
                    off.datos = new byte[] { 0x80, (byte)(b + t), 0 };
                    p.eventos.Add(off);
                }
            }
        }

        // ---------------------------------------------------------- ayudas --
        private static int Int32BE(byte[] d, int i)
        {
            return (d[i] << 24) | (d[i + 1] << 16) | (d[i + 2] << 8) | d[i + 3];
        }

        private static int Vlq(byte[] d, int i, out long valor)
        {
            valor = 0;
            while (i < d.Length)
            {
                byte c = d[i++];
                valor = (valor << 7) | (uint)(c & 0x7F);
                if ((c & 0x80) == 0)
                {
                    break;
                }
            }
            return i;
        }

        private static void EscribirVlq(Stream s, long v)
        {
            if (v < 0)
            {
                v = 0;
            }
            byte[] buf = new byte[5];
            int n = 0;
            buf[n++] = (byte)(v & 0x7F);
            v >>= 7;
            while (v > 0)
            {
                buf[n++] = (byte)((v & 0x7F) | 0x80);
                v >>= 7;
            }
            for (int i = n - 1; i >= 0; i--)
            {
                s.WriteByte(buf[i]);
            }
        }

        private static void EscribirInt32BE(Stream s, int v)
        {
            s.WriteByte((byte)(v >> 24));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        private static void EscribirInt16BE(Stream s, int v)
        {
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }
    }
}
