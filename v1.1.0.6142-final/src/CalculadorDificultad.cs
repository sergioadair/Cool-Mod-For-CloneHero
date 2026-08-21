using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using MelonLoader;

namespace CloneHeroMod
{
    // Recorre la biblioteca, calcula la dificultad global de cada cancion y la
    // escribe en su song.ini bajo "diff_global".
    //
    // Reparto de trabajo, que aqui es obligatorio y no una optimizacion: los
    // objetos del juego son Il2Cpp y NO se pueden tocar desde un hilo que no
    // este adjuntado al runtime. Asi que primero se extraen las rutas en el
    // hilo principal (baratо), y el parseo pesado corre despues sobre cadenas
    // y archivos normales, sin rozar la API del juego.
    //
    // Ese parseo se reparte entre varios hilos: cada cancion es independiente
    // de las demas (lee su chart, escribe su ini) y no hay nada compartido que
    // sincronizar salvo los contadores. Se deja un nucleo libre para que el
    // juego siga yendo fino mientras tanto.
    //
    // Ademas no se recalcula lo que ya esta: si el song.ini es mas nuevo que
    // el chart y ya tiene diff_global, el valor sigue siendo valido. La unica
    // forma de invalidarlos todos de golpe es cambiar ReferenceNps, y eso se
    // detecta comparando con la referencia de la ultima pasada.
    public static class CalculadorDificultad
    {
        private const string TipoBiblioteca = "ʾʲʽʻʺʶʺʺʷʿʺ";   // SongLibrary

        private class Tarea
        {
            public string chartPath;
            public string iniPath;
            public bool esMidi;
        }

        private static volatile bool corriendo;
        private static int total;
        // Los llevan varios hilos con Interlocked, asi que no pueden ser
        // volatile: el compilador avisa de que perderian esa garantia al
        // pasarlos por referencia. Leerlos algo desfasado no importa, el
        // cartel de progreso se repinta en cada fotograma.
        private static int hechas;
        private static int escritas;
        private static int saltadas;
        private static int fallidas;
        private static int alDia;
        private static int siguienteTarea;

        public static bool Corriendo { get { return corriendo; } }
        public static int Total { get { return total; } }
        public static int Hechas { get { return hechas; } }
        public static int Escritas { get { return escritas; } }
        public static int Saltadas { get { return saltadas; } }
        public static int Falladas { get { return fallidas; } }
        public static int AlDia { get { return alDia; } }

        // ------------------------------------------------------------ arranque
        public static void Lanzar()
        {
            if (corriendo)
            {
                MelonLogger.Msg("[Dificultad] ya esta en marcha");
                return;
            }
            List<Tarea> tareas;
            try
            {
                tareas = RecogerTareas();
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Dificultad] no se pudo leer la biblioteca: " + ex);
                return;
            }
            if (tareas == null || tareas.Count == 0)
            {
                MelonLogger.Warning("[Dificultad] no hay canciones con chart accesible; "
                                    + "¿ya se escanearon las canciones?");
                return;
            }

            corriendo = true;
            total = tareas.Count;
            hechas = 0;
            escritas = 0;
            saltadas = 0;
            fallidas = 0;
            alDia = 0;
            siguienteTarea = 0;

            // Se toca la referencia AQUI, en el hilo principal: si se dejara
            // para los hilos de trabajo, varios podrian entrar a la vez en la
            // carga perezosa de los ajustes.
            referencia = Ajustes.ReferenceNps;
            reaprovechar = Math.Abs(Ajustes.UltimaRefUsada() - referencia) < 0.0001f;

            MelonLogger.Msg("[Dificultad] arrancando con " + total.ToString() + " canciones"
                + (reaprovechar
                    ? "  (se saltaran las que ya esten al dia)"
                    : "  (referencia nueva: se recalculan todas)"));

            Thread hilo = new Thread(() => Procesar(tareas));
            hilo.IsBackground = true;
            hilo.Name = "CalculoDificultad";
            hilo.Start();
        }

        // ------------------------------------------- recogida en hilo principal
        private static List<Tarea> RecogerTareas()
        {
            List<Tarea> tareas = new List<Tarea>();
            var canciones = ListaCanciones();
            if (canciones == null)
            {
                return tareas;
            }

            for (int i = 0; i < canciones.Count; i++)
            {
                Il2Cpp.SongEntry s = canciones[i];
                if (s == null)
                {
                    continue;
                }
                try
                {
                    // Las cifradas (.enc) y empaquetadas (.sng) no exponen el
                    // chart como archivo suelto: no hay nada que parsear.
                    if (s.isEnc || s.isSng)
                    {
                        continue;
                    }
                    string chart = s.ChartPath;
                    string ini = s.IniPath;
                    if (string.IsNullOrEmpty(chart) || string.IsNullOrEmpty(ini))
                    {
                        continue;
                    }
                    tareas.Add(new Tarea
                    {
                        chartPath = chart,
                        iniPath = ini,
                        esMidi = s.IsMIDIChart
                    });
                }
                catch (Exception)
                {
                    // una cancion rota no debe tumbar la recogida entera
                }
            }
            return tareas;
        }

