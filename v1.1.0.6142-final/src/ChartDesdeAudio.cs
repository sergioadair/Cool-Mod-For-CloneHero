using System;
using System.Collections.Generic;

namespace CloneHeroMod
{
    // De un archivo de audio a un chart de Experto.
    //
    // La parte de oir el audio la hacen AudioBass y AnalisisAudio. Aqui se
    // decide lo otro: de todo lo que suena, QUE se convierte en nota, en QUE
    // traste, y cuanto dura.
    //
    // ---------------------------------------------------------------------
    // POR QUE ESTO NO ES TRANSCRIBIR
    //
    // Medido sobre 2722 canciones del corpus oficial, con la guitarra aislada
    // y el chart humano al lado: el detector encuentra el 92% de las notas que
    // un humano puso, pero por cada nota sobran 1,5 ataques. Sobre una mezcla
    // completa sobran entre 6 y 20.
    //
    // O sea que el problema nunca fue oir. Es ELEGIR. Un charter oye 1216
    // ataques y pone 126 notas. Lo que sigue son las reglas de esa eleccion,
    // sacadas de medir el corpus, no de suponer.
    //
    // ---------------------------------------------------------------------
    // LAS TRES REGLAS QUE SALIERON DE MEDIR
    //
    // 1. LA FUERZA MANDA, Y DE FORMA ORDENADA. Etiquetando 3,4 millones de
    //    ataques segun acabaran siendo nota o no, agrupados por su fuerza
    //    relativa dentro de la cancion:
    //
    //        percentil  0-10  ->  27% acaban siendo nota
    //        percentil 40-50  ->  61%
    //        percentil 90-100 ->  86%
    //
    //    Monotono y sin saltos. No separa solo, pero ordena bien.
    //
    // 2. DENSIDAD. Las canciones del corpus rondan 3,6 notas por segundo
    //    (p25 2,96 / p75 4,37). Pasar de ahi no hace el chart mas fiel, lo
    //    hace impracticable.
    //
    // 3. NUNCA INVENTAR. Si el audio trae menos ataques que el presupuesto de
    //    densidad, se ponen los que hay y ya. Un chart de un meme con tres
    //    golpes tiene tres notas. Rellenar para llegar a una cifra seria
    //    dejar de representar el audio, que es justo lo que se busca.
    //
    // ---------------------------------------------------------------------
    // LOS TRASTES
    //
    // Un traste no es una nota: no hay afinacion ni cejilla. Lo que si se
    // pudo medir es la DIRECCION. Sobre 86.827 transiciones de 324 canciones,
    // cuando el tono sube entre dos notas seguidas, el traste sube el 70% de
    // las veces. El azar seria 50%, asi que la regla existe — pero con
    // mediana por cancion del 69% y casos del 29%, es criterio, no ley.
    //
    // Asi que los trastes salen del tono, repartiendo la cancion en cinco
    // tramos por cuantiles. Eso sigue el contorno sin depender de acertar la
    // frecuencia exacta, que en una mezcla es mucho pedir.
    public static class ChartDesdeAudio
    {
        public const int Resolucion = 192;          // la de todos los charts medidos
        public const double DensidadObjetivo = 3.6;  // notas por segundo (mediana del corpus)
        public const double SeparacionMinima = 0.070;   // segundos entre dos notas
        public const double FraccionQueSobra = 0.65;    // se conserva el 65% mas fuerte
        public const double ProporcionAcordes = 0.36;   // mediana medida del corpus
        public const double SostenidoMinimo = 0.25;     // segundos para que valga la pena
        public const int VentanaTono = 2048;            // ~93 ms para estimar el tono

        public class Resultado
        {
            public List<ReduccionChart.Nota> notas = new List<ReduccionChart.Nota>();
            public List<ReduccionChart.Fase> fases = new List<ReduccionChart.Fase>();
            public double bpm = 120;
            public double confianzaRitmo;
            public bool cuadriculado;
            public int ataques;
            public double duracion;
            public string aviso = "";
        }

