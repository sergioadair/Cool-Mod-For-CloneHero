using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MelonLoader;

namespace CloneHeroMod
{
    // Genera las dificultades que le faltan a una cancion, a partir de las que
    // ya tiene. El algoritmo esta en ReduccionChart; aqui va el trabajo de
    // alrededor: copia de seguridad, saber que falta, escribir y contarlo.
    //
    // SOLO SE GENERA HACIA ABAJO. De Expert salen Hard, Medium y Easy, en
    // cadena, que es como estan hechos los charts oficiales. Al reves no: de un
    // Easy no se puede sacar un Expert, porque las notas que faltan no estan en
    // ninguna parte. Si una cancion solo trae Hard, se le generan Medium y Easy
    // y Expert se queda sin hacer.
    //
    // TAMPOCO SE INVENTAN INSTRUMENTOS. Si la cancion no tiene bajo, no se le
    // pone uno sacado de la guitarra: midiendo 660 canciones con los dos, solo
    // el 75% de las notas del bajo caen donde la guitarra tambien toca. Una
    // cuarta parte estaria mal, y eso no es reducir sino inventar.
    //
    // Corre en un hilo aparte. Puede hacerlo porque no toca nada de Unity: son
    // archivos y cadenas, igual que el parser de dificultad.
    public static class GeneradorCharts
    {
        public const string Sufijo = ".coolmod.bak";

        private static volatile bool corriendo;
        private static volatile string mensaje;
        private static volatile int paso;
        private static volatile int total;

        public static bool Corriendo { get { return corriendo; } }
        public static string Mensaje { get { return mensaje; } }
        public static int Paso { get { return paso; } }
        public static int Total { get { return total; } }

        public static string RutaCopia(string chart)
        {
            return chart + Sufijo;
        }

        // ------------------------------------------------------------------
        public static void Lanzar(string chart, bool esMidi, bool esSng)
        {
            if (corriendo)
            {
                return;
            }
            if (string.IsNullOrEmpty(chart) || !File.Exists(chart))
            {
                Aviso.Mostrar("Generate Missing Difficulties",
                    "This song's chart file could not be found.");
                return;
            }
            corriendo = true;
            paso = 0;
            total = 0;
            mensaje = "Reading chart...";
            Thread hilo = new Thread(delegate () { Trabajo(chart, esMidi, esSng); });
            hilo.IsBackground = true;
            hilo.Name = "CoolModGenerar";
            hilo.Start();
        }

        private static void Trabajo(string chart, bool esMidi, bool esSng)
        {
            try
            {
                if (esSng)
                {
                    TrabajoSng(chart);
                    return;
                }

                // 1. copia de seguridad. Si ya hay una NO se toca: la primera
                //    es la de los charts originales y es la que vale.
                string copia = RutaCopia(chart);
                if (!File.Exists(copia))
                {
                    File.Copy(chart, copia);
                    MelonLogger.Msg("[Generar] copia de seguridad creada");
                }

                List<Trabajito> faltan;
                if (esMidi)
                {
                    HacerMidi(chart, out faltan);
                }
                else
                {
                    HacerChart(chart, out faltan);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Generar] " + ex);
                Aviso.Mostrar("Generate Missing Difficulties",
                    "Something went wrong. Nothing was changed.\n" + ex.Message);
            }
            finally
            {
                corriendo = false;
            }
        }

        private struct Trabajito
        {
            public string instrumento;      // nombre de pista o sufijo de seccion
            public int dificultad;          // 0 Easy .. 3 Expert
        }