        // La biblioteca tiene varias List<SongEntry> estaticas (filtradas,
        // visibles, etc.). Nos quedamos con la mas larga, que es la completa.
        private static Il2CppSystem.Collections.Generic.List<Il2Cpp.SongEntry> ListaCanciones()
        {
            Type t = Ofuscado.Tipo(TipoBiblioteca);
            if (t == null)
            {
                return null;
            }
            Type esperado = typeof(Il2CppSystem.Collections.Generic.List<>)
                .MakeGenericType(typeof(Il2Cpp.SongEntry));

            Il2CppSystem.Collections.Generic.List<Il2Cpp.SongEntry> mejor = null;
            PropertyInfo[] props = t.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].PropertyType != esperado)
                {
                    continue;
                }
                try
                {
                    var lista = props[i].GetValue(null)
                        as Il2CppSystem.Collections.Generic.List<Il2Cpp.SongEntry>;
                    if (lista != null && (mejor == null || lista.Count > mejor.Count))
                    {
                        mejor = lista;
                    }
                }
                catch (Exception)
                {
                }
            }
            if (mejor != null)
            {
                MelonLogger.Msg("[Dificultad] biblioteca: " + mejor.Count.ToString() + " canciones");
            }
            return mejor;
        }

        // ----------------------------------------------- trabajo en hilo aparte
        private static float referencia;
        private static bool reaprovechar;

        private static void Procesar(List<Tarea> tareas)
        {
            DateTime inicio = DateTime.Now;
            try
            {
                // Un nucleo libre para el juego. Con menos de dos hilos no
                // merece la pena montar el reparto.
                int hilos = Environment.ProcessorCount - 1;
                if (hilos < 1) { hilos = 1; }
                if (hilos > 8) { hilos = 8; }
                if (hilos > tareas.Count) { hilos = tareas.Count; }

                MelonLogger.Msg("[Dificultad] repartiendo en " + hilos.ToString() + " hilos");

                Thread[] equipo = new Thread[hilos];
                for (int h = 0; h < hilos; h++)
                {
                    equipo[h] = new Thread(() => Trabajar(tareas));
                    equipo[h].IsBackground = true;
                    equipo[h].Name = "Dificultad" + h.ToString();
                    equipo[h].Start();
                }
                for (int h = 0; h < hilos; h++)
                {
                    equipo[h].Join();
                }

                // La referencia de esta pasada queda anotada: la siguiente vez
                // sirve para saber si lo guardado sigue valiendo.
                if (fallidas + escritas + alDia > 0)
                {
                    Ajustes.GuardarUltimaRef(referencia);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Dificultad] " + ex);
            }
            finally
            {
                corriendo = false;
                // Los diff_global cambiaron: hay que tirar lo cacheado para que
                // el orden por dificultad se reconstruya con los valores nuevos.
                OrdenDificultad.LimpiarCache();
                EtiquetaDificultad.LimpiarCache();
                TimeSpan dur = DateTime.Now - inicio;
                MelonLogger.Msg("[Dificultad] TERMINADO en "
                    + ((int)dur.TotalSeconds).ToString() + " s: "
                    + escritas.ToString() + " escritas, "
                    + alDia.ToString() + " ya estaban al dia, "
                    + saltadas.ToString() + " saltadas, "
                    + fallidas.ToString() + " sin datos suficientes");
                MelonLogger.Msg("[Dificultad] reinicia el juego (o reescanea) para que "
                    + "el juego recargue los song.ini");
            }
        }

        // Bucle de cada hilo. Van pidiendo la siguiente cancion de la lista en
        // vez de repartirse trozos fijos: los charts varian mucho de tamano y
        // asi ninguno se queda parado esperando al que le toco la parte gorda.
        private static void Trabajar(List<Tarea> tareas)
        {
            while (true)
            {
                int i = Interlocked.Increment(ref siguienteTarea) - 1;
                if (i >= tareas.Count)
                {
                    return;
                }
                Tarea t = tareas[i];
                try
                {
                    if (!File.Exists(t.chartPath) || !File.Exists(t.iniPath))
                    {
                        Interlocked.Increment(ref saltadas);
                    }
                    else if (reaprovechar && YaEstaAlDia(t))
                    {
                        Interlocked.Increment(ref alDia);
                    }
                    else if (Dificultad.Calcular(t.chartPath, t.esMidi, out int valor))
                    {
                        Dificultad.EscribirIni(t.iniPath, valor);
                        Interlocked.Increment(ref escritas);
                    }
                    else
                    {
                        Interlocked.Increment(ref fallidas);
                    }
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref fallidas);
                }

                int n = Interlocked.Increment(ref hechas);
                if (n % 500 == 0 || n == total)
                {
                    MelonLogger.Msg("[Dificultad] " + n.ToString() + "/" + total.ToString()
                        + "  escritas=" + escritas.ToString()
                        + "  al dia=" + alDia.ToString()
                        + "  saltadas=" + saltadas.ToString()
                        + "  fallidas=" + fallidas.ToString());
                }
            }
        }

        // El valor guardado sigue valiendo si el song.ini es MAS NUEVO que el
        // chart (lo escribimos nosotros despues de calcularlo) y de verdad
        // lleva un diff_global dentro.
        //
        // Las dos primeras comprobaciones son solo metadatos del sistema de
        // archivos; solo si pasan se abre el ini, que ademas es diminuto al
        // lado del chart.
        private static bool YaEstaAlDia(Tarea t)
        {
            try
            {
                if (File.GetLastWriteTimeUtc(t.iniPath)
                    <= File.GetLastWriteTimeUtc(t.chartPath))
                {
                    return false;      // el chart cambio despues del calculo
                }
                return Dificultad.LeerIni(t.iniPath) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
