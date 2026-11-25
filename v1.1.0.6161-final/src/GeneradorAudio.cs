using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using MelonLoader;

namespace CloneHeroMod
{
    // "Generate Charts From Audio/Video": recorre las carpetas de canciones, busca
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
    // LAS CARPETAS DE CANCIONES son las mismas que escanea el juego: su lista
    // GlobalVariables.uniqueSongPaths, que es su carpeta predeterminada
    // (<datos>\Songs, que NO aparece en settings.ini) mas las de la seccion
    // [directories]. Leer solo settings.ini se saltaba la predeterminada
    // (issue del 26-10). Si la lista no se puede leer, se arma igual a mano.
    // No se deducen de la biblioteca cargada: estas carpetas, al no tener
    // chart, el juego ni las ha mirado.
    //
    // TODO EL TEXTO EN INGLES: se ve en pantalla.
    public static class GeneradorAudio
    {
        public const string Nombre = "Generate Charts From Audio/Video";

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

            // La lista de carpetas es un objeto del juego: se lee aqui, en el
            // hilo principal. Lo demas es archivo y codigo nativo, nada de
            // Il2Cpp, asi que puede irse a un hilo y no congelar el juego.
            raices = CarpetasDeCanciones();
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
                    Aviso.Mostrar(Nombre,
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
                if (generadas > 0)
                {
                    Reescaneo.Pedir(Nombre);
                }
                Aviso.Mostrar(Nombre,
                    generadas.ToString() + " chart(s) created."
                    + (generadas > 0 ? " Scanning your songs now." : ""));
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Audio] " + ex);
                Aviso.Mostrar(Nombre, "Failed: " + ex.Message);
            }
            finally
            {
                corriendo = false;
            }
        }

        // ------------------------------------------------------- candidatas --
        private static List<string> raices = new List<string>();

        public static List<string> Candidatas()
        {
            List<string> salida = new List<string>();
            foreach (string raiz in raices)
            {
                Recorrer(raiz, salida, 0);
            }
            return salida;
        }

        // Artista de las canciones sueltas cuyo nombre no dice de quien son.
        public const string ArtistaSinNombre = "Cool Mod";

        // Un audio o video que no tiene carpeta propia se generaba en la carpeta
        // donde estaba: la cancion se llamaba como esa carpeta ("Songs",
        // "Caifanes", artista desconocido), solo se usaba el archivo mas
        // grande, y el notes.chart que quedaba ahi convertia la carpeta entera
        // en una cancion: el juego y el mod dejaban de ver sus subcarpetas.
        //
        // Ahora, en una carpeta CONTENEDORA (ver EsContenedora), cada suelto
        // pasa a su propia carpeta, nombrada como el archivo: "Artista -
        // Cancion" si el nombre ya tiene esa forma, o "Cool Mod - <nombre>" si
        // no. El artista y el titulo salen despues de la carpeta (Partir), como
        // en cualquier otra. Un audio y un video con el mismo nombre van a la
        // misma carpeta.
        private static void AcomodarSueltos(string raiz)
        {
            string[] archivos;
            try
            {
                archivos = Directory.GetFiles(raiz);
            }
            catch (Exception)
            {
                return;
            }
            Dictionary<string, string> destinos =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in archivos)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (Array.IndexOf(Extensiones, ext) < 0 && Array.IndexOf(ExtensionesVideo, ext) < 0)
                {
                    continue;
                }
                string baseNombre = Path.GetFileNameWithoutExtension(f).Trim().TrimEnd('.');
                if (baseNombre.Length == 0
                    || baseNombre.Equals("video", StringComparison.OrdinalIgnoreCase)
                    || Array.IndexOf(NombresPista, baseNombre.ToLowerInvariant()) >= 0)
                {
                    continue;      // "song.ogg", "video.mp4": no dicen de que cancion son
                }
                try
                {
                    string carpeta;
                    if (!destinos.TryGetValue(baseNombre, out carpeta))
                    {
                        string artista, titulo;
                        Partir(baseNombre, out artista, out titulo);
                        string nombre = artista == "Unknown Artist"
                            ? ArtistaSinNombre + " - " + baseNombre
                            : baseNombre;
                        carpeta = CarpetaLibre(Path.Combine(raiz, nombre));
                        Directory.CreateDirectory(carpeta);
                        destinos[baseNombre] = carpeta;
                    }
                    string destino = Path.Combine(carpeta, Path.GetFileName(f));
                    File.Move(f, destino);
                    MelonLogger.Msg("[Audio] suelto: " + Path.GetFileName(f) + " -> "
                        + Path.GetFileName(carpeta) + "\\");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning("[Audio] suelto " + Path.GetFileName(f) + ": " + ex.Message);
                }
            }
        }

        // La ruta tal cual si esta libre; si ya existe, "<ruta> (2)", (3)...
        private static string CarpetaLibre(string ruta)
        {
            if (!Directory.Exists(ruta) && !File.Exists(ruta))
            {
                return ruta;
            }
            for (int n = 2; ; n++)
            {
                string otra = ruta + " (" + n.ToString() + ")";
                if (!Directory.Exists(otra) && !File.Exists(otra))
                {
                    return otra;
                }
            }
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
                if (EsContenedora(carpeta, hondura == 0))
                {
                    // Sus sueltos pasan a carpeta propia, y esas carpetas se
                    // recorren abajo como cualquier otra subcarpeta.
                    AcomodarSueltos(carpeta);
                }
                // Vale tanto un archivo de audio como un video con audio
                // dentro. Esto miraba SOLO audio, asi que una carpeta con un
                // mp4 y nada mas no entraba en la lista y nunca llegaba a la
                // parte que sabe sacarle la pista: el barrido contestaba que
                // no habia nada que hacer.
                else if (AudioDe(carpeta) != null || VideoDe(carpeta) != null)
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

        // Una carpeta que AGRUPA canciones, no la carpeta de una cancion:
        //   1. una raiz de canciones;
        //   2. una con subcarpetas (las de una cancion no tienen: "Caifanes",
        //      o una de letra como "N");
        //   3. una con dos o mas audios o videos de nombre distinto, sin contar
        //      las pistas que el juego reconoce (song, guitar...) ni "video":
        //      dos clips sueltos son dos canciones.
        // Una sin subcarpetas y con un solo audio o video (o varios con el
        // mismo nombre) sigue siendo la carpeta de esa cancion.
        private static bool EsContenedora(string carpeta, bool esRaiz)
        {
            if (esRaiz)
            {
                return true;
            }
            try
            {
                if (Directory.GetDirectories(carpeta).Length > 0)
                {
                    return true;
                }
                HashSet<string> nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string f in Directory.GetFiles(carpeta))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(Extensiones, ext) < 0 && Array.IndexOf(ExtensionesVideo, ext) < 0)
                    {
                        continue;
                    }
                    string nombre = Path.GetFileNameWithoutExtension(f).Trim();
                    if (nombre.Equals("video", StringComparison.OrdinalIgnoreCase)
                        || Array.IndexOf(NombresPista, nombre.ToLowerInvariant()) >= 0)
                    {
                        continue;
                    }
                    nombres.Add(nombre);
                }
                return nombres.Count >= 2;
            }
            catch (Exception)
            {
                return false;
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

        // Las que escanea el juego, sin repetir y solo las que existen.
        private static List<string> CarpetasDeCanciones()
        {
            List<string> salida = new List<string>();
            try
            {
                Il2Cpp.GlobalVariables g = Il2Cpp.GlobalVariables.instance;
                var lista = g == null ? null : g.uniqueSongPaths;
                if (lista != null && lista.Count > 0)
                {
                    for (int i = 0; i < lista.Count; i++)
                    {
                        Anadir(salida, lista[i]);
                    }
                    MelonLogger.Msg("[Audio] carpetas del juego: " + string.Join(" | ", salida));
                    return salida;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Audio] lista del juego: " + ex.Message);
            }
            // Plan B: la predeterminada y las de settings.ini.
            Anadir(salida, string.IsNullOrEmpty(RutasJuego.CarpetaDatos)
                ? null : Path.Combine(RutasJuego.CarpetaDatos, "Songs"));
            foreach (string c in CarpetasDelIni())
            {
                Anadir(salida, c);
            }
            MelonLogger.Msg("[Audio] carpetas (settings.ini): " + string.Join(" | ", salida));
            return salida;
        }

        private static void Anadir(List<string> salida, string ruta)
        {
            if (string.IsNullOrWhiteSpace(ruta))
            {
                return;
            }
            try
            {
                string completa = Path.GetFullPath(ruta.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!Directory.Exists(completa))
                {
                    return;
                }
                for (int i = 0; i < salida.Count; i++)
                {
                    if (string.Equals(salida[i], completa, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
                salida.Add(completa);
            }
            catch (Exception)
            {
            }
        }

        private static IEnumerable<string> CarpetasDelIni()
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
            // Los nombres originales, antes de que se renombren a song/video:
            // pueden decir de quien es la cancion.
            string nombreAudio = audio == null ? null : Path.GetFileNameWithoutExtension(audio);
            string videoOriginal = VideoDe(carpeta);
            string nombreVideo = videoOriginal == null ? null : Path.GetFileNameWithoutExtension(videoOriginal);
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
            Titular(Path.GetFileName(carpeta), nombreAudio, nombreVideo, out artista, out nombre);

            audio = Renombrar(audio);
            RenombrarVideo(carpeta);
            string chart = Path.Combine(carpeta, "notes.chart");
            EscribirChart(carpeta, r, nombre, artista);
            EscribirIni(carpeta, r, nombre, artista);
            int dificultades = Puntuar(carpeta, chart);

            MelonLogger.Msg("[Audio] " + Path.GetFileName(carpeta) + ": "
                + r.notas.Count.ToString() + " notas (" + r.inventadas.ToString()
                + " sin ataque debajo), " + r.bpm.ToString("0") + " bpm"
                + ", trastes " + Reparto(r.notas) + " por "
                + (r.conTono >= ChartDesdeAudio.MinimoConTono ? "tono" : "brillo")
                + " (tono en " + (r.conTono * 100).ToString("0") + "%)"
                + (r.ternario ? ", rejilla ternaria" : ", rejilla binaria")
                + ", notas movidas " + r.movidaMedia.ToString("0") + " ms (p90 "
                + r.movidaP90.ToString("0") + ")"
                + ", " + r.compasesCopiados.ToString() + "/" + r.compases.ToString()
                + " compases repetidos"
                + ", " + dificultades.ToString() + " valores de dificultad");
            return true;
        }

        // Que parte de las notas cae en cada traste, de verde a naranja, en %.
        private static string Reparto(List<ReduccionChart.Nota> notas)
        {
            int[] c = new int[5];
            int total = 0;
            for (int i = 0; i < notas.Count; i++)
            {
                for (int t = 0; t < 5; t++)
                {
                    if ((notas[i].trastes & (1 << t)) != 0) { c[t]++; total++; }
                }
            }
            string[] partes = new string[5];
            for (int t = 0; t < 5; t++)
            {
                partes[t] = total > 0 ? (100 * c[t] / total).ToString() : "0";
            }
            return string.Join("/", partes);
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

        // Artista y titulo de una cancion generada, por orden:
        //   1. la carpeta, si se llama "Artista - Cancion";
        //   2. si no, el archivo (primero el audio, luego el video), si se
        //      llama asi;
        //   3. si ninguno, artista "Cool Mod" y la carpeta como titulo.
        private static void Titular(string carpeta, string archivoAudio, string archivoVideo,
                                    out string artista, out string titulo)
        {
            Partir(carpeta, out artista, out titulo);
            if (artista != "Unknown Artist")
            {
                return;
            }
            foreach (string archivo in new[] { archivoAudio, archivoVideo })
            {
                string a, t;
                Partir(archivo, out a, out t);
                if (!string.IsNullOrEmpty(archivo) && a != "Unknown Artist")
                {
                    artista = a;
                    titulo = t;
                    return;
                }
            }
            artista = ArtistaSinNombre;
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
            // El mapa sigue los pulsos de la cancion: un cambio de tempo por
            // cada pulso que se mueve. Si por lo que sea viniera vacio, un
            // tempo fijo como antes.
            if (r.tempos != null && r.tempos.Count > 0)
            {
                for (int i = 0; i < r.tempos.Count; i++)
                {
                    sync.lineas.Add("  " + r.tempos[i].Key.ToString(CultureInfo.InvariantCulture)
                        + " = B " + r.tempos[i].Value.ToString(CultureInfo.InvariantCulture));
                }
            }
            else
            {
                long bpmMil = (long)Math.Round(r.bpm * 1000.0);
                if (bpmMil < 1000) bpmMil = 120000;
                sync.lineas.Add("  0 = B " + bpmMil.ToString(CultureInfo.InvariantCulture));
            }
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