        // ------------------------------------------------------------ .sng --
        // Un .sng lleva la cancion entera dentro. Se saca el chart, se le hace
        // exactamente lo mismo que a uno suelto —el codigo de abajo no se
        // entera— y se vuelve a meter.
        //
        // LA COPIA DE SEGURIDAD ES SOLO DEL CHART, no del contenedor. Duplicar
        // un .sng son diez o doscientos megas de audio por cancion para guardar
        // cuarenta kilobytes de notas. Con el chart original basta: restaurar es
        // volver a meterlo.
        private static void TrabajoSng(string ruta)
        {
            ArchivoSng s = ArchivoSng.Leer(ruta);
            bool esMidi;
            ArchivoSng.Entrada chart = s.BuscarChart(out esMidi);
            if (chart == null)
            {
                Aviso.Mostrar("Generate Missing Difficulties",
                    "This .sng has no chart inside it.");
                return;
            }

            byte[] original = s.LeerArchivo(chart);
            string copia = RutaCopia(ruta);
            if (!File.Exists(copia))
            {
                File.WriteAllBytes(copia, original);
                MelonLogger.Msg("[Generar] copia del chart del .sng creada");
            }

            string temporal = Path.Combine(Path.GetTempPath(),
                "coolmod_" + Guid.NewGuid().ToString("N") + Path.GetExtension(chart.nombre));
            try
            {
                File.WriteAllBytes(temporal, original);
                List<Trabajito> hechos;
                if (esMidi)
                {
                    HacerMidi(temporal, out hechos);
                }
                else
                {
                    HacerChart(temporal, out hechos);
                }
                if (hechos.Count == 0)
                {
                    return;      // el aviso ya lo puso quien corresponda
                }
                mensaje = "Repacking .sng...";
                if (!s.Escribir(ruta, chart.nombre, File.ReadAllBytes(temporal)))
                {
                    Anotar(ruta);
                    MelonLogger.Msg("[Generar] .sng en espera: el juego lo tiene abierto");
                    Aviso.Mostrar("Generate Missing Difficulties",
                        hechos.Count.ToString() + " difficulty level(s) generated.\n\n"
                        + "The game has this .sng open, so it will be swapped in\n"
                        + "next time you start Clone Hero.");
                    return;
                }
                MelonLogger.Msg("[Generar] .sng reescrito");
            }
            finally
            {
                try { File.Delete(temporal); } catch (Exception) { }
            }
        }

        // --------------------------------------------------------- .chart --
        private static void HacerChart(string ruta, out List<Trabajito> hechos)
        {
            hechos = new List<Trabajito>();
            ArchivoChart a = ArchivoChart.Leer(ruta);

            List<string> instrumentos = new List<string>();
            for (int i = 0; i < Dificultad.InstrumentosChart.Length; i++)
            {
                string inst = Dificultad.InstrumentosChart[i];
                if (inst == "Drums" || inst == "GHLGuitar" || inst == "GHLBass")
                {
                    continue;      // otra rejilla de notas; no lo cubre esto
                }
                for (int d = 0; d < 4; d++)
                {
                    ArchivoChart.Seccion s = a.Buscar(Dificultad.NombreDificultad(d) + inst);
                    if (s != null && ArchivoChart.Notas(s).Count > 0)
                    {
                        instrumentos.Add(inst);
                        break;
                    }
                }
            }

            List<Trabajito> pendientes = new List<Trabajito>();
            for (int i = 0; i < instrumentos.Count; i++)
            {
                int alta = -1;
                for (int d = 3; d >= 0; d--)
                {
                    ArchivoChart.Seccion s = a.Buscar(
                        Dificultad.NombreDificultad(d) + instrumentos[i]);
                    if (s != null && ArchivoChart.Notas(s).Count > 0)
                    {
                        alta = d;
                        break;
                    }
                }
                for (int d = alta - 1; d >= 0; d--)
                {
                    ArchivoChart.Seccion s = a.Buscar(
                        Dificultad.NombreDificultad(d) + instrumentos[i]);
                    if (s == null || ArchivoChart.Notas(s).Count == 0)
                    {
                        Trabajito t = new Trabajito();
                        t.instrumento = instrumentos[i];
                        t.dificultad = d;
                        pendientes.Add(t);
                    }
                }
            }

            if (pendientes.Count == 0)
            {
                Aviso.Mostrar("Generate Missing Difficulties",
                    "This song already has every difficulty level.");
                return;
            }

            total = pendientes.Count;
            for (int i = 0; i < pendientes.Count; i++)
            {
                Trabajito t = pendientes[i];
                paso = i + 1;
                mensaje = Dificultad.NombrePista(t.instrumento) + " - "
                    + Dificultad.NombreDificultad(t.dificultad);

                ArchivoChart.Seccion arriba = a.Buscar(
                    Dificultad.NombreDificultad(t.dificultad + 1) + t.instrumento);
                List<ReduccionChart.Nota> fuente = ArchivoChart.Notas(arriba);
                if (fuente.Count == 0)
                {
                    continue;
                }
                List<ReduccionChart.Nota> nuevas =
                    ReduccionChart.Reducir(fuente, t.dificultad, a.resolucion);
                if (nuevas.Count == 0)
                {
                    continue;
                }
                a.PonerNotas(Dificultad.NombreDificultad(t.dificultad) + t.instrumento,
                             nuevas, t.instrumento);
                hechos.Add(t);
            }

            if (hechos.Count == 0)
            {
                Aviso.Mostrar("Generate Missing Difficulties",
                    "Nothing could be generated.");
                return;
            }
            a.Escribir(ruta);
            Terminado(hechos);
        }

