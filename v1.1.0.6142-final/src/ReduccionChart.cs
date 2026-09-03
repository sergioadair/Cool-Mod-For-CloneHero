using System;
using System.Collections.Generic;

namespace CloneHeroMod
{
    // Genera una dificultad a partir de la inmediatamente superior.
    //
    // NADA DE ESTO ES INVENTADO: sale de medir 3175 charts oficiales (Guitar
    // Hero 1 a Warriors of Rock, World Tour, Live, Rock Band, The Beatles) en
    // C:\...\Songs\A. Lo que dijeron los numeros:
    //
    // 1. REDUCIR ES QUEDARSE CON UN SUBCONJUNTO, no re-chartear. El 99,4% de
    //    los tiempos de Hard existen tal cual en Expert; 99,2% de Medium en
    //    Hard; 98,9% de Easy en Medium. Y cuando el tiempo coincide, el acorde
    //    de la baja es subconjunto del de la alta el 89,1% de las veces.
    //
    // 2. CUANTO SE RECORTA. Densidad respecto a la inmediata superior:
    //    Hard 0,78 · Medium 0,68 · Easy 0,68. En notas medias eso es
    //    805 -> 626 -> 425 -> 287.
    //
    // 3. QUE SE CONSERVA. Hay una jerarquia metrica clarisima. De las notas de
    //    Expert llegan a Easy:
    //
    //        tiempo 1 del compas  83,6%      corcheas      16-24%
    //        tiempo 3             77,3%      semicorcheas   6-15%
    //        tiempos 2 y 4        ~40%
    //
    //    De ahi los pesos de PesoMetrico: el downbeat es lo ultimo que se cae.
    //
    // 4. SEPARACION MINIMA entre notas seguidas. Expert admite semicorcheas
    //    (31% de sus huecos son de 1/16); Hard es casi todo corcheas; Medium
    //    negras; Easy negras y blancas. De ahi SeparacionMinima.
    //
    // 5. TRASTES. La moda de cada fila de la matriz de remapeo da el mapa
    //    directo. Hard conserva el traste (86-93%); Medium comprime a cuatro
    //    trastes; Easy a tres.
    //
    // 6. ACORDES. Expert llega a tres notas; Hard y Medium se quedan en dos
    //    (los de tres son el 0,4% y el 0,0%); Easy es 99% nota suelta.
    //
    // LO QUE NO SE GENERA: HOPO forzado, tap y notas abiertas. Se dejan fuera a
    // proposito — el juego aplica sus reglas naturales de HOPO y sale bien;
    // emitir marcas mal puestas se notaria mucho mas que no ponerlas.
    public static class ReduccionChart
    {
        // Indices como en Dificultad: 0 Easy, 1 Medium, 2 Hard, 3 Expert.
        public const int Easy = 0;
        public const int Medium = 1;
        public const int Hard = 2;
        public const int Expert = 3;

        // Densidad respecto a la dificultad de la que se parte.
        private static readonly double[] Densidad = { 0.68, 0.68, 0.78, 1.0 };

        // Separacion minima entre notas, en semicorcheas.
        private static readonly int[] SeparacionMinima = { 4, 2, 1, 1 };

        // Acorde maximo.
        private static readonly int[] AcordeMaximo = { 1, 2, 2, 3 };

        // Remapeo de trastes 0-4. Hard conserva; Medium usa cuatro; Easy tres.
        private static readonly int[][] Trastes =
        {
            new[] { 0, 1, 1, 2, 2 },      // Easy
            new[] { 0, 1, 2, 2, 3 },      // Medium
            new[] { 0, 1, 2, 3, 4 },      // Hard
            new[] { 0, 1, 2, 3, 4 }       // Expert
        };

        public struct Nota
        {
            public long tick;
            public int trastes;      // mascara de bits, 1<<0 .. 1<<4
            public long sostenido;
        }

