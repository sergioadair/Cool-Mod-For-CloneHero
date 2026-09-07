using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MelonLoader;

namespace CloneHeroMod
{
    // "Generate All Missing Difficulties" y "Restore All Song Charts": lo mismo
    // que las dos filas de Song Options, pero sobre la biblioteca entera.
    //
    // Va en Settings > General, debajo de Calculate Difficulty, porque es ahi
    // donde ya vive el trabajo que barre toda la biblioteca: mismo cartel de
    // progreso, mismo reparto en hilos, mismo sitio donde el jugador lo busca.
    //
    // POR QUE HAY UN RESTORE EN LOTE. Escribir en miles de archivos sin una
    // forma de deshacerlo tambien en lote es una trampa: si al jugador no le
    // convence el resultado, no se le puede pedir que entre cancion por cancion
    // en 3900 canciones. El de una en una sigue estando para casos sueltos.
    //
    // La lista de canciones se recoge en el hilo principal —viene del juego— y
    // el trabajo va aparte, que solo toca archivos.
    public static class GeneradorLote
    {
        public const string NombreGenerar = "Generate All Missing Difficulties";
        public const string NombreRestaurar = "Restore All Song Charts";

        private struct Tarea
        {
            public string ruta;
            public bool esMidi;
            public bool esSng;
        }

        private static volatile bool corriendo;
        private static volatile bool restaurando;
        private static int hechas;         // canciones ya miradas
        private static int total;
        private static int cambiadas;      // canciones a las que se les anadio algo
        private static int dificultades;   // dificultades generadas en total
        private static int fallidas;
        private static int pendientes;     // .sng en espera de reinicio

        public static bool Corriendo { get { return corriendo; } }
        public static int Total { get { return total; } }
        public static int Hechas { get { return hechas; } }
        public static int Cambiadas { get { return cambiadas; } }
        public static int Dificultades { get { return dificultades; } }
        public static int Fallidas { get { return fallidas; } }
        public static int Pendientes { get { return pendientes; } }
        public static bool Restaurando { get { return restaurando; } }

        // ------------------------------------------------------------------
        public static void Lanzar(bool restaurar)
        {
            if (corriendo || GeneradorCharts.Corriendo || CalculadorDificultad.Corriendo)
            {
                return;
            }
            List<Tarea> tareas = Recoger();
            if (tareas.Count == 0)
            {
                Aviso.Mostrar(restaurar ? NombreRestaurar : NombreGenerar,
                    "No songs found.");
                return;
            }

            corriendo = true;
            restaurando = restaurar;
            hechas = 0;
            cambiadas = 0;
            dificultades = 0;
            fallidas = 0;
            pendientes = 0;
            total = tareas.Count;
            siguiente = 0;

            Thread hilo = new Thread(delegate () { Repartir(tareas, restaurar); });
            hilo.IsBackground = true;
            hilo.Name = "CoolModLote";
            hilo.Start();
        }

        // Se recoge aqui, en el hilo principal, porque son objetos del juego.
        // A diferencia del calculo de dificultad, los .sng SI entran: desde que
        // se sabe abrirlos, tienen chart como cualquier otra.
        private static List<Tarea> Recoger()
        {
            List<Tarea> tareas = new List<Tarea>();
            var canciones = CalculadorDificultad.ListaCancionesPublica();
            if (canciones == null)
            {
                return tareas;
            }
            var vistas = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < canciones.Count; i++)
            {
                Il2Cpp.SongEntry s = canciones[i];
                if (s == null)
                {
                    continue;
                }
                try
                {
                    if (s.isEnc)
                    {
                        continue;      // cifrada: no hay nada que abrir
                    }
                    Tarea t = new Tarea();
                    t.esSng = s.isSng;
                    t.esMidi = s.IsMIDIChart;
                    t.ruta = s.isSng ? RutaSng(s) : s.ChartPath;
                    if (string.IsNullOrEmpty(t.ruta) || vistas.ContainsKey(t.ruta))
                    {
                        continue;      // la misma cancion puede salir en varias listas
                    }
                    vistas[t.ruta] = true;
                    tareas.Add(t);
                }
                catch (Exception)
                {
                    // una cancion rota no tumba la recogida entera
                }
            }
            return tareas;
        }

        private static string RutaSng(Il2Cpp.SongEntry s)
        {
            string[] candidatas = { s.folderPath, s.ChartPath };
            for (int i = 0; i < candidatas.Length; i++)
            {
                if (ArchivoSng.EsSng(candidatas[i]) && File.Exists(candidatas[i]))
                {
                    return candidatas[i];
                }
            }
            return null;
        }