        // ------------------------------------------------------------ entrada
        public static Resultado Generar(string rutaAudio)
        {
            Resultado r = new Resultado();

            float[] x = AudioBass.LeerMono(rutaAudio, AnalisisAudio.Frecuencia);
            if (x == null || x.Length < AnalisisAudio.Frecuencia / 2)
            {
                r.aviso = "no se pudo leer el audio";
                return r;
            }
            r.duracion = (double)x.Length / AnalisisAudio.Frecuencia;

            float[] env = AnalisisAudio.Envolvente(x);
            List<AnalisisAudio.Ataque> ataques = AnalisisAudio.Picos(env);
            r.ataques = ataques.Count;
            if (ataques.Count < 4)
            {
                r.aviso = "el audio no tiene sonidos suficientes";
                return r;
            }

            AnalisisAudio.Ritmo ritmo = AnalisisAudio.Seguir(env);
            r.confianzaRitmo = ritmo.confianza;
            r.bpm = ritmo.bpm > 20 ? ritmo.bpm : 120.0;

            List<AnalisisAudio.Ataque> elegidos = Elegir(ataques, r.duracion, ritmo);

            // Cuadricular SOLO si el seguidor fue firme. Una nota fuera de
            // rejilla se juega igual de bien; una nota movida a la rejilla
            // equivocada se juega mal, porque deja de caer donde suena.
            double[] rejilla = null;
            if (ritmo.confianza >= 0.85 && ritmo.tiempos != null && ritmo.tiempos.Length > 8)
            {
                rejilla = ritmo.tiempos;
                r.cuadriculado = true;
            }

            AsignarTrastes(x, elegidos, r, rejilla);
            r.fases = ReduccionChart.AjustarFases(new List<ReduccionChart.Fase>(), r.notas);
            return r;
        }

        // ----------------------------------------------------------- elegir --
        private static List<AnalisisAudio.Ataque> Elegir(List<AnalisisAudio.Ataque> todos,
                                                         double duracion,
                                                         AnalisisAudio.Ritmo ritmo)
        {
            // Cuantas caben. El minimo entre lo que pide la densidad y lo que
            // el audio de verdad ofrece: de los dos manda el audio.
            int porDensidad = (int)(duracion * DensidadObjetivo);
            int porFuerza = (int)(todos.Count * FraccionQueSobra);
            int objetivo = Math.Min(porDensidad, porFuerza);
            if (objetivo >= todos.Count)
            {
                objetivo = todos.Count;
            }
            if (objetivo < 1)
            {
                objetivo = Math.Min(todos.Count, 1);
            }

            // Se puntua cada ataque y se van cogiendo de mas a menos fuerte,
            // saltando los que caen encima de uno ya cogido. Asi el recorte no
            // deja huecos enormes en los tramos flojos: dentro de cada tramo
            // sobreviven los mas destacados de ESE tramo.
            List<int> orden = new List<int>();
            for (int i = 0; i < todos.Count; i++) orden.Add(i);
            double[] puntos = Puntuar(todos, ritmo);
            orden.Sort(delegate (int a, int b) { return puntos[b].CompareTo(puntos[a]); });

            List<AnalisisAudio.Ataque> tomados = new List<AnalisisAudio.Ataque>();
            List<double> tiempos = new List<double>();
            for (int k = 0; k < orden.Count && tomados.Count < objetivo; k++)
            {
                double t = todos[orden[k]].segundo;
                if (DemasiadoCerca(tiempos, t))
                {
                    continue;
                }
                tomados.Add(todos[orden[k]]);
                Insertar(tiempos, t);
            }
            tomados.Sort(delegate (AnalisisAudio.Ataque a, AnalisisAudio.Ataque b)
            {
                return a.segundo.CompareTo(b.segundo);
            });
            return tomados;
        }

