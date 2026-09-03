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
                               string instrumento)
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
            s.lineas.Clear();
            for (int i = 0; i < notas.Count; i++)
            {
                for (int t = 0; t < 5; t++)
                {
                    if ((notas[i].trastes & (1 << t)) != 0)
                    {
                        s.lineas.Add(notas[i].tick.ToString(CultureInfo.InvariantCulture)
                            + " = N " + t.ToString(CultureInfo.InvariantCulture) + " "
                            + notas[i].sostenido.ToString(CultureInfo.InvariantCulture));
                    }
                }
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