        // La fuente tiene que venir ordenada por tick. resolucion son los ticks
        // de una negra (el "Resolution" del .chart o la division del MIDI).
        public static List<Nota> Reducir(List<Nota> fuente, int destino, int resolucion)
        {
            List<Nota> salida = new List<Nota>();
            if (fuente == null || fuente.Count == 0 || resolucion <= 0
                || destino < 0 || destino > 2)
            {
                return salida;
            }

            // 1. peso de cada nota
            int n = fuente.Count;
            int[] peso = new int[n];
            for (int i = 0; i < n; i++)
            {
                peso[i] = PesoMetrico(fuente[i].tick, resolucion)
                        + PesoSostenido(fuente[i].sostenido, resolucion);
            }

            // 2. se recorren de mas importante a menos y se van aceptando las
            //    que respeten la separacion minima. Ordenar por peso y no por
            //    tiempo es lo que conserva los golpes fuertes cuando hay
            //    sincopa: si se hiciera en orden, la primera nota de un grupo
            //    de semicorcheas bloquearia a la negra siguiente.
            int[] orden = new int[n];
            for (int i = 0; i < n; i++)
            {
                orden[i] = i;
            }
            int[] clave = peso;
            List<Nota> f = fuente;
            Array.Sort(orden, delegate (int a, int b)
            {
                int d = clave[b] - clave[a];
                return d != 0 ? d : f[a].tick.CompareTo(f[b].tick);
            });

            long hueco = (long)SeparacionMinima[destino] * resolucion / 4;
            if (hueco < 1)
            {
                hueco = 1;
            }
            List<long> puestas = new List<long>();
            List<int> aceptadas = new List<int>();
            for (int k = 0; k < n; k++)
            {
                int i = orden[k];
                if (CabeEn(puestas, fuente[i].tick, hueco))
                {
                    Insertar(puestas, fuente[i].tick);
                    aceptadas.Add(i);
                }
            }

            // 3. si aun asi quedan mas de las que toca, se caen las de menos
            //    peso. La separacion minima sola no basta: en un pasaje lento
            //    todo cabe y Hard saldria identico a Expert.
            int objetivo = (int)Math.Round(n * Densidad[destino]);
            if (objetivo < 1)
            {
                objetivo = 1;
            }
            if (aceptadas.Count > objetivo)
            {
                int[] a = aceptadas.ToArray();
                Array.Sort(a, delegate (int x, int y)
                {
                    int d = clave[y] - clave[x];
                    return d != 0 ? d : f[x].tick.CompareTo(f[y].tick);
                });
                aceptadas = new List<int>();
                for (int i = 0; i < objetivo; i++)
                {
                    aceptadas.Add(a[i]);
                }
            }
            aceptadas.Sort(delegate (int x, int y) { return f[x].tick.CompareTo(f[y].tick); });

            // 4. acordes y trastes
            int tope = AcordeMaximo[destino];
            int[] mapa = Trastes[destino];
            for (int k = 0; k < aceptadas.Count; k++)
            {
                Nota o = fuente[aceptadas[k]];
                int mascara = 0;
                int puestos = 0;
                // de grave a agudo: al recortar un acorde se conserva la parte
                // baja, que es como aparecen en los charts oficiales
                for (int t = 0; t < 5 && puestos < tope; t++)
                {
                    if ((o.trastes & (1 << t)) == 0)
                    {
                        continue;
                    }
                    mascara |= 1 << mapa[t];
                    puestos++;
                }
                if (mascara == 0)
                {
                    continue;      // solo traia abiertas o marcas
                }
                Nota d = new Nota();
                d.tick = o.tick;
                d.trastes = mascara;
                d.sostenido = o.sostenido;
                salida.Add(d);
            }
            return salida;
        }

        // Ver el punto 3 de la cabecera. Los numeros son el orden que salio de
        // medir, no una escala fina: lo unico que importa es cual se cae antes.
        private static int PesoMetrico(long tick, int resolucion)
        {
            long semicorchea = resolucion / 4;
            if (semicorchea <= 0)
            {
                return 10;
            }
            if (tick % semicorchea != 0)
            {
                return 8;          // fuera de rejilla (tresillos y demas)
            }
            long pos = (tick / semicorchea) % 16;
            if (pos == 0)
            {
                return 100;        // tiempo 1
            }
            if (pos == 8)
            {
                return 90;         // tiempo 3
            }
            if (pos % 4 == 0)
            {
                return 70;         // tiempos 2 y 4
            }
            if (pos % 2 == 0)
            {
                return 40;         // corcheas
            }
            return 15;             // semicorcheas
        }

        // Una nota larga es un ancla de la cancion: aguanta mas que una corta
        // en el mismo sitio del compas.
        private static int PesoSostenido(long sostenido, int resolucion)
        {
            if (sostenido >= resolucion)
            {
                return 12;
            }
            return sostenido >= resolucion / 2 ? 6 : 0;
        }

        // --- lista ordenada de tiempos ya aceptados -------------------------
        private static bool CabeEn(List<long> puestas, long tick, long hueco)
        {
            int i = puestas.BinarySearch(tick);
            if (i >= 0)
            {
                return false;      // ya hay una nota en ese tiempo
            }
            i = ~i;
            if (i > 0 && tick - puestas[i - 1] < hueco)
            {
                return false;
            }
            if (i < puestas.Count && puestas[i] - tick < hueco)
            {
                return false;
            }
            return true;
        }

        private static void Insertar(List<long> puestas, long tick)
        {
            int i = puestas.BinarySearch(tick);
            puestas.Insert(i >= 0 ? i : ~i, tick);
        }
    }
}