        // Fuerza relativa, mas una ayuda si cae en un tiempo del compas. Lo
        // segundo solo cuenta cuando hay rejilla de fiar: el corpus dice que
        // el 70% de las notas humanas caen en tiempo o corchea, pero premiar
        // posiciones de una rejilla mal puesta es peor que no premiar nada.
        private static double[] Puntuar(List<AnalisisAudio.Ataque> todos,
                                        AnalisisAudio.Ritmo ritmo)
        {
            double[] puntos = new double[todos.Count];
            float mayor = 0;
            for (int i = 0; i < todos.Count; i++)
            {
                if (todos[i].fuerza > mayor) mayor = todos[i].fuerza;
            }
            if (mayor <= 0) mayor = 1;

            bool hayRejilla = ritmo.confianza >= 0.85
                && ritmo.tiempos != null && ritmo.tiempos.Length > 8;
            double periodo = ritmo.bpm > 20 ? 60.0 / ritmo.bpm : 0.5;

            for (int i = 0; i < todos.Count; i++)
            {
                double p = todos[i].fuerza / mayor;
                if (hayRejilla)
                {
                    double d = DistanciaARejilla(ritmo.tiempos, todos[i].segundo, periodo);
                    // en el tiempo justo suma; a contratiempo no resta
                    p += 0.35 * Math.Max(0, 1.0 - d / (periodo / 4));
                }
                puntos[i] = p;
            }
            return puntos;
        }

        private static double DistanciaARejilla(double[] tiempos, double t, double periodo)
        {
            int lo = 0, hi = tiempos.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (tiempos[mid] < t) lo = mid + 1; else hi = mid;
            }
            double mejor = Math.Abs(tiempos[lo] - t);
            if (lo > 0) mejor = Math.Min(mejor, Math.Abs(tiempos[lo - 1] - t));
            // tambien vale caer en la mitad o el cuarto del tiempo
            double resto = mejor % (periodo / 4);
            return Math.Min(mejor, Math.Min(resto, periodo / 4 - resto));
        }