        // ----------------------------------------------------------- .mid --
        private static readonly string[] PistasMidi =
        { "PART GUITAR", "PART BASS", "PART RHYTHM", "PART GUITAR COOP", "PART KEYS" };

        private static void HacerMidi(string ruta, out List<Trabajito> hechos)
        {
            hechos = new List<Trabajito>();
            ArchivoMidi m = ArchivoMidi.Leer(ruta);

            List<Trabajito> pendientes = new List<Trabajito>();
            for (int i = 0; i < PistasMidi.Length; i++)
            {
                ArchivoMidi.Pista p = m.BuscarPista(PistasMidi[i]);
                if (p == null)
                {
                    continue;
                }
                int alta = -1;
                for (int d = 3; d >= 0; d--)
                {
                    if (ArchivoMidi.Notas(p, d).Count > 0)
                    {
                        alta = d;
                        break;
                    }
                }
                if (alta < 0)
                {
                    continue;
                }
                for (int d = alta - 1; d >= 0; d--)
                {
                    if (ArchivoMidi.Notas(p, d).Count == 0)
                    {
                        Trabajito t = new Trabajito();
                        t.instrumento = PistasMidi[i];
                        t.dificultad = d;
                        pendientes.Add(t);
                    }
                }
            }

            if (pendientes.Count == 0)
            {
                Aviso.Mostrar("Generate Missing Difficulties",
                    "This song already has every difficulty level.");
                return;
            }

            total = pendientes.Count;
            for (int i = 0; i < pendientes.Count; i++)
            {
                Trabajito t = pendientes[i];
                paso = i + 1;
                mensaje = t.instrumento + " - "
                    + Dificultad.NombreDificultad(t.dificultad);

                ArchivoMidi.Pista p = m.BuscarPista(t.instrumento);
                List<ReduccionChart.Nota> fuente =
                    ArchivoMidi.Notas(p, t.dificultad + 1);
                if (fuente.Count == 0)
                {
                    continue;
                }
                List<ReduccionChart.Nota> nuevas =
                    ReduccionChart.Reducir(fuente, t.dificultad, m.division);
                if (nuevas.Count == 0)
                {
                    continue;
                }
                ArchivoMidi.PonerNotas(p, t.dificultad, nuevas, m.division);
                hechos.Add(t);
            }

            if (hechos.Count == 0)
            {
                Aviso.Mostrar("Generate Missing Difficulties",
                    "Nothing could be generated.");
                return;
            }
            m.Escribir(ruta);
            Terminado(hechos);
        }

        private static void Terminado(List<Trabajito> hechos)
        {
            string lista = "";
            for (int i = 0; i < hechos.Count && i < 8; i++)
            {
                lista += (i > 0 ? ", " : "")
                    + Dificultad.NombreDificultad(hechos[i].dificultad);
            }
            MelonLogger.Msg("[Generar] " + hechos.Count.ToString() + " dificultad(es)");
            Aviso.Mostrar("Generate Missing Difficulties",
                hechos.Count.ToString() + " difficulty level(s) generated.\n\n"
                + "Scan Songs to play them.\n"
                + "The original chart was backed up.");
        }