        // ------------------------------------------------------ hilo aparte -
        private static void Repartir(List<Tarea> tareas, bool restaurar)
        {
            DateTime inicio = DateTime.Now;
            try
            {
                GeneradorCharts.Lote = true;

                // Un nucleo libre para el juego, igual que en el calculo de
                // dificultad. Aqui ademas hay escritura de archivos, asi que
                // pasar de ocho hilos no compensa.
                int hilos = Environment.ProcessorCount - 1;
                if (hilos < 1) { hilos = 1; }
                if (hilos > 8) { hilos = 8; }
                if (hilos > tareas.Count) { hilos = tareas.Count; }

                MelonLogger.Msg("[Lote] " + (restaurar ? "restaurando " : "generando en ")
                    + tareas.Count.ToString() + " cancion(es), " + hilos.ToString()
                    + " hilos");

                Thread[] equipo = new Thread[hilos];
                for (int h = 0; h < hilos; h++)
                {
                    equipo[h] = new Thread(delegate () { Trabajar(tareas, restaurar); });
                    equipo[h].IsBackground = true;
                    equipo[h].Name = "CoolModLote" + h.ToString();
                    equipo[h].Start();
                }
                for (int h = 0; h < hilos; h++)
                {
                    equipo[h].Join();
                }

                double s = (DateTime.Now - inicio).TotalSeconds;
                MelonLogger.Msg("[Lote] terminado en " + s.ToString("0.0") + " s: "
                    + cambiadas.ToString() + " cancion(es), "
                    + dificultades.ToString() + " dificultad(es), "
                    + fallidas.ToString() + " fallo(s), "
                    + pendientes.ToString() + " en espera");
                Terminado(restaurar);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Lote] " + ex);
                Aviso.Mostrar(restaurar ? NombreRestaurar : NombreGenerar,
                    "Failed: " + ex.Message);
            }
            finally
            {
                GeneradorCharts.Lote = false;
                corriendo = false;
                // Los charts han cambiado: lo perfilado ya no vale.
                PerfilChart.Vaciar();
            }
        }

        private static int siguiente;

        private static void Trabajar(List<Tarea> tareas, bool restaurar)
        {
            while (true)
            {
                int i = Interlocked.Increment(ref siguiente) - 1;
                if (i >= tareas.Count)
                {
                    return;
                }
                Tarea t = tareas[i];
                try
                {
                    if (restaurar)
                    {
                        Restaurar(t);
                    }
                    else
                    {
                        Generar(t);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref fallidas);
                    if (fallidas <= 5)
                    {
                        MelonLogger.Warning("[Lote] " + Path.GetFileName(t.ruta ?? "?")
                            + ": " + ex.Message);
                    }
                }
                Interlocked.Increment(ref hechas);
            }
        }

        private static void Generar(Tarea t)
        {
            if (string.IsNullOrEmpty(t.ruta) || !File.Exists(t.ruta))
            {
                return;
            }
            int n = GeneradorCharts.UnaEnLote(t.ruta, t.esMidi, t.esSng);
            if (n <= 0)
            {
                return;
            }
            Interlocked.Increment(ref cambiadas);
            Interlocked.Add(ref dificultades, n);
            if (t.esSng && File.Exists(t.ruta + ArchivoSng.Pendiente))
            {
                Interlocked.Increment(ref pendientes);
            }
        }

        private static void Restaurar(Tarea t)
        {
            if (string.IsNullOrEmpty(t.ruta))
            {
                return;
            }
            string copia = GeneradorCharts.RutaCopia(t.ruta);
            if (!File.Exists(copia))
            {
                return;      // esta cancion nunca se genero
            }
            if (t.esSng)
            {
                ArchivoSng s = ArchivoSng.Leer(t.ruta);
                bool esMidi;
                ArchivoSng.Entrada dentro = s.BuscarChart(out esMidi);
                if (dentro == null)
                {
                    return;
                }
                if (!s.Escribir(t.ruta, dentro.nombre, File.ReadAllBytes(copia)))
                {
                    GeneradorCharts.AnotarPendiente(t.ruta);
                    Interlocked.Increment(ref pendientes);
                }
            }
            else
            {
                File.Copy(copia, t.ruta, true);
            }
            Interlocked.Increment(ref cambiadas);
        }

        private static void Terminado(bool restaurar)
        {
            if (restaurar)
            {
                Aviso.Mostrar(NombreRestaurar, cambiadas == 0
                    ? "Nothing to restore."
                    : cambiadas.ToString() + " song(s) restored.\n\nScan Songs to apply."
                      + (pendientes > 0 ? "\nSome .sng need a restart." : ""));
                return;
            }
            Aviso.Mostrar(NombreGenerar, cambiadas == 0
                ? "Nothing to do - every song has them all."
                : dificultades.ToString() + " added across "
                  + cambiadas.ToString() + " song(s).\n\nScan Songs to play them."
                  + (pendientes > 0 ? "\nSome .sng need a restart." : ""));
        }
    }
}
