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
    //
    // ---------------------------------------------------------------------
    // TOCABLE ANTES QUE FIEL
    //
    // La primera version ponia cada nota donde sonaba el golpe, al
    // milisegundo, y salia mas dificil de lo que decia su numero. Medido
    // contra 444 charts humanos de la misma dificultad (25-50):
    //
    //                                         humanos   generados
    //     notas en la rejilla del compas        100%      2-15%
    //     notas a menos de 120 ms de la otra      4%     16-22%
    //     rapidas que hay que rasguear            2%      9-21%
    //     saltos de 2 trastes o mas              14%     15-36%
    //
    // La densidad era la misma (3,6 contra 3,3 notas por segundo); lo que
    // cambiaba era COMO estaban puestas. Una nota fuera de rejilla no se
    // puede tocar "a sentimiento", solo reaccionando. Asi que ahora:
    //
    //   - siempre se cuadricula, contra el tempo de la cancion (ver Mapa);
    //   - 150 ms como minimo entre notas, y si aun asi dos quedan a menos
    //     de un tercio de pulso, la segunda va suelta y en otro traste, para
    //     que el juego la haga HOPO en vez de pedir un rasgueo imposible;
    //   - de traste en traste de uno en uno, o de dos si hay tiempo.
    public static class ChartDesdeAudio
    {
        public const int Resolucion = 192;          // la de todos los charts medidos
        public const double DensidadObjetivo = 3.6;  // notas por segundo (mediana del corpus)
        // Segundos entre dos notas. Con 70 ms salian rafagas que un humano no
        // pone en canciones de esta dificultad: ver la cabecera.
        public const double SeparacionMinima = 0.150;
        public const double FraccionQueSobra = 0.65;    // se conserva el 65% mas fuerte
        public const double ProporcionAcordes = 0.36;   // mediana medida del corpus
        public const double SostenidoMinimo = 0.25;     // segundos para que valga la pena
        public const int VentanaTono = 2048;            // ~93 ms para estimar el tono

        // El umbral de HOPO de Clone Hero: 65 ticks a resolucion 192, un
        // tercio de negra escaso. Mas juntas que eso, dos notas sueltas en
        // trastes distintos se tocan sin rasguear.
        public const int UmbralHopo = 65;

        // Tiempo que hace falta para mover la mano dos trastes de golpe.
        public const double TiempoParaSaltar = 0.40;

        // Tramos para medir la rejilla, y cuanto tienen que apartarse entre si
        // para seguir a una cancion que se adelanta o se atrasa. Ver Mapa.
        public const double VentanaTempo = 20.0;      // segundos
        public const double DerivaMinima = 0.03;      // fraccion de pulso
        // Por encima de esto (en fraccion de semicorchea; el azar da 0,25) se
        // considera que el audio no tiene pulso. Ver ElegirPeriodo.
        public const double SinPulso = 0.20;

        // Los pesos de Figuras, en las mismas unidades que la puntuacion de una
        // nota (de 0,27 a 0,86):
        //
        //   CambioDeFigura  cambiar de figura de un pulso al siguiente (la
        //                   mitad si una de las dos es silencio);
        //   Inventar        poner nota donde no suena nada, ademas del precio;
        //   Rapida          cada par de notas a menos de LimiteRapida: sin
        //                   esto, a 96 bpm salian rachas de semicorcheas y el
        //                   72% de las notas eran HOPO (humanos: 12%);
        //   Contraste       exponente sobre el percentil del ataque. Con 2,
        //                   un ataque flojo vale bastante menos que uno fuerte
        //                   y las figuras siguen donde de verdad esta la fuerza.
        //
        // Calibrados regenerando seis audios (tres canciones y tres memes) y
        // midiendo contra 444 charts humanos de dificultad 25-50: el ritmo
        // cambia de una nota a la siguiente el 39% de las veces en mediana
        // (humanos 41%; antes de esto, 70%).
        public const double CambioDeFigura = 0.15;
        public const double Inventar = 0.35;
        public const double Rapida = 0.20;
        public const double Contraste = 2.0;
        public const double LimiteRapida = 0.20;
        public const double ConfianzaSeguidor = 0.85;

        // ENTRADILLA. Dos segundos en blanco antes de que empiece nada, para
        // que al darle a jugar no te caiga una nota encima de inmediato.
        //
        // Las notas se corren dos segundos hacia delante y el song.ini lleva
        // delay = -2000 para compensar, que es lo que mantiene el audio en su
        // sitio. El signo no es un adivinanza: medido sobre las canciones de
        // la biblioteca que usan delay, sumarlo a los tiempos del chart deja
        // las notas humanas encima de los ataques del audio el 90-99% de las
        // veces, contra un 67-70% restandolo. Y negativos los hay en circulacion
        // —seis canciones de la biblioteca, la menor en -2305 ms—, asi que el
        // juego los admite.
        public const double Entradilla = 2.0;

        // STAR POWER. Repartido como lo reparten los humanos, medido sobre 595
        // canciones del corpus oficial (el 99% llevan):
        //
        //     frases por minuto          2,68
        //     notas dentro de cada una   8
        //     lo que dura una frase      2,1 s
        //     hueco entre frases         17 s
        //     notas cubiertas            10%
        //
        // Donde se ponen: en los tramos mas fuertes, que es donde un charter
        // las pone —el estribillo, el riff gordo— y ademas lo unico que
        // podemos reconocer sin entender la cancion.
        public const double FrasesPorMinuto = 2.68;
        public const int NotasPorFrase = 8;
        public const double HuecoEntreFrases = 15.0;

        public class Resultado
        {
            public List<ReduccionChart.Nota> notas = new List<ReduccionChart.Nota>();
            public List<ReduccionChart.Fase> fases = new List<ReduccionChart.Fase>();
            public double bpm = 120;
            public double confianzaRitmo;
            public bool cuadriculado;
            public bool ternario;
            // Cuanto se movio de media cada nota para caer en la rejilla, y
            // el peor caso, en milisegundos. Si sale alto, la rejilla no es
            // la de la cancion.
            public double movidaMedia;
            public double movidaP90;
            public int compases;
            public int compasesCopiados;
            public int inventadas;
            // El mapa de tempo: (tick, bpm x 1000), uno por cada cambio.
            public List<KeyValuePair<long, long>> tempos = new List<KeyValuePair<long, long>>();
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

            // Se cuadricula SIEMPRE. Antes solo con el seguidor muy seguro,
            // con la idea de que una nota fuera de rejilla se juega igual de
            // bien. Medido, no: es lo que mas separaba estos charts de los
            // humanos. Y el riesgo de equivocarse de rejilla es acotado: una
            // nota se mueve como mucho media subdivision, unos 60 ms a 120 bpm.
            double[] lineas = Mapa(ataques, r.bpm, ritmo.confianza, r.duracion);
            r.ternario = Ternario(elegidos, lineas);
            r.cuadriculado = true;
            r.tempos = Tempos(lineas);
            r.bpm = BpmMediano(lineas);

            long[] ticks;
            int objetivo = elegidos.Count;      // cuantas notas, ya decidido arriba
            elegidos = Figuras(ataques, lineas, r.ternario, objetivo, out ticks,
                               out r.inventadas);
            Movido(elegidos, ticks, lineas, r);
            AsignarTrastes(x, elegidos, ticks, lineas, r);

            // Lo que suena igual, igual: ver Repeticion.
            List<float> fuerzas = new List<float>();
            for (int i = 0; i < elegidos.Count; i++) fuerzas.Add(elegidos[i].fuerza);
            List<float> fuerzasNuevas;
            r.notas = Repeticion.Aplicar(x, env, lineas, r.notas, fuerzas, out fuerzasNuevas,
                                         out r.compasesCopiados, out r.compases);
            // El Star Power mira la fuerza y el instante de cada nota, asi que
            // se le dan los de las notas que quedaron.
            elegidos = new List<AnalisisAudio.Ataque>();
            for (int i = 0; i < r.notas.Count; i++)
            {
                AnalisisAudio.Ataque a;
                a.segundo = Segundo(lineas, r.notas[i].tick) - Entradilla;
                a.fuerza = fuerzasNuevas[i];
                elegidos.Add(a);
            }
            r.fases = Frases(elegidos, r.notas, r.duracion);
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
                                           long[] ticks, double[] lineas, Resultado r)
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

            int anterior = -1;
            for (int i = 0; i < n; i++)
            {
                long tick = ticks[i];
                bool rapida = i > 0 && tick - ticks[i - 1] <= UmbralHopo;
                double hueco = i > 0 ? elegidos[i].segundo - elegidos[i - 1].segundo : 99;

                // El tono dice adonde ir; la mano, cuanto se puede mover. De
                // uno en uno, o de dos si hay tiempo: los humanos saltan dos o
                // mas trastes en el 14% de las notas, y el tono crudo daba
                // hasta un 36%.
                int deseado = Tramo(tono[i], cortes);
                int traste = deseado;
                if (anterior >= 0)
                {
                    int paso = hueco >= TiempoParaSaltar ? 2 : 1;
                    int d = deseado - anterior;
                    if (d > paso) d = paso;
                    if (d < -paso) d = -paso;
                    traste = anterior + d;
                }
                // Una rapida en el mismo traste no puede ser HOPO: habria que
                // rasguear dos veces seguidas a toda velocidad. Se mueve un
                // traste, hacia donde va el tono.
                if (rapida && traste == anterior)
                {
                    bool sube = tono[i] > tono[i - 1] ? true
                              : tono[i] < tono[i - 1] ? false
                              : traste < 4;
                    traste = sube ? (traste < 4 ? traste + 1 : traste - 1)
                                  : (traste > 0 ? traste - 1 : traste + 1);
                }
                anterior = traste;

                ReduccionChart.Nota nota;
                nota.tick = tick;
                nota.trastes = 1 << traste;
                nota.sostenido = Sostenido(elegidos, ticks, lineas, i);

                // Acordes en los ataques mas fuertes. El corpus da un 36% de
                // posiciones con mas de una nota; aqui se reparte por fuerza,
                // que es lo que hace un charter: el golpe gordo lleva acorde.
                // Menos en las rapidas: un acorde ahi tambien pide rasgueo.
                if (elegidos[i].fuerza >= umbralAcorde && !rapida)
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

        // Las frases de Star Power. Se eligen las ventanas de notas seguidas
        // que mas fuerza acumulan, con la condicion de no pisarse ni caer
        // demasiado juntas.
        //
        // Antes esto llamaba a AjustarFases con una lista vacia, o sea que
        // adaptaba fielmente a las notas nuevas... un conjunto vacio de
        // frases. Los charts generados salian sin Star Power ninguno.
        private static List<ReduccionChart.Fase> Frases(List<AnalisisAudio.Ataque> elegidos,
                                                        List<ReduccionChart.Nota> notas,
                                                        double duracion)
        {
            List<ReduccionChart.Fase> salida = new List<ReduccionChart.Fase>();
            int n = Math.Min(elegidos.Count, notas.Count);
            if (n < NotasPorFrase * 2 || duracion < 30)
            {
                return salida;      // muy corta para que el Star Power aporte algo
            }

            int objetivo = (int)Math.Round(duracion / 60.0 * FrasesPorMinuto);
            if (objetivo < 1) objetivo = 1;
            if (objetivo > n / NotasPorFrase) objetivo = n / NotasPorFrase;

            // fuerza acumulada de cada ventana de NotasPorFrase notas seguidas
            int ventanas = n - NotasPorFrase + 1;
            double[] puntos = new double[ventanas];
            for (int i = 0; i < ventanas; i++)
            {
                double suma = 0;
                for (int k = 0; k < NotasPorFrase; k++)
                {
                    suma += elegidos[i + k].fuerza;
                }
                puntos[i] = suma;
            }

            List<int> orden = new List<int>();
            for (int i = 0; i < ventanas; i++) orden.Add(i);
            orden.Sort(delegate (int a, int b) { return puntos[b].CompareTo(puntos[a]); });

            List<int> elegidas = new List<int>();
            for (int k = 0; k < orden.Count && elegidas.Count < objetivo; k++)
            {
                int i = orden[k];
                double cuando = elegidos[i].segundo;
                bool choca = false;
                for (int j = 0; j < elegidas.Count; j++)
                {
                    double otra = elegidos[elegidas[j]].segundo;
                    if (Math.Abs(cuando - otra) < HuecoEntreFrases)
                    {
                        choca = true;
                        break;
                    }
                }
                if (!choca)
                {
                    elegidas.Add(i);
                }
            }
            elegidas.Sort();

            for (int j = 0; j < elegidas.Count; j++)
            {
                int i = elegidas[j];
                int ultimo = i + NotasPorFrase - 1;
                ReduccionChart.Fase f;
                f.tick = notas[i].tick;
                // la frase tiene que tapar la ultima nota entera, sostenido
                // incluido, o esa nota se queda fuera y no puntua
                long fin = notas[ultimo].tick + Math.Max(notas[ultimo].sostenido, 1);
                f.largo = fin - f.tick;
                if (f.largo > 0)
                {
                    salida.Add(f);
                }
            }
            return salida;
        }

        // ------------------------------------------------------ mapa de tempo
        // Las lineas del mastil, en segundos del chart (entradilla incluida):
        // la linea k esta en el tick k * Resolucion.
        //
        // NO SALEN DEL SEGUIDOR DE PULSOS. Se probo y no sirve para esto: pulso
        // a pulso tiembla muchisimo —en Kudai - Escapar saltaba entre 117 y
        // 215 bpm de un pulso al siguiente— y a veces ni el tempo global es el
        // bueno: ahi dio 151, y los ataques de la cancion encajan limpios
        // (coherencia 0,71 contra 0,10) a 112.
        //
        // Lo que se mira es la REJILLA DE SEMICORCHEAS, que es contra lo que
        // se cuadran las notas. Para un periodo dado, cada ataque es un vector
        // de angulo (t / semicorchea) vueltas, con su fuerza de largo; si la
        // rejilla es la buena, apuntan todos igual y la suma sale larga. Eso
        // se mide por tramos de VentanaTempo segundos, cada uno con su propia
        // fase, para que una cancion que se adelanta o se atrasa un poco no
        // estropee la cuenta.
        //
        // Con eso: se elige el tempo, se coloca la rejilla tramo a tramo, y
        // si la cancion no se mueve, el mapa queda a tempo fijo y limpio.
        private static double[] Mapa(List<AnalisisAudio.Ataque> todos, double bpm,
                                     double confianza, double duracion)
        {
            List<AnalisisAudio.Ataque> ataques = MasFuertes(todos);
            // Con el seguidor muy seguro, su tempo es el bueno: en el corpus,
            // por encima de 0,85 acierta. Sus pulsos sueltos siguen sin valer
            // de rejilla, pero el tempo si. Es cuando duda cuando se busca otro.
            double periodo = confianza >= ConfianzaSeguidor
                ? 60.0 / Plegar(bpm)
                : ElegirPeriodo(ataques, 60.0 / bpm, duracion);
            double semi = periodo / 4;

            // Donde cae el pulso en la cancion entera. Solo decide cual de las
            // cuatro semicorcheas es el pulso; lo fino lo ponen los tramos.
            double coherenciaPulso;
            double fase = Desfase(ataques, periodo, 0, 0, duracion + 1, out coherenciaPulso);

            // La correccion de cada tramo: cuanto hay que mover la rejilla
            // para que encaje ahi, entre media semicorchea atras y adelante.
            List<double> centros = new List<double>();
            List<double> ajustes = new List<double>();
            for (double desde = 0; desde < duracion; desde += VentanaTempo / 2)
            {
                double hasta = Math.Min(duracion, desde + VentanaTempo);
                if (hasta - desde < VentanaTempo / 2 && centros.Count > 0) break;
                double coherencia;
                double d = Desfase(ataques, semi, fase, desde, hasta, out coherencia);
                if (coherencia < 0.10)
                {
                    continue;      // tramo sin pulso claro: no opina
                }
                if (ajustes.Count > 0)
                {
                    // la misma deriva que sigue, no un salto de semicorchea
                    double previo = ajustes[ajustes.Count - 1];
                    while (d - previo > semi / 2) d -= semi;
                    while (previo - d > semi / 2) d += semi;
                }
                centros.Add((desde + hasta) / 2);
                ajustes.Add(d);
            }
            // Si los tramos casi no se mueven entre si, tempo fijo: una sola
            // correccion, la mediana. Seguir variaciones de pocos ms solo
            // llenaria el mapa de cambios de tempo que nadie oye.
            if (ajustes.Count > 0)
            {
                List<double> orden = new List<double>(ajustes);
                orden.Sort();
                double mediana = orden[orden.Count / 2];
                bool fija = true;
                for (int i = 0; i < ajustes.Count; i++)
                {
                    if (Math.Abs(ajustes[i] - mediana) > DerivaMinima * periodo)
                    {
                        fija = false;
                        break;
                    }
                }
                if (fija)
                {
                    centros = new List<double> { 0 };
                    ajustes = new List<double> { mediana };
                }
            }

            List<double> pulsos = new List<double>();
            double k0 = Math.Ceiling(-fase / periodo) - 1;
            for (double k = k0; ; k++)
            {
                double t = fase + k * periodo;
                if (t > duracion + periodo) break;
                t += Interpolar(centros, ajustes, t);
                if (t < 0) continue;
                t += Entradilla;
                if (pulsos.Count == 0 || t - pulsos[pulsos.Count - 1] > periodo * 0.4)
                {
                    pulsos.Add(t);
                }
            }
            if (pulsos.Count < 2)
            {
                pulsos.Clear();
                pulsos.Add(Entradilla);
            }

            List<double> lineas = new List<double>();
            // Del cero del chart al primer pulso, un numero entero de lineas
            // a un tempo lo mas parecido posible al de la cancion.
            double primero = pulsos[0];
            double p0 = pulsos.Count > 1 ? pulsos[1] - pulsos[0] : periodo;
            int antes = Math.Max(1, (int)Math.Round(primero / p0));
            for (int k = 0; k < antes; k++)
            {
                lineas.Add(primero * k / antes);
            }
            lineas.AddRange(pulsos);

            // Y despues del ultimo pulso, al ultimo tempo, hasta pasado el
            // final del audio.
            double paso = pulsos.Count > 1
                ? pulsos[pulsos.Count - 1] - pulsos[pulsos.Count - 2] : periodo;
            double fin = duracion + Entradilla + 2 * paso;
            while (lineas[lineas.Count - 1] < fin)
            {
                lineas.Add(lineas[lineas.Count - 1] + paso);
            }
            return lineas.ToArray();
        }

        // La mitad mas fuerte de los ataques. Los flojos son sobre todo
        // relleno —ecos, colas, ruido— y empujan la rejilla hacia ningun lado.
        private static List<AnalisisAudio.Ataque> MasFuertes(List<AnalisisAudio.Ataque> todos)
        {
            List<float> f = new List<float>();
            for (int i = 0; i < todos.Count; i++) f.Add(todos[i].fuerza);
            f.Sort();
            float corte = f.Count > 0 ? f[f.Count / 2] : 0;
            List<AnalisisAudio.Ataque> salida = new List<AnalisisAudio.Ataque>();
            for (int i = 0; i < todos.Count; i++)
            {
                if (todos[i].fuerza >= corte) salida.Add(todos[i]);
            }
            return salida;
        }

        // El tempo. Dos candidatos: el del seguidor y el que mejor encaja la
        // rejilla de semicorcheas en un barrido de 60 a 210 bpm. Los dos se
        // llevan a 80-180, que es donde se lee bien un chart —la mitad o el
        // doble de un tempo dan la misma rejilla, una mas gruesa—, y gana el
        // que menos tiene que mover las notas.
        //
        // Medido en FRACCION DE SEMICORCHEA, no en milisegundos: en ms gana
        // siempre el tempo mas rapido, porque su semicorchea es mas corta, y
        // eso llevo una cancion de 96 bpm a 145. Al azar, un ataque cae de
        // media a 0,25 semicorcheas de la rejilla. Si ninguno de los dos baja
        // claramente de ahi, el audio no tiene pulso —un meme hablado, por
        // ejemplo— y ninguna rejilla va a ser "la suya": entonces se elige la
        // que menos mueve las notas en ms.
        private static double ElegirPeriodo(List<AnalisisAudio.Ataque> ataques,
                                            double periodoSeguidor, double duracion)
        {
            double mejorBpm = 0, mejor = -1;
            for (double b = 60; b <= 210; b *= 1.002)
            {
                double c = CoherenciaPorTramos(ataques, 60.0 / b / 4, duracion);
                if (c > mejor)
                {
                    mejor = c;
                    mejorBpm = b;
                }
            }
            double[] candidatos = { Plegar(60.0 / periodoSeguidor), Plegar(mejorBpm) };
            double porFraccion = candidatos[0], menorFraccion = double.MaxValue;
            double porMs = candidatos[0], menorMs = double.MaxValue;
            for (int i = 0; i < candidatos.Length; i++)
            {
                if (candidatos[i] <= 0) continue;
                double semi = 60.0 / candidatos[i] / 4;
                double m = Movimiento(ataques, semi, duracion);
                if (m / semi < menorFraccion)
                {
                    menorFraccion = m / semi;
                    porFraccion = candidatos[i];
                }
                if (m < menorMs)
                {
                    menorMs = m;
                    porMs = candidatos[i];
                }
            }
            return 60.0 / (menorFraccion < SinPulso ? porFraccion : porMs);
        }

        private static double Plegar(double bpm)
        {
            if (bpm <= 0) return 0;
            while (bpm < 80) bpm *= 2;
            while (bpm >= 180) bpm /= 2;
            return bpm;
        }

        // Coherencia media por tramos, cada uno con su fase.
        private static double CoherenciaPorTramos(List<AnalisisAudio.Ataque> ataques,
                                                  double semi, double duracion)
        {
            double suma = 0;
            int n = 0;
            for (double desde = 0; desde < duracion; desde += VentanaTempo / 2)
            {
                double c;
                Desfase(ataques, semi, 0, desde, desde + VentanaTempo, out c);
                suma += c;
                n++;
            }
            return n > 0 ? suma / n : 0;
        }

        // Cuanto se moverian de media los ataques para caer en la rejilla,
        // colocada tramo a tramo.
        private static double Movimiento(List<AnalisisAudio.Ataque> ataques, double semi,
                                         double duracion)
        {
            double suma = 0;
            int n = 0;
            for (double desde = 0; desde < duracion; desde += VentanaTempo)
            {
                double c;
                double fase = Desfase(ataques, semi, 0, desde, desde + VentanaTempo, out c);
                for (int i = 0; i < ataques.Count; i++)
                {
                    double t = ataques[i].segundo;
                    if (t < desde || t >= desde + VentanaTempo) continue;
                    double x = (t - fase) / semi;
                    suma += Math.Abs(x - Math.Round(x)) * semi;
                    n++;
                }
            }
            return n > 0 ? suma / n : double.MaxValue;
        }

        // La suma de vectores: devuelve donde cae la rejilla respecto a
        // "fase", en segundos, y en coherencia lo larga que sale la suma
        // comparada con la fuerza total (1 = todos los ataques en la rejilla).
        private static double Desfase(List<AnalisisAudio.Ataque> ataques, double periodo,
                                      double fase, double desde, double hasta,
                                      out double coherencia)
        {
            double re = 0, im = 0, total = 0;
            for (int i = 0; i < ataques.Count; i++)
            {
                double t = ataques[i].segundo;
                if (t < desde || t >= hasta) continue;
                double w = ataques[i].fuerza;
                double ang = 2 * Math.PI * (t - fase) / periodo;
                re += w * Math.Cos(ang);
                im += w * Math.Sin(ang);
                total += w;
            }
            if (total <= 0)
            {
                coherencia = 0;
                return 0;
            }
            coherencia = Math.Sqrt(re * re + im * im) / total;
            return Math.Atan2(im, re) / (2 * Math.PI) * periodo;
        }

        private static double Interpolar(List<double> x, List<double> y, double t)
        {
            if (x.Count == 0) return 0;
            if (t <= x[0]) return y[0];
            if (t >= x[x.Count - 1]) return y[y.Count - 1];
            int i = 1;
            while (x[i] < t) i++;
            double f = (t - x[i - 1]) / (x[i] - x[i - 1]);
            return y[i - 1] + (y[i] - y[i - 1]) * f;
        }

        // Posicion en pulsos (linea + fraccion) de un segundo del chart.
        private static double Pulsos(double[] lineas, double t)
        {
            if (t <= lineas[0]) return 0;
            int lo = 0, hi = lineas.Length - 1;
            if (t >= lineas[hi]) return hi;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (lineas[mid] <= t) lo = mid; else hi = mid;
            }
            return lo + (t - lineas[lo]) / (lineas[lo + 1] - lineas[lo]);
        }

        // Binario o ternario, para la cancion entera: mezclar las dos
        // rejillas nota a nota haria saltar el ritmo de un lado a otro por
        // puro ruido. Ternario solo si encaja claramente mejor.
        private static bool Ternario(List<AnalisisAudio.Ataque> notas, double[] lineas)
        {
            if (notas.Count < 16) return false;
            double errBin = 0, errTer = 0;
            for (int i = 0; i < notas.Count; i++)
            {
                double p = Pulsos(lineas, notas[i].segundo + Entradilla);
                double f = p - Math.Floor(p);
                errBin += Math.Abs(f * 4 - Math.Round(f * 4)) / 4;
                errTer += Math.Abs(f * 3 - Math.Round(f * 3)) / 3;
            }
            return errTer < errBin * 0.8;
        }

        private static double Segundo(double[] lineas, long tick)
        {
            double pulso = (double)tick / Resolucion;
            int k = Math.Max(0, Math.Min(lineas.Length - 2, (int)pulso));
            return lineas[k] + (pulso - k) * (lineas[k + 1] - lineas[k]);
        }

        private static void Movido(List<AnalisisAudio.Ataque> elegidos, long[] ticks,
                                   double[] lineas, Resultado r)
        {
            List<double> d = new List<double>();
            for (int i = 0; i < elegidos.Count; i++)
            {
                double pulso = (double)ticks[i] / Resolucion;
                int k = Math.Max(0, Math.Min(lineas.Length - 2, (int)pulso));
                double t = lineas[k] + (pulso - k) * (lineas[k + 1] - lineas[k]);
                d.Add(Math.Abs(t - Entradilla - elegidos[i].segundo) * 1000.0);
            }
            if (d.Count == 0) return;
            double suma = 0;
            for (int i = 0; i < d.Count; i++) suma += d[i];
            d.Sort();
            r.movidaMedia = suma / d.Count;
            r.movidaP90 = d[d.Count * 9 / 10];
        }

        // ----------------------------------------------------------- figuras
        // EL RITMO SE ELIGE POR FIGURAS, NO NOTA A NOTA.
        //
        // Antes se cogian los ataques mas fuertes de uno en uno, sin mirar a
        // los de al lado, y luego se cuadraban. Cada nota era razonable por
        // separado, pero en fila salian negra, semicorchea, corchea con
        // puntillo... Medido: el ritmo cambiaba de una nota a la siguiente el
        // 70% de las veces, contra un 41% en los charts humanos de la misma
        // dificultad. Un humano no piensa nota a nota sino por figuras: "aqui
        // van corcheas", y las sostiene mientras la musica las sostiene.
        //
        // Asi que cada pulso se rellena con una figura —que semicorcheas del
        // pulso llevan nota: negra, dos corcheas, silencio...— y la secuencia
        // de figuras de toda la cancion se elige a la vez, por programacion
        // dinamica, sopesando:
        //
        //   - lo que hay debajo: cada nota puntua segun la fuerza del ataque
        //     que cae en su sitio, por percentil dentro de la cancion. Es la
        //     escala medida en el corpus: del 27% de los ataques mas flojos
        //     al 86% de los mas fuertes acaban siendo nota;
        //   - un precio por nota, que se ajusta solo hasta dar la densidad
        //     decidida arriba;
        //   - un coste por cambiar de figura respecto al pulso anterior: es
        //     lo que forma las rachas;
        //   - y un castigo por poner nota donde no suena nada. Se puede, para
        //     no romper una racha evidente, pero cuesta: inventar notas es
        //     dejar de representar el audio.
        //
        // Y ninguna figura deja dos notas a menos de SeparacionMinima.
        private static List<AnalisisAudio.Ataque> Figuras(List<AnalisisAudio.Ataque> ataques,
            double[] lineas, bool ternario, int objetivo, out long[] ticks,
            out int inventadas)
        {
            inventadas = 0;
            int partes = ternario ? 3 : 4;
            int pulsos = lineas.Length - 1;
            int huecos = pulsos * partes;

            // Lo que suena en cada semicorchea: el ataque mas fuerte que cae
            // en ella, puntuado por su percentil en la cancion.
            List<float> fuerzas = new List<float>();
            for (int i = 0; i < ataques.Count; i++) fuerzas.Add(ataques[i].fuerza);
            fuerzas.Sort();
            double[] prueba = new double[huecos];
            int[] cual = new int[huecos];
            for (int h = 0; h < huecos; h++) cual[h] = -1;
            for (int i = 0; i < ataques.Count; i++)
            {
                double p = Pulsos(lineas, ataques[i].segundo + Entradilla);
                int h = (int)Math.Round(p * partes);
                if (h < 0 || h >= huecos) continue;
                int rango = fuerzas.BinarySearch(ataques[i].fuerza);
                if (rango < 0) rango = ~rango;
                double e = 0.27 + 0.59 * Math.Pow((double)rango / Math.Max(1, fuerzas.Count - 1), Contraste);
                if (e > prueba[h])
                {
                    prueba[h] = e;
                    cual[h] = i;
                }
            }

            // El precio por nota, por biseccion, hasta no pasarse del objetivo.
            double bajo = 0, alto = 2;
            int[] figuras = null;
            for (int vuelta = 0; vuelta < 24; vuelta++)
            {
                double precio = (bajo + alto) / 2;
                int notas;
                int[] f = Resolver(prueba, lineas, partes, precio, out notas);
                if (notas > objetivo)
                {
                    bajo = precio;
                }
                else
                {
                    alto = precio;
                    figuras = f;
                }
            }
            if (figuras == null)
            {
                int ignorar;
                figuras = Resolver(prueba, lineas, partes, alto, out ignorar);
            }

            // De figuras a notas. Una nota sin ataque debajo toma el instante
            // de su semicorchea y una fuerza baja: no merece acorde ni pesa
            // en el Star Power.
            float floja = fuerzas.Count > 0 ? fuerzas[fuerzas.Count / 4] : 0;
            List<AnalisisAudio.Ataque> salida = new List<AnalisisAudio.Ataque>();
            List<long> lista = new List<long>();
            for (int k = 0; k < pulsos; k++)
            {
                for (int pos = 0; pos < partes; pos++)
                {
                    if ((figuras[k] & (1 << pos)) == 0) continue;
                    int h = k * partes + pos;
                    AnalisisAudio.Ataque a;
                    if (cual[h] >= 0)
                    {
                        a = ataques[cual[h]];
                    }
                    else
                    {
                        a.segundo = Segundo(lineas, (long)h * Resolucion / partes) - Entradilla;
                        a.fuerza = floja;
                        inventadas++;
                    }
                    salida.Add(a);
                    lista.Add((long)h * Resolucion / partes);
                }
            }
            ticks = lista.ToArray();
            return salida;
        }

        // La programacion dinamica: la mejor figura de cada pulso dada la del
        // anterior. Devuelve la figura elegida para cada pulso.
        private static int[] Resolver(double[] prueba, double[] lineas, int partes,
                                      double precio, out int notas)
        {
            int pulsos = lineas.Length - 1;
            int figuras = 1 << partes;
            double[] antes = new double[figuras];
            double[] ahora = new double[figuras];
            int[,] desde = new int[pulsos, figuras];
            for (int f = 0; f < figuras; f++) antes[f] = f == 0 ? 0 : double.NegativeInfinity;

            for (int k = 0; k < pulsos; k++)
            {
                double hueco = (lineas[k + 1] - lineas[k]) / partes;
                for (int f = 0; f < figuras; f++)
                {
                    ahora[f] = double.NegativeInfinity;
                    if (!Cabe(f, partes, hueco)) continue;
                    double propio = -Rapida * RapidasDentro(f, partes, hueco);
                    for (int pos = 0; pos < partes; pos++)
                    {
                        if ((f & (1 << pos)) == 0) continue;
                        double e = prueba[k * partes + pos];
                        propio += e > 0 ? e - precio : -precio - Inventar;
                    }
                    double mejor = double.NegativeInfinity;
                    int de = 0;
                    for (int g = 0; g < figuras; g++)
                    {
                        if (double.IsNegativeInfinity(antes[g])) continue;
                        if (!Junta(g, f, partes, hueco)) continue;
                        double v = antes[g];
                        if (RapidaEnJunta(g, f, partes, hueco)) v -= Rapida;
                        if (g != f) v -= (g == 0 || f == 0) ? CambioDeFigura / 2 : CambioDeFigura;
                        if (v > mejor)
                        {
                            mejor = v;
                            de = g;
                        }
                    }
                    if (double.IsNegativeInfinity(mejor)) continue;
                    ahora[f] = mejor + propio;
                    desde[k, f] = de;
                }
                double[] t = antes; antes = ahora; ahora = t;
            }

            int fin = 0;
            for (int f = 1; f < figuras; f++)
            {
                if (antes[f] > antes[fin]) fin = f;
            }
            int[] salida = new int[pulsos];
            notas = 0;
            for (int k = pulsos - 1; k >= 0; k--)
            {
                salida[k] = fin;
                for (int pos = 0; pos < partes; pos++)
                {
                    if ((fin & (1 << pos)) != 0) notas++;
                }
                fin = desde[k, fin];
            }
            return salida;
        }

        private static int RapidasDentro(int figura, int partes, double hueco)
        {
            int previa = -1, n = 0;
            for (int pos = 0; pos < partes; pos++)
            {
                if ((figura & (1 << pos)) == 0) continue;
                if (previa >= 0 && (pos - previa) * hueco < LimiteRapida) n++;
                previa = pos;
            }
            return n;
        }

        private static bool RapidaEnJunta(int antes, int despues, int partes, double hueco)
        {
            if (antes == 0 || despues == 0) return false;
            int ultima = 0, primera = 0;
            for (int pos = partes - 1; pos >= 0; pos--)
            {
                if ((antes & (1 << pos)) != 0) { ultima = pos; break; }
            }
            for (int pos = 0; pos < partes; pos++)
            {
                if ((despues & (1 << pos)) != 0) { primera = pos; break; }
            }
            return (partes - ultima + primera) * hueco < LimiteRapida;
        }

        // Dentro de un pulso, ninguna pareja de notas mas cerca de lo debido.
        private static bool Cabe(int figura, int partes, double hueco)
        {
            int previa = -1;
            for (int pos = 0; pos < partes; pos++)
            {
                if ((figura & (1 << pos)) == 0) continue;
                if (previa >= 0 && (pos - previa) * hueco < SeparacionMinima - 0.005) return false;
                previa = pos;
            }
            return true;
        }

        // Y tampoco entre la ultima de un pulso y la primera del siguiente.
        private static bool Junta(int antes, int despues, int partes, double hueco)
        {
            if (antes == 0 || despues == 0) return true;
            int ultima = 0, primera = 0;
            for (int pos = partes - 1; pos >= 0; pos--)
            {
                if ((antes & (1 << pos)) != 0) { ultima = pos; break; }
            }
            for (int pos = 0; pos < partes; pos++)
            {
                if ((despues & (1 << pos)) != 0) { primera = pos; break; }
            }
            return (partes - ultima + primera) * hueco >= SeparacionMinima - 0.005;
        }

        // Un evento de tempo por cada linea cuyo tempo cambia. bpm x 1000,
        // que es como lo guarda el formato.
        private static List<KeyValuePair<long, long>> Tempos(double[] lineas)
        {
            List<KeyValuePair<long, long>> t = new List<KeyValuePair<long, long>>();
            long previo = -1;
            for (int k = 0; k + 1 < lineas.Length; k++)
            {
                double d = lineas[k + 1] - lineas[k];
                if (d <= 0) continue;
                long mil = (long)Math.Round(60.0 / d * 1000.0);
                if (mil != previo)
                {
                    t.Add(new KeyValuePair<long, long>((long)k * Resolucion, mil));
                    previo = mil;
                }
            }
            return t;
        }

        private static double BpmMediano(double[] lineas)
        {
            List<double> v = new List<double>();
            for (int k = 0; k + 1 < lineas.Length; k++)
            {
                double d = lineas[k + 1] - lineas[k];
                if (d > 0) v.Add(60.0 / d);
            }
            if (v.Count == 0) return 120;
            v.Sort();
            return v[v.Count / 2];
        }

        // Cuanto aguanta el sonido antes del siguiente ataque. Solo se marca
        // sostenido si de verdad hay hueco: un sostenido que pisa la nota
        // siguiente estorba mas que aporta.
        private static long Sostenido(List<AnalisisAudio.Ataque> elegidos, long[] ticks,
                                      double[] lineas, int i)
        {
            if (i + 1 >= elegidos.Count) return 0;
            double hueco = elegidos[i + 1].segundo - elegidos[i].segundo;
            if (hueco < SostenidoMinimo * 1.5) return 0;
            double fin = elegidos[i].segundo + hueco * 0.75 + Entradilla;
            long tickFin = (long)Math.Round(Pulsos(lineas, fin) * Resolucion);
            long largo = Math.Min(tickFin, ticks[i + 1] - Resolucion / 4) - ticks[i];
            return largo > 0 ? largo : 0;
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
