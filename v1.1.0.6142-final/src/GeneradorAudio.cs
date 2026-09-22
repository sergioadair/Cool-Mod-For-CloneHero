using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using MelonLoader;

namespace CloneHeroMod
{
    // "Generate Charts From Audio": recorre las carpetas de canciones, busca
    // las que tienen audio pero NINGUN chart, y les hace uno.
    //
    // Solo se genera Experto. Las demas dificultades salen despues del motor
    // que ya existe —el mismo de "Generate Song Difficulties"—, que lleva
    // meses probado y sabe reducir de Experto hacia abajo.
    //
    // NUNCA TOCA UNA CARPETA QUE YA TENGA CHART. Ni .chart, ni .mid, ni .sng.
    // Esta funcion es para audio suelto que todavia no es una cancion
    // jugable; no es un reemplazo de nada.
    //
    // LAS CARPETAS DE CANCIONES salen de settings.ini, seccion [directories],
    // que es donde el juego guarda las que el usuario anadio. No se deducen de
    // la biblioteca cargada: estas carpetas, al no tener chart, el juego ni
    // las ha mirado.
    //
    // TODO EL TEXTO EN INGLES: se ve en pantalla.
    public static class GeneradorAudio
    {
        public const string Nombre = "Generate Charts From Audio";

        public static readonly string[] Extensiones = { ".ogg", ".opus", ".mp3", ".wav", ".flac" };

        // El juego pone de fondo el video de la carpeta, pero solo si se llama
        // "video". En una biblioteca de 2018 archivos de video, 2017 se llaman
        // asi; los que no —videoplayback.mp4, videwo.mp4— simplemente no se
        // ven. Si el usuario deja el video junto al audio, se renombra.
        public static readonly string[] ExtensionesVideo =
            { ".mp4", ".webm", ".avi", ".mov", ".mpeg", ".mpg", ".ogv", ".mkv" };

        // Nombres que el juego reconoce como pista principal. Si el audio se
        // llama de otra forma —"mi cancion.mp3"— hay que renombrarlo, o el
        // juego encuentra el chart y no encuentra con que sonar.
        public static readonly string[] NombresPista =
        {
            "song", "guitar", "rhythm", "bass", "keys", "drums",
            "drums_1", "drums_2", "drums_3", "drums_4", "vocals", "crowd", "preview"
        };

        private static bool corriendo;
        private static int total, hechas, generadas, fallidas, saltadas;
        private static string actual = "";

        public static bool Corriendo { get { return corriendo; } }
        public static int Total { get { return total; } }
        public static int Hechas { get { return hechas; } }
        public static int Generadas { get { return generadas; } }
        public static int Fallidas { get { return fallidas; } }
        public static int Saltadas { get { return saltadas; } }
        public static string Actual { get { return actual; } }

        // ----------------------------------------------------------- lanzar --
        public static void Lanzar()
        {
            if (corriendo)
            {
                return;
            }
            corriendo = true;
            total = hechas = generadas = fallidas = saltadas = 0;
            actual = "looking for audio...";

            // Todo lo de aqui es archivo y codigo nativo: nada de Il2Cpp, asi
            // que puede irse a un hilo y no congelar el juego.
            Thread hilo = new Thread(Trabajar);
            hilo.IsBackground = true;
            hilo.Start();
        }

        private static void Trabajar()
        {
            try
            {
                List<string> carpetas = Candidatas();
                total = carpetas.Count;
                if (total == 0)
                {
                    Aviso.Mostrar("Generate Charts From Audio",
                        "No audio folders without a chart were found.");
                    return;
                }
                for (int i = 0; i < carpetas.Count; i++)
                {
                    actual = Path.GetFileName(carpetas[i]);
                    try
                    {
                        if (Una(carpetas[i]))
                        {
                            generadas++;
                        }
                        else
                        {
                            saltadas++;
                        }
                    }
                    catch (Exception ex)
                    {
                        fallidas++;
                        MelonLogger.Warning("[Audio] " + actual + ": " + ex.Message);
                    }
                    hechas++;
                }
                Aviso.Mostrar("Generate Charts From Audio",
                    generadas.ToString() + " chart(s) created. Rescan your songs to play them.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Audio] " + ex);
                Aviso.Mostrar("Generate Charts From Audio", "Failed: " + ex.Message);
            }
            finally
            {
                corriendo = false;
            }
        }

        // ------------------------------------------------------- candidatas --
        public static List<string> Candidatas()
        {
            List<string> salida = new List<string>();
            foreach (string raiz in CarpetasDeCanciones())
            {
                Recorrer(raiz, salida, 0);
            }
            return salida;
        }

