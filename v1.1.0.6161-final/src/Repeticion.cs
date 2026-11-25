using System;
using System.Collections.Generic;

namespace CloneHeroMod
{
    // Que lo que suena igual se toque igual.
    //
    // Un charter humano pone el mismo riff igual cada vez que vuelve, y el
    // jugador lo aprende. El generador decidia cada nota por separado, asi que
    // dos estribillos identicos salian con notas distintas y nunca habia un
    // patron que memorizar: frases de 8 notas repetidas, 0% contra un 67% en
    // los charts humanos de la misma dificultad.
    //
    // Lo que se hace: describir como suena cada pulso, y para cada compas
    // buscar el tramo anterior de cuatro pulsos que mas se le parece; si se
    // parece lo bastante, el compas se queda con las notas de aquel tramo tal
    // cual. Se copia de lo ya construido, asi que una copia de una copia sale
    // igual que el original y todas las vueltas de un estribillo coinciden.
    //
    // El tramo puede empezar en cualquier pulso, no solo donde empieza un
    // compas: el generador no sabe donde cae el primer tiempo de verdad, ni si
    // el tempo que eligio es el doble del real, y una repeticion corrida un
    // pulso no se encontraria nunca. Medido en charts humanos, buscar en
    // cualquier pulso no empeora el acierto (54% contra 56%).
    //
    // CALIBRADO CONTRA CHARTS HUMANOS. 159 canciones de la biblioteca, con el
    // mapa de tempo humano y su chart de Experto al lado, 17.349 compases:
    //
    //     compases con uno identico antes, en el chart humano     50%
    //     el tramo mas parecido por el audio, charteado igual:
    //         a similitud >= 0,70 (cubre el 42%)                  57%
    //         ... o casi igual (80% de notas en comun)            63%
    //     un compas anterior cualquiera, charteado igual           6%
    //
    // No es perfecto ni hace falta que lo sea: lo que se copia sale de un
    // tramo que suena como este, asi que encaja con el audio igual que el
    // original. Lo que se gana es que el jugador reconoce el patron.
    public static class Repeticion
    {
        public const double Umbral = 0.70;
        public const int PulsosPorCompas = 4;
        private const int Dimension = AnalisisAudio.DimensionRasgos + 4;

        // Devuelve las notas nuevas y, alineadas con ellas, la fuerza de cada
        // una (para el Star Power). copiados: cuantos compases se copiaron.
        public static List<ReduccionChart.Nota> Aplicar(float[] audio, float[] env,
            double[] lineas, List<ReduccionChart.Nota> notas, List<float> fuerzas,
            out List<float> fuerzasNuevas, out int copiados, out int compases)
        {
            fuerzasNuevas = fuerzas;
            copiados = 0;
            int res = ChartDesdeAudio.Resolucion;
            int largoCompas = PulsosPorCompas * res;

            double[][] pulsos = Describir(audio, env, lineas);
            compases = pulsos.Length / PulsosPorCompas;
            if (compases < 4)
            {
                compases = 0;
                return notas;
            }
            double[][] tramos = new double[pulsos.Length][];
            for (int p = 0; p + PulsosPorCompas <= pulsos.Length; p++)
            {
                tramos[p] = Tramo(pulsos, p);
            }

            List<ReduccionChart.Nota> salida = new List<ReduccionChart.Nota>();
            List<float> fuerzaSalida = new List<float>();
            int siguiente = 0;      // primera nota original aun no volcada
            for (int c = 0; c < compases; c++)
            {
                long desde = (long)c * largoCompas;
                long hasta = desde + largoCompas;
                int propias = siguiente;
                while (propias < notas.Count && notas[propias].tick < hasta) propias++;
                bool tieneNotas = propias > siguiente;

                int origen = -1;
                int inicio = c * PulsosPorCompas;
                if (tieneNotas && tramos[inicio] != null)
                {
                    double parecido = Umbral;
                    for (int q = 0; q + PulsosPorCompas <= inicio; q++)
                    {
                        if (tramos[q] == null) continue;
                        double s = Similitud(tramos, inicio, q);
                        if (s >= parecido)
                        {
                            parecido = s;
                            origen = q;
                        }
                    }
                }

                bool copiado = false;
                if (origen >= 0)
                {
                    // Lo que ya se construyo en ese tramo. Si alli no hay
                    // notas, no se copia: borrar las de este compas seria
                    // quitar lo que si suena.
                    long a = (long)origen * res, b = a + largoCompas;
                    long desplaza = desde - a;
                    int n0 = salida.Count;
                    for (int i = 0; i < n0; i++)
                    {
                        if (salida[i].tick < a || salida[i].tick >= b) continue;
                        ReduccionChart.Nota n = salida[i];
                        n.tick += desplaza;
                        salida.Add(n);
                        fuerzaSalida.Add(fuerzaSalida[i]);
                    }
                    copiado = salida.Count > n0;
                }
                if (copiado)
                {
                    copiados++;
                }
                else
                {
                    for (int i = siguiente; i < propias; i++)
                    {
                        salida.Add(notas[i]);
                        fuerzaSalida.Add(fuerzas[i]);
                    }
                }
                siguiente = propias;
            }
            // las que quedaran mas alla del ultimo compas descrito
            for (int i = siguiente; i < notas.Count; i++)
            {
                salida.Add(notas[i]);
                fuerzaSalida.Add(fuerzas[i]);
            }
            if (copiados == 0)
            {
                return notas;
            }
            Coser(salida);
            fuerzasNuevas = fuerzaSalida;
            return salida;
        }