        // ------------------------------------------------------ en espera --
        // Un .sng que el juego tenia abierto no se puede reemplazar sobre la
        // marcha, asi que el nuevo se queda al lado con el sufijo .coolmod.new
        // y se coloca al arrancar, antes de que el juego abra nada. Se apunta
        // aqui porque al arrancar no se sabe donde mirar: la biblioteca puede
        // estar repartida por varias carpetas.
        private static string Registro()
        {
            return Path.Combine(MelonLoader.Utils.MelonEnvironment.ModsDirectory,
                                "coolmod_sng_pendientes.txt");
        }

        private static void Anotar(string sng)
        {
            try
            {
                File.AppendAllText(Registro(), sng + Environment.NewLine);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Generar] no se pudo anotar: " + ex.Message);
            }
        }

        // Al arrancar. Lo que no se pueda colocar todavia se deja apuntado.
        public static void AplicarPendientes()
        {
            string reg = Registro();
            try
            {
                if (!File.Exists(reg))
                {
                    return;
                }
                string[] lineas = File.ReadAllLines(reg);
                List<string> quedan = new List<string>();
                int puestos = 0;
                for (int i = 0; i < lineas.Length; i++)
                {
                    string sng = lineas[i].Trim();
                    if (sng.Length == 0)
                    {
                        continue;
                    }
                    string nuevo = sng + ArchivoSng.Pendiente;
                    if (!File.Exists(nuevo))
                    {
                        continue;      // ya no hace falta
                    }
                    try
                    {
                        if (ArchivoSng.Intercambiar(nuevo, sng))
                        {
                            puestos++;
                        }
                        else
                        {
                            quedan.Add(sng);
                        }
                    }
                    catch (Exception)
                    {
                        quedan.Add(sng);
                    }
                }
                if (quedan.Count > 0)
                {
                    File.WriteAllLines(reg, quedan.ToArray());
                }
                else
                {
                    File.Delete(reg);
                }
                if (puestos > 0)
                {
                    MelonLogger.Msg("[Generar] " + puestos.ToString()
                        + " .sng colocado(s) al arrancar");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Generar] pendientes: " + ex.Message);
            }
        }

        // ------------------------------------------------------ restaurar --
        public static void Restaurar(string chart)
        {
            try
            {
                if (string.IsNullOrEmpty(chart))
                {
                    return;
                }
                string copia = RutaCopia(chart);
                if (!File.Exists(copia))
                {
                    Aviso.Mostrar("Restore Song Chart",
                        "There is no backup for this song.\n"
                        + "Its difficulty levels have never been generated.");
                    return;
                }
                if (ArchivoSng.EsSng(chart))
                {
                    ArchivoSng s = ArchivoSng.Leer(chart);
                    bool esMidi;
                    ArchivoSng.Entrada dentro = s.BuscarChart(out esMidi);
                    if (dentro == null)
                    {
                        Aviso.Mostrar("Restore Song Chart",
                            "This .sng has no chart inside it.");
                        return;
                    }
                    if (!s.Escribir(chart, dentro.nombre, File.ReadAllBytes(copia)))
                    {
                        Anotar(chart);
                        Aviso.Mostrar("Restore Song Chart",
                            "The game has this .sng open, so the original will\n"
                            + "be put back next time you start Clone Hero.");
                        return;
                    }
                }
                else
                {
                    File.Copy(copia, chart, true);
                }
                MelonLogger.Msg("[Generar] restaurado desde la copia");
                Aviso.Mostrar("Restore Song Chart",
                    "The original chart is back.\n\n"
                    + "Scan Songs to pick up the change.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Generar] restaurar: " + ex);
                Aviso.Mostrar("Restore Song Chart", "Could not restore: " + ex.Message);
            }
        }
    }
}
