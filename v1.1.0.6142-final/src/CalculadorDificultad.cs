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
        private static volatile int total;
        private static volatile int hechas;
        private static volatile int escritas;
        private static volatile int saltadas;
        private static volatile int fallidas;

        public static bool Corriendo { get { return corriendo; } }
        public static int Total { get { return total; } }
        public static int Hechas { get { return hechas; } }
        public static int Escritas { get { return escritas; } }
        public static int Saltadas { get { return saltadas; } }
        public static int Falladas { get { return fallidas; } }

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
            MelonLogger.Msg("[Dificultad] arrancando con " + total.ToString() + " canciones");

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
        private static void Procesar(List<Tarea> tareas)
        {
            DateTime inicio = DateTime.Now;
            try
            {
                for (int i = 0; i < tareas.Count; i++)
                {
                    Tarea t = tareas[i];
                    try
                    {
                        if (!File.Exists(t.chartPath) || !File.Exists(t.iniPath))
                        {
                            saltadas++;
                        }
                        else if (Dificultad.Calcular(t.chartPath, t.esMidi, out int valor))
                        {
                            Dificultad.EscribirIni(t.iniPath, valor);
                            escritas++;
                        }
                        else
                        {
                            fallidas++;
                        }
                    }
                    catch (Exception)
                    {
                        fallidas++;
                    }
                    hechas++;

                    if (hechas % 100 == 0 || hechas == total)
                    {
                        MelonLogger.Msg("[Dificultad] " + hechas.ToString() + "/" + total.ToString()
                            + "  escritas=" + escritas.ToString()
                            + "  saltadas=" + saltadas.ToString()
                            + "  fallidas=" + fallidas.ToString());
                    }
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
                    + saltadas.ToString() + " saltadas, "
                    + fallidas.ToString() + " sin datos suficientes");
                MelonLogger.Msg("[Dificultad] reinicia el juego (o reescanea) para que "
                    + "el juego recargue los song.ini");
            }
        }
    }
}
