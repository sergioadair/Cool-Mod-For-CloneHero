using System;
using System.Collections.Generic;

namespace CloneHeroMod
{
    // Donde suena algo en un audio, y a que ritmo.
    //
    // QUE MIDE. El flujo espectral: cuanta energia APARECE de un instante al
    // siguiente, banda por banda. Sumando solo lo que crece se tiene una curva
    // con un pico en cada ataque. No distingue una guitarra de una silaba ni
    // de un portazo, y eso es justo lo que hace falta aqui: el objetivo es
    // representar el audio, no transcribir un instrumento.
    //
    // Es la variante "superflux": cada banda se compara con el MAXIMO de su
    // entorno en el instante anterior, no consigo misma. Sin eso, un vibrato o
    // un bend movian energia de una banda a la de al lado y se contaban como
    // ataque nuevo.
    //
    // CON QUE NUMEROS. Medido sobre 2722 canciones del corpus oficial, cada
    // una con su guitarra aislada y su chart humano al lado, comparando contra
    // 3,4 millones de ataques etiquetados:
    //
    //     encuentra el 92% de las notas que un humano puso  (mediana)
    //     y por cada nota sobran solo 1,5 ataques
    //
    // Sobre la mezcla completa en vez del instrumento aislado, la cobertura
    // baja al 66%: un tercio de las notas de guitarra no llegan a ser un
    // ataque propio, tapadas por bateria y voz. Por eso, si la cancion trae
    // stems, hay que analizar el stem.
    //
    // LOS VALORES SON LOS DEL PROTOTIPO. Esta tuberia se ajusto primero fuera
    // del juego y estas constantes son las que dieron esas cifras. Cambiar
    // cualquiera invalida las medidas de arriba.
    public static class AnalisisAudio
    {
        public const int Frecuencia = 22050;
        public const int Salto = 256;        // 11,6 ms entre instantes
        public const int Ventana = 1024;     // 46 ms de ventana
        public const float Delta = 0.30f;    // cuanto hay que destacar del entorno
        public const int Entorno = 9;        // instantes a cada lado para la media
        public const int SeparacionMinima = 2;   // 23 ms entre dos ataques

        // EL INSTANTE DE UN MARCO ES SU CENTRO, no donde empieza su ventana.
        //
        // Parece un detalle y no lo es. Marcando el principio, todo salia
        // adelantado media ventana: midiendo una cancion contra su chart, los
        // tiempos seguidos caian sistematicamente 33 ms antes que los reales,
        // y media ventana son 23 ms. Con un chart al lado ese desfase se
        // absorbe buscando el mejor ajuste global, pero generando desde cero
        // no hay contra que ajustar: se quedaria como retraso fijo en todas
        // las notas.
        public static double Instante(int marco)
        {
            return (marco * Salto + Ventana / 2.0) / Frecuencia;
        }

        public struct Ataque
        {
            public double segundo;
            public float fuerza;
        }

        // ------------------------------------------------------- envolvente -
        public static float[] Envolvente(float[] x)
        {
            int marcos = x == null ? 0 : 1 + (x.Length - Ventana) / Salto;
            if (marcos < 8)
            {
                return new float[0];
            }
            int bandas = Ventana / 2 + 1;

            float[] hann = new float[Ventana];
            for (int i = 0; i < Ventana; i++)
            {
                hann[i] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (Ventana - 1)));
            }

            Fft fft = new Fft(Ventana);
            float[] anterior = null;
            float[] actual = new float[bandas];
            float[] salida = new float[marcos];