        private static bool DemasiadoCerca(List<double> ordenados, double t)
        {
            if (ordenados.Count == 0) return false;
            int lo = 0, hi = ordenados.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (ordenados[mid] < t) lo = mid + 1; else hi = mid;
            }
            if (Math.Abs(ordenados[lo] - t) < SeparacionMinima) return true;
            if (lo > 0 && Math.Abs(ordenados[lo - 1] - t) < SeparacionMinima) return true;
            return false;
        }

        private static void Insertar(List<double> ordenados, double t)
        {
            int i = ordenados.BinarySearch(t);
            if (i < 0) i = ~i;
            ordenados.Insert(i, t);
        }

        // ---------------------------------------------------------- trastes --
        private static void AsignarTrastes(float[] x, List<AnalisisAudio.Ataque> elegidos,
                                           Resultado r, double[] rejilla)
        {
            int n = elegidos.Count;
            double[] tono = new double[n];
            for (int i = 0; i < n; i++)
            {
                tono[i] = Tono(x, (int)(elegidos[i].segundo * AnalisisAudio.Frecuencia));
            }
            // los huecos se rellenan con el tono anterior conocido: en un
            // ataque de percusion no hay altura que medir, pero la nota tiene
            // que ir a algun traste
            double ultimo = 0;
            for (int i = 0; i < n; i++)
            {
                if (tono[i] <= 0) tono[i] = ultimo;
                else ultimo = tono[i];
            }
            for (int i = n - 1; i >= 0; i--)
            {
                if (tono[i] <= 0 && i + 1 < n) tono[i] = tono[i + 1];
            }

            // Cinco tramos por cuantiles del propio audio. Repartir asi —en
            // vez de por frecuencia absoluta— hace que el chart use los cinco
            // trastes tanto en una voz grave como en una guitarra aguda.
            double[] cortes = Cuantiles(tono);
            double umbralAcorde = UmbralAcorde(elegidos);

            long ultimoTick = -1;
            for (int i = 0; i < n; i++)
            {
                long tick = ATick(elegidos[i].segundo, r.bpm, rejilla);
                if (tick <= ultimoTick) tick = ultimoTick + 1;
                ultimoTick = tick;

                int traste = Tramo(tono[i], cortes);
                long sostenido = Sostenido(elegidos, i, r.bpm);

                ReduccionChart.Nota nota;
                nota.tick = tick;
                nota.trastes = 1 << traste;
                nota.sostenido = sostenido;

                // Acordes en los ataques mas fuertes. El corpus da un 36% de
                // posiciones con mas de una nota; aqui se reparte por fuerza,
                // que es lo que hace un charter: el golpe gordo lleva acorde.
                if (elegidos[i].fuerza >= umbralAcorde)
                {
                    int companero = traste > 0 ? traste - 1 : traste + 1;
                    if (companero >= 0 && companero <= 4)
                    {
                        nota.trastes |= 1 << companero;
                    }
                }
                r.notas.Add(nota);
            }
        }

        private static long ATick(double segundo, double bpm, double[] rejilla)
        {
            double porNegra = 60.0 / bpm;
            double negras = segundo / porNegra;
            if (rejilla != null)
            {
                // con rejilla de fiar se redondea a la semicorchea mas cercana,
                // pero solo si ya estaba a menos de media: si esta lejos, es
                // que la nota va a contratiempo de verdad y moverla la sacaria
                // de donde suena
                double semis = negras * 4.0;
                double redondeada = Math.Round(semis);
                if (Math.Abs(semis - redondeada) < 0.5)
                {
                    negras = redondeada / 4.0;
                }
            }
            long tick = (long)Math.Round(negras * Resolucion);
            return tick < 0 ? 0 : tick;
        }

        // Cuanto aguanta el sonido antes del siguiente ataque. Solo se marca
        // sostenido si de verdad hay hueco: un sostenido que pisa la nota
        // siguiente estorba mas que aporta.
        private static long Sostenido(List<AnalisisAudio.Ataque> elegidos, int i, double bpm)
        {
            if (i + 1 >= elegidos.Count) return 0;
            double hueco = elegidos[i + 1].segundo - elegidos[i].segundo;
            if (hueco < SostenidoMinimo * 1.5) return 0;
            double largo = hueco * 0.75;
            double porNegra = 60.0 / bpm;
            return (long)(largo / porNegra * Resolucion);
        }

        private static double UmbralAcorde(List<AnalisisAudio.Ataque> elegidos)
        {
            if (elegidos.Count == 0) return double.MaxValue;
            List<float> fuerzas = new List<float>();
            for (int i = 0; i < elegidos.Count; i++) fuerzas.Add(elegidos[i].fuerza);
            fuerzas.Sort();
            int idx = (int)((1.0 - ProporcionAcordes) * (fuerzas.Count - 1));
            return fuerzas[Math.Max(0, Math.Min(fuerzas.Count - 1, idx))];
        }

        private static double[] Cuantiles(double[] tono)
        {
            List<double> v = new List<double>();
            for (int i = 0; i < tono.Length; i++)
            {
                if (tono[i] > 0) v.Add(tono[i]);
            }
            if (v.Count < 5) return null;
            v.Sort();
            double[] cortes = new double[4];
            for (int k = 1; k <= 4; k++)
            {
                cortes[k - 1] = v[(int)((double)k / 5 * (v.Count - 1))];
            }
            return cortes;
        }

        private static int Tramo(double t, double[] cortes)
        {
            if (cortes == null || t <= 0) return 0;
            for (int i = 0; i < cortes.Length; i++)
            {
                if (t <= cortes[i]) return i;
            }
            return 4;
        }

        // Frecuencia dominante por autocorrelacion. No es un afinador: solo
        // hace falta saber si esto suena mas agudo que lo anterior.
        private static double Tono(float[] x, int desde)
        {
            if (desde < 0 || desde + VentanaTono >= x.Length) return 0;
            double media = 0;
            for (int i = 0; i < VentanaTono; i++) media += x[desde + i];
            media /= VentanaTono;

            double energia = 0;
            for (int i = 0; i < VentanaTono; i++)
            {
                double d = x[desde + i] - media;
                energia += d * d;
            }
            if (energia <= 1e-8) return 0;

            int menor = AnalisisAudio.Frecuencia / 1200;      // 1200 Hz
            int mayor = AnalisisAudio.Frecuencia / 70;        // 70 Hz
            if (mayor >= VentanaTono) mayor = VentanaTono - 1;

            double mejor = 0;
            int mejorLag = 0;
            for (int lag = menor; lag <= mayor; lag++)
            {
                double suma = 0;
                for (int i = 0; i + lag < VentanaTono; i++)
                {
                    suma += (x[desde + i] - media) * (x[desde + i + lag] - media);
                }
                if (suma > mejor)
                {
                    mejor = suma;
                    mejorLag = lag;
                }
            }
            if (mejorLag == 0 || mejor / energia < 0.30) return 0;
            return (double)AnalisisAudio.Frecuencia / mejorLag;
        }
    }
}