        private static void Recorrer(string carpeta, List<string> salida, int hondura)
        {
            if (hondura > 8)
            {
                return;      // biblioteca rara; no merece la pena seguir
            }
            try
            {
                if (TieneChart(carpeta))
                {
                    return;      // ya es una cancion: ni se entra
                }
                // Vale tanto un archivo de audio como un video con audio
                // dentro. Esto miraba SOLO audio, asi que una carpeta con un
                // mp4 y nada mas no entraba en la lista y nunca llegaba a la
                // parte que sabe sacarle la pista: el barrido contestaba que
                // no habia nada que hacer.
                if (AudioDe(carpeta) != null || VideoDe(carpeta) != null)
                {
                    salida.Add(carpeta);
                    return;
                }
                foreach (string sub in Directory.GetDirectories(carpeta))
                {
                    Recorrer(sub, salida, hondura + 1);
                }
            }
            catch (Exception)
            {
            }
        }

        public static bool TieneChart(string carpeta)
        {
            try
            {
                if (File.Exists(Path.Combine(carpeta, "notes.chart"))
                    || File.Exists(Path.Combine(carpeta, "notes.mid"))
                    || File.Exists(Path.Combine(carpeta, "notes.midi")))
                {
                    return true;
                }
                return Directory.GetFiles(carpeta, "*.sng").Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // El audio de la carpeta. Si hay varios se prefiere el que ya se llame
        // como una pista del juego, y si no, el mas grande: en una carpeta
        // suelta suele haber una cancion y algun recorte de muestra.
        public static string AudioDe(string carpeta)
        {
            try
            {
                string mejor = null;
                long mayor = -1;
                foreach (string f in Directory.GetFiles(carpeta))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(Extensiones, ext) < 0)
                    {
                        continue;
                    }
                    string nombre = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    long peso = new FileInfo(f).Length;
                    if (Array.IndexOf(NombresPista, nombre) >= 0)
                    {
                        peso += 1L << 40;      // los nombres de pista van delante
                    }
                    if (peso > mayor)
                    {
                        mayor = peso;
                        mejor = f;
                    }
                }
                return mejor;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IEnumerable<string> CarpetasDeCanciones()
        {
            List<string> salida = new List<string>();
            try
            {
                string ini = RutasJuego.RutaSettings();
                if (string.IsNullOrEmpty(ini) || !File.Exists(ini))
                {
                    return salida;
                }
                bool dentro = false;
                foreach (string linea in File.ReadAllLines(ini))
                {
                    string l = linea.Trim();
                    if (l.StartsWith("[", StringComparison.Ordinal))
                    {
                        dentro = l.Equals("[directories]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!dentro)
                    {
                        continue;
                    }
                    int ig = l.IndexOf('=');
                    if (ig <= 0)
                    {
                        continue;
                    }
                    string valor = l.Substring(ig + 1).Trim();
                    if (valor.Length > 0 && Directory.Exists(valor))
                    {
                        salida.Add(valor);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Audio] carpetas: " + ex.Message);
            }
            return salida;
        }

        // ------------------------------------------------------------- una ---
        public static bool Una(string carpeta)
        {
            if (TieneChart(carpeta))
            {
                return false;
            }
            string audio = AudioDe(carpeta);
            if (audio == null)
            {
                // Sin archivo de audio, pero puede haber un video que lo
                // lleve dentro: alguien que deja el mp4 de un clip y ya.
                audio = DesdeVideo(carpeta);
                if (audio == null)
                {
                    return false;
                }
            }

            ChartDesdeAudio.Resultado r = ChartDesdeAudio.Generar(audio);
            if (r.notas.Count < 8)
            {
                MelonLogger.Warning("[Audio] " + Path.GetFileName(carpeta) + ": "
                    + (r.aviso.Length > 0 ? r.aviso : "muy pocas notas"));
                return false;
            }

            string nombre, artista;
            Partir(Path.GetFileName(carpeta), out artista, out nombre);

            audio = Renombrar(audio);
            RenombrarVideo(carpeta);
            string chart = Path.Combine(carpeta, "notes.chart");
            EscribirChart(carpeta, r, nombre, artista);
            EscribirIni(carpeta, r, nombre, artista);
            int dificultades = Puntuar(carpeta, chart);

            MelonLogger.Msg("[Audio] " + Path.GetFileName(carpeta) + ": "
                + r.notas.Count.ToString() + " notas, " + r.bpm.ToString("0") + " bpm"
                + (r.cuadriculado ? ", cuadriculado" : ", sin cuadricular")
                + ", " + dificultades.ToString() + " valores de dificultad");
            return true;
        }

        // El valor de Difficulty de cada instrumento, con el mismo calculo
        // que "Calculate Difficulty". Se hace ya, aprovechando que el chart
        // acaba de escribirse: si no, la cancion aparece sin puntuacion hasta
        // que el usuario lance el calculo a mano.
        private static int Puntuar(string carpeta, string chart)
        {
            try
            {
                string ini = Path.Combine(carpeta, "song.ini");
                if (!File.Exists(ini) || !File.Exists(chart))
                {
                    return 0;
                }
                Dificultad.Perfil perfil;
                if (!Dificultad.Calcular(chart, false, out perfil) || perfil == null)
                {
                    return 0;
                }
                Dificultad.EscribirIni(ini, perfil);
                return 1;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Audio] dificultad: " + ex.Message);
                return 0;
            }
        }

        // "Artista - Cancion" es la forma estandar de nombrar carpetas en esta
        // comunidad. Si no lleva guion, la carpeta entera es el titulo.
        public static void Partir(string carpeta, out string artista, out string titulo)
        {
            artista = "Unknown Artist";
            titulo = carpeta;
            if (string.IsNullOrEmpty(carpeta))
            {
                return;
            }
            int guion = carpeta.IndexOf(" - ", StringComparison.Ordinal);
            if (guion > 0 && guion + 3 < carpeta.Length)
            {
                artista = carpeta.Substring(0, guion).Trim();
                titulo = carpeta.Substring(guion + 3).Trim();
            }
        }

        // El juego solo suena si la pista se llama como el espera. Se renombra
        // conservando la extension; si ya se llamaba bien, no se toca.
        private static string Renombrar(string audio)
        {
            try
            {
                string nombre = Path.GetFileNameWithoutExtension(audio).ToLowerInvariant();
                if (Array.IndexOf(NombresPista, nombre) >= 0)
                {
                    return audio;
                }
                string destino = Path.Combine(Path.GetDirectoryName(audio),
                    "song" + Path.GetExtension(audio));
                if (File.Exists(destino))
                {
                    return audio;
                }
                File.Move(audio, destino);
                return destino;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Audio] renombrar: " + ex.Message);
                return audio;
            }
        }

        // Saca el audio del video a un song.wav al lado. El video se queda
        // donde esta —lo quiere el juego como fondo— y ademas se le pone el
        // nombre que el juego espera.
        // El video de la carpeta, el mas grande si hay varios.
        public static string VideoDe(string carpeta)
        {
            try
            {
                string video = null;
                long mayor = -1;
                foreach (string f in Directory.GetFiles(carpeta))
                {
                    if (!AudioVideo.EsVideo(f))
                    {
                        continue;
                    }
                    long peso = new FileInfo(f).Length;
                    if (peso > mayor)
                    {
                        mayor = peso;
                        video = f;
                    }
                }
                return video;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string DesdeVideo(string carpeta)
        {
            try
            {
                string video = VideoDe(carpeta);
                if (video == null)
                {
                    return null;
                }
                string wav = Path.Combine(carpeta, "song.wav");
                if (!AudioVideo.ExtraerWav(video, wav))
                {
                    try { if (File.Exists(wav)) File.Delete(wav); } catch (Exception) { }
                    return null;
                }
                return wav;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Audio] desde video: " + ex.Message);
                return null;
            }
        }

        // Si hay un video con otro nombre, se le pone el que el juego espera.
        // Si ya hay uno llamado "video", no se toca nada: el suyo manda.
        private static void RenombrarVideo(string carpeta)
        {
            try
            {
                string mejor = null;
                long mayor = -1;
                foreach (string f in Directory.GetFiles(carpeta))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(ExtensionesVideo, ext) < 0)
                    {
                        continue;
                    }
                    if (Path.GetFileNameWithoutExtension(f)
                            .Equals("video", StringComparison.OrdinalIgnoreCase))
                    {
                        return;      // ya esta puesto
                    }
                    long peso = new FileInfo(f).Length;
                    if (peso > mayor)
                    {
                        mayor = peso;
                        mejor = f;
                    }
                }
                if (mejor == null)
                {
                    return;
                }
                string destino = Path.Combine(carpeta, "video" + Path.GetExtension(mejor));
                if (File.Exists(destino))
                {
                    return;
                }
                File.Move(mejor, destino);
                MelonLogger.Msg("[Audio] video: " + Path.GetFileName(mejor)
                    + " -> " + Path.GetFileName(destino));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Audio] video: " + ex.Message);
            }
        }

        private static void EscribirChart(string carpeta, ChartDesdeAudio.Resultado r,
                                          string titulo, string artista)
        {
            ArchivoChart c = new ArchivoChart();
            c.resolucion = ChartDesdeAudio.Resolucion;

            ArchivoChart.Seccion song = new ArchivoChart.Seccion();
            song.nombre = "Song";
            song.lineas.Add("  Name = \"" + Limpiar(titulo) + "\"");
            song.lineas.Add("  Artist = \"" + Limpiar(artista) + "\"");
            song.lineas.Add("  Charter = \"Cool Mod\"");
            song.lineas.Add("  Offset = 0");
            song.lineas.Add("  Resolution = " + ChartDesdeAudio.Resolucion.ToString());
            song.lineas.Add("  Player2 = bass");
            song.lineas.Add("  Difficulty = 0");
            song.lineas.Add("  PreviewStart = 0");
            song.lineas.Add("  PreviewEnd = 0");
            song.lineas.Add("  Genre = \"Generated\"");
            song.lineas.Add("  MediaType = \"Audio\"");
            c.secciones.Add(song);

            ArchivoChart.Seccion sync = new ArchivoChart.Seccion();
            sync.nombre = "SyncTrack";
            sync.lineas.Add("  0 = TS 4");
            // el .chart guarda el tempo multiplicado por mil
            long bpmMil = (long)Math.Round(r.bpm * 1000.0);
            if (bpmMil < 1000) bpmMil = 120000;
            sync.lineas.Add("  0 = B " + bpmMil.ToString(CultureInfo.InvariantCulture));
            c.secciones.Add(sync);

            ArchivoChart.Seccion eventos = new ArchivoChart.Seccion();
            eventos.nombre = "Events";
            c.secciones.Add(eventos);

            // Las cuatro dificultades de una vez. Del audio solo sale
            // Experto; las de abajo las saca el mismo reductor que usa
            // "Generate Song Difficulties", asi que un chart generado desde
            // audio se reduce con el mismo criterio que uno humano.
            //
            // Se hace aqui y no llamando despues a GeneradorCharts porque eso
            // reabriria el archivo recien escrito y dejaria una copia de
            // seguridad de algo que acabamos de crear nosotros.
            c.PonerNotas("ExpertSingle", r.notas, r.fases,
                         new List<string[]>(), "Single");
            string[] nombres = { "EasySingle", "MediumSingle", "HardSingle" };
            for (int d = 2; d >= 0; d--)
            {
                List<ReduccionChart.Nota> menos =
                    ReduccionChart.Reducir(r.notas, d, ChartDesdeAudio.Resolucion);
                if (menos.Count == 0)
                {
                    continue;
                }
                c.PonerNotas(nombres[d], menos,
                             ReduccionChart.AjustarFases(r.fases, menos),
                             new List<string[]>(), "Single");
            }
            c.Escribir(Path.Combine(carpeta, "notes.chart"));
        }

        private static void EscribirIni(string carpeta, ChartDesdeAudio.Resultado r,
                                        string titulo, string artista)
        {
            string ruta = Path.Combine(carpeta, "song.ini");
            if (File.Exists(ruta))
            {
                return;      // si el usuario ya puso uno, es suyo
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[song]");
            sb.AppendLine("name = " + titulo);
            sb.AppendLine("artist = " + artista);
            sb.AppendLine("charter = Cool Mod");
            sb.AppendLine("album = Generated from audio");
            sb.AppendLine("genre = Generated");
            sb.AppendLine("diff_guitar = -1");
            sb.AppendLine("song_length = "
                + ((int)((r.duracion + ChartDesdeAudio.Entradilla) * 1000)).ToString());
            // Los dos segundos de entradilla, para el audio y para el video.
            //
            // delay solo mueve el audio: la clase que lleva el video no
            // consulta ni una vez el reloj del audio, lo coloca al empezar y
            // lo deja correr. Sin video_start_time el video iria dos segundos
            // por delante de su propio sonido, que en una cancion sacada de un
            // video se nota en seguida.
            //
            // Los dos campos son de uso corriente: en la biblioteca de prueba
            // hay 1618 canciones con video_start_time, nueve de ellas
            // justamente en -2000. Ver ChartDesdeAudio.Entradilla para el signo.
            int entradillaMs = (int)(-ChartDesdeAudio.Entradilla * 1000);
            sb.AppendLine("delay = " + entradillaMs.ToString());
            sb.AppendLine("video_start_time = " + entradillaMs.ToString());
            File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false));
        }

        private static string Limpiar(string t)
        {
            return string.IsNullOrEmpty(t) ? "" : t.Replace("\"", "'");
        }
    }
}