        // Las juntas entre compases copiados de sitios distintos: que un
        // sostenido no pise la nota siguiente, y que dos rapidas seguidas no
        // queden en el mismo traste (seria un rasgueo doble imposible).
        private static void Coser(List<ReduccionChart.Nota> notas)
        {
            int res = ChartDesdeAudio.Resolucion;
            for (int i = 0; i + 1 < notas.Count; i++)
            {
                ReduccionChart.Nota a = notas[i];
                ReduccionChart.Nota b = notas[i + 1];
                long hueco = b.tick - a.tick;
                if (a.sostenido > 0 && a.tick + a.sostenido > b.tick - res / 4)
                {
                    long largo = b.tick - res / 4 - a.tick;
                    a.sostenido = largo >= res / 4 ? largo : 0;
                    notas[i] = a;
                }
                if (hueco <= ChartDesdeAudio.UmbralHopo && b.trastes == a.trastes)
                {
                    int f = Primero(b.trastes);
                    int g = f < 4 ? f + 1 : f - 1;
                    b.trastes = 1 << g;
                    notas[i + 1] = b;
                }
            }
        }

        private static int Primero(int trastes)
        {
            for (int f = 0; f < 5; f++)
            {
                if ((trastes & (1 << f)) != 0) return f;
            }
            return 0;
        }

        // Parecido de dos tramos, con su contexto: la media entre ellos dos y
        // el mejor de sus vecinos emparejados (el tramo de antes con el de
        // antes, o el de despues con el de despues). Un tramo suelto se parece
        // a muchos; con el de al lado tambien parecido, es la misma parte de
        // la cancion.
        private static double Similitud(double[][] t, int p, int q)
        {
            double s = Coseno(t[p], t[q]);
            double ctx = double.NegativeInfinity;
            int n = PulsosPorCompas;
            if (p + n < t.Length && t[p + n] != null && t[q + n] != null)
                ctx = Math.Max(ctx, Coseno(t[p + n], t[q + n]));
            if (q >= n && t[p - n] != null && t[q - n] != null)
                ctx = Math.Max(ctx, Coseno(t[p - n], t[q - n]));
            return double.IsNegativeInfinity(ctx) ? s : (s + ctx) / 2;
        }

        private static double Coseno(double[] a, double[] b)
        {
            double s = 0;
            for (int k = 0; k < a.Length; k++) s += a[k] * b[k];
            return s;      // ya vienen normalizados
        }