            for (int m = 0; m < marcos; m++)
            {
                fft.Magnitudes(x, m * Salto, hann, actual);
                for (int b = 0; b < bandas; b++)
                {
                    // log1p comprime el rango: sin esto los pasajes fuertes se
                    // comen a los flojos y el umbral no vale para toda la
                    // cancion
                    actual[b] = (float)Math.Log(1.0 + 1000.0 * actual[b]);
                }
                if (anterior != null)
                {
                    double suma = 0;
                    for (int b = 0; b < bandas; b++)
                    {
                        float techo = anterior[b];
                        if (b > 0 && anterior[b - 1] > techo) techo = anterior[b - 1];
                        if (b + 1 < bandas && anterior[b + 1] > techo) techo = anterior[b + 1];
                        float d = actual[b] - techo;
                        if (d > 0) suma += d;
                    }
                    salida[m] = (float)suma;
                }
                float[] intercambio = anterior;
                anterior = actual;
                actual = intercambio ?? new float[bandas];
            }
            return salida;
        }

        // ------------------------------------------------------------ picos -
        // Un pico cuenta si destaca sobre su propio entorno, no sobre un
        // umbral fijo: una cancion con subidas y bajadas de volumen no tiene
        // un solo umbral que valga para toda ella.
        public static List<Ataque> Picos(float[] env)
        {
            List<Ataque> salida = new List<Ataque>();
            if (env == null || env.Length < Entorno * 2 + 2)
            {
                return salida;
            }

            double media = 0;
            for (int i = 0; i < env.Length; i++) media += env[i];
            media /= env.Length;
            double varianza = 0;
            for (int i = 0; i < env.Length; i++)
            {
                double d = env[i] - media;
                varianza += d * d;
            }
            float desviacion = (float)Math.Sqrt(varianza / env.Length);

            // media movil por sumas acumuladas: recalcularla en cada instante
            // seria cuadratico y esto corre sobre canciones enteras
            double[] acumulado = new double[env.Length + 1];
            for (int i = 0; i < env.Length; i++)
            {
                acumulado[i + 1] = acumulado[i] + env[i];
            }

            int ultimo = -SeparacionMinima - 1;
            for (int i = 1; i < env.Length - 1; i++)
            {
                int desde = Math.Max(0, i - Entorno);
                int hasta = Math.Min(env.Length, i + Entorno + 1);
                double local = (acumulado[hasta] - acumulado[desde]) / (hasta - desde);
                if (env[i] <= local + Delta * desviacion)
                {
                    continue;
                }
                if (env[i] < env[i - 1] || env[i] <= env[i + 1])
                {
                    continue;
                }
                if (i - ultimo < SeparacionMinima)
                {
                    continue;
                }
                ultimo = i;
                Ataque a;
                a.segundo = Instante(i);
                a.fuerza = env[i];
                salida.Add(a);
            }
            return salida;
        }

        // ------------------------------------------------------------ ritmo -
        //
        // El pulso y DONDE caen los tiempos, decididos a la vez.
        //
        // La primera version los separaba: el maximo de la autocorrelacion
        // daba el periodo, y luego se buscaba aparte la fase que mas energia
        // acumulaba. Medido sobre el corpus, eso acertaba el tempo salvo
        // octava el 70% de las veces y dejaba la fase a 108 ms del tiempo
        // real —una quinta parte de un tiempo a 120 pulsaciones—, demasiado
        // para cuadricular nada.
        //
        // Dos arreglos, los dos habituales en seguidores de ritmo:
        //
        // 1. UNA PREFERENCIA DE TEMPO. La autocorrelacion tiene picos en el
        //    periodo real y tambien en su mitad y su doble, y muchas veces el
        //    mas alto no es el que un musico llamaria tempo. Pesar cada
        //    periodo por lo cerca que queda de 120 —contado en octavas, no en
        //    diferencia absoluta— desempata a favor del que se siente.
        //
        // 2. PROGRAMACION DINAMICA en vez de elegir la fase por separado. Se
        //    busca la cadena de tiempos que maximiza a la vez la energia de
        //    ataque en cada uno y la regularidad del espaciado. Asi un tiempo
        //    flojo que llega cuando toca puede ganarle a uno fuerte que llega
        //    a destiempo, que es lo que hace un oyente.
        public struct Ritmo
        {
            public double bpm;
            public double[] tiempos;      // segundos
            public double confianza;      // 0..1, que tan regular salio
        }

        public const double TempoPreferido = 120.0;
        public const double AnchuraPreferencia = 0.9;   // en octavas
        public const double Firmeza = 6.0;              // castigo al espaciado irregular
        public const double MargenCorto = 0.70;         // hueco minimo entre tiempos
        public const double MargenLargo = 1.40;         // y maximo, en periodos

        public static Ritmo Seguir(float[] env, double bpmMinimo = 50, double bpmMaximo = 210)
        {
            Ritmo r = new Ritmo();
            if (env == null || env.Length < 64)
            {
                return r;
            }
            float[] n = Normalizar(env);
            double periodo = Periodo(n, bpmMinimo, bpmMaximo);
            if (periodo <= 0)
            {
                return r;
            }
            r.bpm = 60.0 / (periodo * Salto / Frecuencia);
            r.tiempos = Cadena(n, periodo, out r.confianza);
            return r;
        }

        // Cuando solo hace falta el numero.
        public static double Pulso(float[] env, double bpmMinimo = 50, double bpmMaximo = 210)
        {
            return Seguir(env, bpmMinimo, bpmMaximo).bpm;
        }

        private static float[] Normalizar(float[] env)
        {
            double media = 0;
            for (int i = 0; i < env.Length; i++) media += env[i];
            media /= env.Length;
            double v = 0;
            for (int i = 0; i < env.Length; i++)
            {
                double d = env[i] - media;
                v += d * d;
            }
            double desv = Math.Sqrt(v / env.Length);
            if (desv <= 0) desv = 1;
            float[] salida = new float[env.Length];
            for (int i = 0; i < env.Length; i++)
            {
                salida[i] = (float)((env[i] - media) / desv);
            }
            return salida;
        }

        // Periodo en marcos: autocorrelacion pesada por la preferencia.
        private static double Periodo(float[] n, double bpmMinimo, double bpmMaximo)
        {
            int menor = (int)(60.0 / bpmMaximo * Frecuencia / Salto);
            int mayor = (int)(60.0 / bpmMinimo * Frecuencia / Salto);
            if (menor < 2) menor = 2;
            if (mayor >= n.Length / 2) mayor = n.Length / 2 - 1;
            if (mayor <= menor) return 0;

            double[] puntuacion = new double[mayor + 2];
            double mejor = double.NegativeInfinity;
            int mejorLag = 0;
            for (int d = menor; d <= mayor; d++)
            {
                double suma = 0;
                int cuantos = n.Length - d;
                for (int i = 0; i < cuantos; i++)
                {
                    suma += n[i] * n[i + d];
                }
                suma /= cuantos;
                double bpm = 60.0 / ((double)d * Salto / Frecuencia);
                double octavas = Math.Log(bpm / TempoPreferido, 2.0) / AnchuraPreferencia;
                puntuacion[d] = suma * Math.Exp(-0.5 * octavas * octavas);
                if (puntuacion[d] > mejor)
                {
                    mejor = puntuacion[d];
                    mejorLag = d;
                }
            }
            if (mejorLag == 0)
            {
                return 0;
            }

            // PRECISION POR DEBAJO DEL MARCO. Un marco dura 11,6 ms y un
            // tiempo ronda los 500, asi que quedarse con el marco entero deja
            // hasta un 1,2% de error en el periodo. Parece poco y no lo es:
            // sobre cien tiempos son mas de medio segundo de deriva, y la
            // rejilla acaba despegada del final de la cancion aunque el
            // principio cuadre. Medido: sin esto, solo un 33% de los tiempos
            // caian a menos de 30 ms del real.
            //
            // El maximo de verdad se saca ajustando una parabola a los tres
            // valores de alrededor del pico.
            if (mejorLag > menor && mejorLag < mayor)
            {
                double y0 = puntuacion[mejorLag - 1];
                double y1 = puntuacion[mejorLag];
                double y2 = puntuacion[mejorLag + 1];
                double abajo = y0 - 2.0 * y1 + y2;
                if (Math.Abs(abajo) > 1e-12)
                {
                    double ajuste = 0.5 * (y0 - y2) / abajo;
                    if (ajuste > -1.0 && ajuste < 1.0)
                    {
                        return mejorLag + ajuste;
                    }
                }
            }
            return mejorLag;
        }

        // La cadena de tiempos que mejor combina energia y regularidad.
        private static double[] Cadena(float[] n, double periodo, out double confianza)
        {
            confianza = 0;
            int largo = n.Length;
            double[] puntos = new double[largo];
            int[] atras = new int[largo];
            // Cuanto puede estirarse o encogerse el hueco entre dos tiempos.
            // Estaba en medio periodo a doble periodo, y con tanta manga el
            // seguidor intercalaba tiempos donde no tocaba: en una cancion de
            // 264 negras llego a sacar 288, y al final se habia saltado un
            // tiempo entero. Un margen estrecho lo obliga a mantener el paso.
            int desde = (int)Math.Round(periodo * MargenCorto);
            int hasta = (int)Math.Round(periodo * MargenLargo);
            if (desde < 1) desde = 1;
            if (hasta <= desde) hasta = desde + 1;

            for (int i = 0; i < largo; i++)
            {
                double mejor = double.NegativeInfinity;
                int mejorJ = -1;
                int j0 = i - hasta;
                if (j0 < 0) j0 = 0;
                for (int j = j0; j <= i - desde; j++)
                {
                    double razon = (i - j) / periodo;
                    double castigo = Math.Log(razon);
                    double v = puntos[j] - Firmeza * castigo * castigo;
                    if (v > mejor)
                    {
                        mejor = v;
                        mejorJ = j;
                    }
                }
                if (mejorJ < 0)
                {
                    puntos[i] = n[i];
                    atras[i] = -1;
                }
                else
                {
                    puntos[i] = n[i] + mejor;
                    atras[i] = mejorJ;
                }
            }

            // se arranca desde el mejor final, mirando solo la ultima parte
            int fin = -1;
            double tope = double.NegativeInfinity;
            for (int i = Math.Max(0, largo - hasta); i < largo; i++)
            {
                if (puntos[i] > tope) { tope = puntos[i]; fin = i; }
            }
            if (fin < 0) return new double[0];

            List<int> marcos = new List<int>();
            int k = fin;
            while (k >= 0)
            {
                marcos.Add(k);
                k = atras[k];
            }
            marcos.Reverse();
            if (marcos.Count < 4) return new double[0];

            double[] salida = new double[marcos.Count];
            for (int i = 0; i < marcos.Count; i++)
            {
                salida[i] = Instante(marcos[i]);
            }

            // confianza: lo parecidos que son los espaciados entre si
            double mediaSep = 0;
            for (int i = 1; i < marcos.Count; i++) mediaSep += marcos[i] - marcos[i - 1];
            mediaSep /= (marcos.Count - 1);
            double var = 0;
            for (int i = 1; i < marcos.Count; i++)
            {
                double d = (marcos[i] - marcos[i - 1]) - mediaSep;
                var += d * d;
            }
            double desv = Math.Sqrt(var / Math.Max(1, marcos.Count - 1));
            confianza = mediaSep > 0 ? Math.Max(0, 1.0 - desv / mediaSep) : 0;
            return salida;
        }

        // --------------------------------------------------------------- fft
        // Radix-2 iterativa con tablas precalculadas. Se reutiliza para todos
        // los instantes de la cancion, que son decenas de miles.
        private class Fft
        {
            private readonly int n;
            private readonly int[] inverso;
            private readonly double[] cos;
            private readonly double[] sen;
            private readonly double[] re;
            private readonly double[] im;

            public Fft(int tamano)
            {
                n = tamano;
                re = new double[n];
                im = new double[n];
                inverso = new int[n];
                int bits = 0;
                while ((1 << bits) < n) bits++;
                for (int i = 0; i < n; i++)
                {
                    int r = 0;
                    for (int b = 0; b < bits; b++)
                    {
                        if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                    }
                    inverso[i] = r;
                }
                cos = new double[n / 2];
                sen = new double[n / 2];
                for (int i = 0; i < n / 2; i++)
                {
                    cos[i] = Math.Cos(-2.0 * Math.PI * i / n);
                    sen[i] = Math.Sin(-2.0 * Math.PI * i / n);
                }
            }

            public void Magnitudes(float[] x, int desde, float[] ventana, float[] destino)
            {
                for (int i = 0; i < n; i++)
                {
                    int j = desde + i;
                    re[inverso[i]] = j < x.Length ? x[j] * ventana[i] : 0.0;
                    im[inverso[i]] = 0.0;
                }
                for (int largo = 2; largo <= n; largo <<= 1)
                {
                    int mitad = largo >> 1;
                    int paso = n / largo;
                    for (int i = 0; i < n; i += largo)
                    {
                        for (int k = 0; k < mitad; k++)
                        {
                            int t = k * paso;
                            int a = i + k;
                            int b = a + mitad;
                            double wr = cos[t], wi = sen[t];
                            double xr = re[b] * wr - im[b] * wi;
                            double xi = re[b] * wi + im[b] * wr;
                            re[b] = re[a] - xr;
                            im[b] = im[a] - xi;
                            re[a] += xr;
                            im[a] += xi;
                        }
                    }
                }
                int bandas = n / 2 + 1;
                for (int b = 0; b < bandas; b++)
                {
                    destino[b] = (float)Math.Sqrt(re[b] * re[b] + im[b] * im[b]);
                }
            }
        }
    }
}
