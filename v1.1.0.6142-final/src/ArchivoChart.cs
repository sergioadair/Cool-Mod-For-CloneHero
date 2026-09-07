using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CloneHeroMod
{
    // Lectura y escritura de .chart CONSERVANDO el archivo.
    //
    // Dificultad.cs ya sabe leer .chart, pero para medir: se queda con los
    // tiempos y tira el resto. Aqui hace falta lo contrario — poder devolver el
    // archivo entero con una seccion mas, sin tocarle una coma a lo demas. Por
    // eso cada seccion se guarda como la lista de sus lineas tal cual.
    //
    // El formato es texto plano:
    //
    //     [ExpertSingle]
    //     {
    //       768 = N 0 0
    //       960 = N 2 192
    //     }
    //
    // "N traste sostenido" son notas; los trastes 5 y 6 son marcas de HOPO
    // forzado y tap, el 7 es nota abierta. "S" es star power y "E" un evento.
    public class ArchivoChart
    {
        public class Seccion
        {
            public string nombre;
            public List<string> lineas = new List<string>();
        }

        public List<Seccion> secciones = new List<Seccion>();
        public int resolucion = 192;
        private string finDeLinea = "\r\n";

        public static ArchivoChart Leer(string ruta)
        {
            ArchivoChart a = new ArchivoChart();
            string texto = File.ReadAllText(ruta, DetectarCodificacion(ruta));
            if (texto.IndexOf("\r\n", StringComparison.Ordinal) < 0)
            {
                a.finDeLinea = "\n";
            }
            string[] lineas = texto.Replace("\r\n", "\n").Split('\n');

            Seccion actual = null;
            for (int i = 0; i < lineas.Length; i++)
            {
                string l = lineas[i];
                string t = l.Trim();
                if (t.Length > 2 && t[0] == '[' && t[t.Length - 1] == ']')
                {
                    actual = new Seccion();
                    actual.nombre = t.Substring(1, t.Length - 2);
                    a.secciones.Add(actual);
                    continue;
                }
                if (actual == null || t == "{" || t == "}")
                {
                    continue;
                }
                if (t.Length > 0)
                {
                    actual.lineas.Add(t);
                }
            }

            Seccion song = a.Buscar("Song");
            if (song != null)
            {
                for (int i = 0; i < song.lineas.Count; i++)
                {
                    string l = song.lineas[i];
                    int eq = l.IndexOf('=');
                    if (eq < 0)
                    {
                        continue;
                    }
                    if (l.Substring(0, eq).Trim().Equals("Resolution",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        int v;
                        if (int.TryParse(l.Substring(eq + 1).Trim(),
                                NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                            && v > 0)
                        {
                            a.resolucion = v;
                        }
                    }
                }
            }
            return a;
        }

        // Los .chart suelen venir en UTF-8 con BOM, pero los hay en ANSI. Si no
        // hay BOM se lee como UTF-8 sin lanzar, que degrada mejor.
        private static Encoding DetectarCodificacion(string ruta)
        {
            byte[] cab = new byte[3];
            using (FileStream fs = File.OpenRead(ruta))
            {
                int n = fs.Read(cab, 0, 3);
                if (n == 3 && cab[0] == 0xEF && cab[1] == 0xBB && cab[2] == 0xBF)
                {
                    return new UTF8Encoding(true);
                }
            }
            return new UTF8Encoding(false);
        }

        public Seccion Buscar(string nombre)
        {
            for (int i = 0; i < secciones.Count; i++)
            {
                if (string.Equals(secciones[i].nombre, nombre,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return secciones[i];
                }
            }
            return null;
        }

        // Las notas de una seccion, agrupadas por tiempo. Se ignoran los
        // trastes 5, 6 y 7 (marcas y abiertas): la reduccion no los genera.
        public static List<ReduccionChart.Nota> Notas(Seccion s)
        {
            List<ReduccionChart.Nota> salida = new List<ReduccionChart.Nota>();
            if (s == null)
            {
                return salida;
            }
            Dictionary<long, int> mascara = new Dictionary<long, int>();
            Dictionary<long, long> sostenido = new Dictionary<long, long>();
            for (int i = 0; i < s.lineas.Count; i++)
            {
                long tick;
                int traste;
                long largo;
                if (!LeerNota(s.lineas[i], out tick, out traste, out largo))
                {
                    continue;
                }
                if (traste < 0 || traste > 4)
                {
                    continue;
                }
                int m;
                mascara.TryGetValue(tick, out m);
                mascara[tick] = m | (1 << traste);
                long v;
                if (!sostenido.TryGetValue(tick, out v) || largo > v)
                {
                    sostenido[tick] = largo;
                }
            }
            List<long> ticks = new List<long>(mascara.Keys);
            ticks.Sort();
            for (int i = 0; i < ticks.Count; i++)
            {
                ReduccionChart.Nota n = new ReduccionChart.Nota();
                n.tick = ticks[i];
                n.trastes = mascara[ticks[i]];
                n.sostenido = sostenido[ticks[i]];
                salida.Add(n);
            }
            return salida;
        }

        // Las frases de Star Power de una seccion: lineas "tick = S 2 largo".
        // El tipo 2 es el unico que aparece en los charts oficiales; los otros
        // (0 y 1) son restos de la separacion de jugadores del Guitar Hero
        // viejo y no los usa Clone Hero.
        public static List<ReduccionChart.Fase> Fases(Seccion s)
        {
            List<ReduccionChart.Fase> salida = new List<ReduccionChart.Fase>();
            if (s == null)
            {
                return salida;
            }
            for (int i = 0; i < s.lineas.Count; i++)
            {
                long tick;
                string[] p;
                if (!Partir(s.lineas[i], out tick, out p) || p.Length < 3 || p[0] != "S")
                {
                    continue;
                }
                long largo;
                if (p[1] != "2" || !long.TryParse(p[2], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out largo))
                {
                    continue;
                }
                ReduccionChart.Fase f = new ReduccionChart.Fase();
                f.tick = tick;
                f.largo = largo;
                salida.Add(f);
            }
            salida.Sort(delegate (ReduccionChart.Fase a, ReduccionChart.Fase b)
            {
                return a.tick.CompareTo(b.tick);
            });
            return salida;
        }

        // Los eventos locales de la seccion ("E solo", "E soloend"). Se copian
        // tal cual: son marcas de sitio, y las notas de la generada son un
        // subconjunto de las de origen, asi que siguen delimitando lo mismo.
        public static List<string[]> EventosLocales(Seccion s)
        {
            List<string[]> salida = new List<string[]>();
            if (s == null)
            {
                return salida;
            }
            for (int i = 0; i < s.lineas.Count; i++)
            {
                long tick;
                string[] p;
                if (!Partir(s.lineas[i], out tick, out p) || p.Length < 2 || p[0] != "E")
                {
                    continue;
                }
                string resto = string.Join(" ", p, 1, p.Length - 1);
                salida.Add(new[] { tick.ToString(CultureInfo.InvariantCulture), resto });
            }
            return salida;
        }

        private static bool Partir(string linea, out long tick, out string[] partes)
        {
            tick = 0;
            partes = null;
            int eq = linea.IndexOf('=');
            if (eq <= 0 || !long.TryParse(linea.Substring(0, eq).Trim(),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out tick))
            {
                return false;
            }
            partes = linea.Substring(eq + 1).Trim()
                .Split(new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries);
            return partes.Length > 0;
        }

        // "  768 = N 0 0"
        private static bool LeerNota(string linea, out long tick, out int traste,
                                     out long largo)
        {
            tick = 0;
            traste = -1;
            largo = 0;
            int eq = linea.IndexOf('=');
            if (eq <= 0)
            {
                return false;
            }
            if (!long.TryParse(linea.Substring(0, eq).Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out tick))
            {
                return false;
            }
            string[] p = linea.Substring(eq + 1).Trim()
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3 || p[0] != "N")
            {
                return false;
            }
            return int.TryParse(p[1], NumberStyles.Integer,
                       CultureInfo.InvariantCulture, out traste)
                && long.TryParse(p[2], NumberStyles.Integer,
                       CultureInfo.InvariantCulture, out largo);
        }

        // Anade (o reemplaza) una seccion de notas. Se coloca justo detras de
        // la ultima del mismo instrumento para que el archivo siga legible.
        public void PonerNotas(string nombre, List<ReduccionChart.Nota> notas,
                               List<ReduccionChart.Fase> fases,
                               List<string[]> eventos, string instrumento)
        {
            Seccion s = Buscar(nombre);
            if (s == null)
            {
                s = new Seccion();
                s.nombre = nombre;
                int donde = secciones.Count;
                for (int i = 0; i < secciones.Count; i++)
                {
                    if (secciones[i].nombre.EndsWith(instrumento, StringComparison.Ordinal))
                    {
                        donde = i + 1;
                    }
                }
                secciones.Insert(donde, s);
            }
            // Todo junto y ordenado por tiempo, que es como el juego escribe
            // sus secciones. Con el tiempo empatado van primero los eventos y
            // las frases y luego las notas, igual que en los charts oficiales.
            List<long[]> orden = new List<long[]>();
            List<string> texto = new List<string>();
            for (int i = 0; i < eventos.Count; i++)
            {
                long t = long.Parse(eventos[i][0], CultureInfo.InvariantCulture);
                orden.Add(new[] { t, 0L, (long)texto.Count });
                texto.Add(eventos[i][0] + " = E " + eventos[i][1]);
            }
            for (int i = 0; i < fases.Count; i++)
            {
                orden.Add(new[] { fases[i].tick, 1L, (long)texto.Count });
                texto.Add(fases[i].tick.ToString(CultureInfo.InvariantCulture)
                    + " = S 2 " + fases[i].largo.ToString(CultureInfo.InvariantCulture));
            }
            for (int i = 0; i < notas.Count; i++)
            {
                for (int t = 0; t < 5; t++)
                {
                    if ((notas[i].trastes & (1 << t)) == 0)
                    {
                        continue;
                    }
                    orden.Add(new[] { notas[i].tick, 2L, (long)texto.Count });
                    texto.Add(notas[i].tick.ToString(CultureInfo.InvariantCulture)
                        + " = N " + t.ToString(CultureInfo.InvariantCulture) + " "
                        + notas[i].sostenido.ToString(CultureInfo.InvariantCulture));
                }
            }
            orden.Sort(delegate (long[] a, long[] b)
            {
                if (a[0] != b[0])
                {
                    return a[0].CompareTo(b[0]);
                }
                if (a[1] != b[1])
                {
                    return a[1].CompareTo(b[1]);
                }
                return a[2].CompareTo(b[2]);
            });

            s.lineas.Clear();
            for (int i = 0; i < orden.Count; i++)
            {
                s.lineas.Add(texto[(int)orden[i][2]]);
            }
        }

        public void Escribir(string ruta)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < secciones.Count; i++)
            {
                sb.Append('[').Append(secciones[i].nombre).Append(']').Append(finDeLinea);
                sb.Append('{').Append(finDeLinea);
                List<string> l = secciones[i].lineas;
                for (int j = 0; j < l.Count; j++)
                {
                    sb.Append("  ").Append(l[j]).Append(finDeLinea);
                }
                sb.Append('}').Append(finDeLinea);
            }
            File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