        // Cuatro pulsos seguidos en un vector de largo 1, o null si alguno no
        // tiene audio debajo.
        private static double[] Tramo(double[][] pulsos, int p)
        {
            double[] v = new double[Dimension * PulsosPorCompas];
            for (int k = 0; k < PulsosPorCompas; k++)
            {
                double[] x = pulsos[p + k];
                if (x == null) return null;
                Array.Copy(x, 0, v, k * Dimension, Dimension);
            }
            double largo = 0;
            for (int d = 0; d < v.Length; d++) largo += v[d] * v[d];
            largo = Math.Sqrt(largo);
            if (largo <= 0) return null;
            for (int d = 0; d < v.Length; d++) v[d] /= largo;
            return v;
        }

        // Un vector por pulso: sus 20 rasgos medios y la fuerza de ataque en
        // cada semicorchea. Cada dimension se tipifica contra la cancion
        // entera; si no, el volumen general manda sobre todo lo demas.
        private static double[][] Describir(float[] audio, float[] env, double[] lineas)
        {
            float[][] rasgos = AnalisisAudio.Rasgos(audio);
            double duracion = (double)audio.Length / AnalisisAudio.Frecuencia;
            int pulsos = lineas.Length - 1;
            double[][] porPulso = new double[pulsos][];
            for (int k = 0; k < pulsos; k++)
            {
                double a = lineas[k] - ChartDesdeAudio.Entradilla;
                double b = lineas[k + 1] - ChartDesdeAudio.Entradilla;
                if (a < 0 || b > duracion) continue;
                porPulso[k] = Pulso(rasgos, env, a, b);
            }

            double[] media = new double[Dimension];
            double[] desv = new double[Dimension];
            int n = 0;
            for (int k = 0; k < pulsos; k++)
            {
                if (porPulso[k] == null) continue;
                for (int d = 0; d < Dimension; d++) media[d] += porPulso[k][d];
                n++;
            }
            if (n < 8)
            {
                return new double[0][];
            }
            for (int d = 0; d < Dimension; d++) media[d] /= n;
            for (int k = 0; k < pulsos; k++)
            {
                if (porPulso[k] == null) continue;
                for (int d = 0; d < Dimension; d++)
                {
                    double x = porPulso[k][d] - media[d];
                    desv[d] += x * x;
                }
            }
            for (int d = 0; d < Dimension; d++) desv[d] = Math.Sqrt(desv[d] / n) + 1e-9;
            for (int k = 0; k < pulsos; k++)
            {
                if (porPulso[k] == null) continue;
                for (int d = 0; d < Dimension; d++)
                {
                    porPulso[k][d] = (porPulso[k][d] - media[d]) / desv[d];
                }
            }
            return porPulso;
        }

        private static double[] Pulso(float[][] rasgos, float[] env, double a, double b)
        {
            double[] v = new double[Dimension];
            int n = 0;
            int m0 = Math.Max(0, (int)((a * AnalisisAudio.Frecuencia
                - AnalisisAudio.VentanaRasgos / 2.0) / AnalisisAudio.SaltoRasgos));
            for (int m = m0; m < rasgos.Length; m++)
            {
                double c = AnalisisAudio.InstanteRasgo(m);
                if (c < a) continue;
                if (c >= b) break;
                for (int d = 0; d < AnalisisAudio.DimensionRasgos; d++) v[d] += rasgos[m][d];
                n++;
            }
            if (n > 0)
            {
                for (int d = 0; d < AnalisisAudio.DimensionRasgos; d++) v[d] /= n;
            }
            double q = (b - a) / 4;
            for (int s = 0; s < 4; s++)
            {
                double x0 = a + s * q, x1 = x0 + q;
                float mayor = 0;
                int f0 = Math.Max(0, (int)((x0 * AnalisisAudio.Frecuencia
                    - AnalisisAudio.Ventana / 2.0) / AnalisisAudio.Salto));
                for (int f = f0; f < env.Length; f++)
                {
                    double c = AnalisisAudio.Instante(f);
                    if (c < x0) continue;
                    if (c >= x1) break;
                    if (env[f] > mayor) mayor = env[f];
                }
                v[AnalisisAudio.DimensionRasgos + s] = Math.Log(1.0 + mayor);
            }
            return v;
        }
    }
}
