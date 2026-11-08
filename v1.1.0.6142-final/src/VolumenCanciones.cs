using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MelonLoader;
using MelonLoader.Utils;

namespace CloneHeroMod
{
    // Igualar el volumen de las canciones sin tocar sus archivos.
    //
    // Settings > General > Normalize Songs Volume mide cada cancion y guarda
    // cuanto hay que subirla o bajarla; Restore Songs Volume lo olvida todo.
    // Lo medido va a UserData\CoolMod\song_volume.tsv: ni el audio ni el
    // song.ini se tocan, y borrar ese archivo deja todo como estaba.
    //
    // COMO SUENA UNA CANCION EN EL JUEGO (BassAudioManager). Cada pista
    // (guitar, bass, song...) es un stream de solo decodificar que entra en un
    // mezclador de bassmix a 48 kHz estereo; si hay cambio de velocidad, el
    // mezclador va envuelto en un stream de tempo de bass_fx. Ese canal final
    // se crea en m77 y se guarda en linkedChannel: es lo unico que suena, en la
    // vista previa y al jugar. Llega por tres caminos:
    //
    //   jugar                         GameManager -> m106 (carga) -> m77
    //   vista previa sin preview.*    hilo de carga m80 -> m106 -> m77
    //   vista previa con preview.*    m80 -> m77, y mete ese archivo solo
    //
    // Asi que al salir de m77 se le pone al canal la ganancia de la cancion
    // que se esta cargando (la de m106, o la que m80 tiene apuntada en f_80).
    //
    // SE USA BASS_ATTRIB_VOLDSP, no el volumen normal. El juego maneja
    // BASS_ATTRIB_VOL (fundidos, volumen por pista) y el volumen global de
    // streams; VOLDSP es otro mando que multiplica aparte, no se pisan, y
    // ademas deja pasar de 1 para subir las canciones bajas. Existe desde BASS
    // 2.4.15; el juego trae la 2.4.18.
    //
    // COMO SE MIDE. Sonoridad integrada (ITU-R BS.1770: filtro K, bloques de
    // 400 ms, puertas absoluta y relativa) y pico de la SUMA de las pistas,
    // que es lo que suena. Si la cancion trae preview.* propio, ese se mide
    // aparte, porque es lo que suena en la lista.
    //
    // POR FRAGMENTOS, NO ENTERA. Medir la cancion entera era decodificar cada
    // pista de principio a fin, y eso es casi todo el coste: en un i5-3340M,
    // 3781 canciones daban mas de dos horas. Se miden 12 trozos de 1,5 s
    // repartidos por la cancion: en 40 canciones reales la diferencia con la
    // medida entera fue de 0,20 dB de media y 0,55 dB como mucho (un cambio de
    // volumen empieza a notarse hacia 1 dB), y es 11 veces mas rapido. Con
    // menos audio la cosa se estropea: 5 x 2 s ya se desviaba 2,5 dB.
    //
    // Cada pista se decodifica a su frecuencia y se suman aqui, sin el
    // mezclador del juego (bassmix no sabe saltar a un fragmento). Las que van
    // a otra frecuencia se miden en su propio grupo y se suma su energia: dos
    // pistas distintas no estan correladas, y la energia de la suma es la suma
    // de las energias.
    //
    // A QUE NIVEL. A la mediana de la biblioteca: el volumen medio no cambia y
    // solo se corrigen las que se salen. Al subir una cancion, su pico no pasa
    // del pico tipico de la biblioteca. No del maximo digital: casi todas las
    // canciones ya lo pasan al sumar sus pistas (1,04 a 1,65 en las medidas), y
    // con ese tope casi ninguna se habria podido subir. Como pico y mediana se
    // miden igual, por fragmentos, la comparacion es justa. Tope de +-12 dB.
    //
    // EN SEGUNDO PLANO. Aun asi la primera vez son unos 15 minutos para una
    // biblioteca de 3800 canciones, asi que no bloquea nada: los menus siguen
    // funcionando, un indicador pequeno en una esquina dice como va, los
    // hilos van a prioridad baja y se quedan quietos mientras se toca una
    // cancion. Restore Songs Volume a medias la cancela.
    //
    // SE GUARDA SOBRE LA MARCHA, cada pocos segundos: si se cierra el juego no
    // se pierde lo medido, la siguiente vez se salta lo que ya esta al dia, y
    // lo medido ya se oye sin esperar al final.
    //
    // Las cifradas no se pueden leer y se quedan como estan.
    public static class VolumenCanciones
    {
        public const string NombreNormalizar = "Normalize Songs Volume";
        public const string NombreRestaurar = "Restore Songs Volume";

        // ------------------------------------------------------------ BASS
        private const uint StreamDecode = 0x200000;
        private const uint MuestraFloat = 0x100;
        private const uint Unicode = 0x80000000;
        private const int AtributoVolDsp = 19;          // BASS_ATTRIB_VOLDSP

        // Los fragmentos que se miden (ver "por fragmentos").
        private const int Fragmentos = 12;
        private const double SegundosFragmento = 1.5;

        [DllImport("bass", EntryPoint = "BASS_StreamCreateFile", CharSet = CharSet.Unicode)]
        private static extern uint CrearDeArchivo(bool memoria,
            [MarshalAs(UnmanagedType.LPWStr)] string archivo, ulong desde, ulong largo, uint banderas);

        [DllImport("bass", EntryPoint = "BASS_StreamCreateFile")]
        private static extern uint CrearDeMemoria(bool memoria, IntPtr datos,
            ulong desde, ulong largo, uint banderas);

        [DllImport("bass")]
        private static extern bool BASS_StreamFree(uint canal);

        [DllImport("bass")]
        private static extern int BASS_ChannelGetData(uint canal, float[] destino, uint largo);

        [DllImport("bass")]
        private static extern bool BASS_ChannelSetAttribute(uint canal, int atributo, float valor);

        [DllImport("bass")]
        private static extern bool BASS_ChannelGetInfo(uint canal, out InfoCanal info);

        [DllImport("bass")]
        private static extern long BASS_ChannelGetLength(uint canal, uint modo);

        [DllImport("bass")]
        private static extern double BASS_ChannelBytes2Seconds(uint canal, long bytes);

        [DllImport("bass")]
        private static extern long BASS_ChannelSeconds2Bytes(uint canal, double segundos);

        [DllImport("bass")]
        private static extern bool BASS_ChannelSetPosition(uint canal, long bytes, uint modo);

        [StructLayout(LayoutKind.Sequential)]
        private struct InfoCanal
        {
            public uint frecuencia;
            public uint canales;
            public uint banderas;
            public uint tipo;
            public uint resolucionOriginal;
            public uint complemento;
            public uint muestra;
            public IntPtr nombreArchivo;
        }

        // Las pistas y formatos que carga el juego, en su orden de preferencia
        // (BassAudioManager, stemFileNames y containerTypes).
        private static readonly string[] Pistas =
        {
            "guitar", "bass", "rhythm", "vocals", "vocals_1", "vocals_2",
            "drums", "drums_1", "drums_2", "drums_3", "drums_4", "keys", "song", "crowd"
        };
        private static readonly string[] Formatos = { ".opus", ".ogg", ".mp3", ".wav" };
        private const string Previa = "preview";

        private const double TopeDb = 12.0;

        // ------------------------------------------------------------ datos
        private class Medida
        {
            public string firma = "";
            public double lufs = double.NaN;
            public double pico;
            public double lufsPrevia = double.NaN;
            public double picoPrevia;
        }

        // Lo que consulta la reproduccion. Se sustituye entera, nunca se
        // modifica: la lee el hilo de carga del juego.
        private class Tabla
        {
            public readonly Dictionary<string, float> cancion = new Dictionary<string, float>();
            public readonly Dictionary<string, float> previa = new Dictionary<string, float>();
        }

        private static Dictionary<string, Medida> medidas = new Dictionary<string, Medida>();
        private static volatile Tabla tabla = new Tabla();
        private static double objetivo = double.NaN;

        private static string Ruta
        {
            get { return Path.Combine(MelonEnvironment.UserDataDirectory, "CoolMod", "song_volume.tsv"); }
        }

        // ------------------------------------------------------- reproduccion
        private const string MetodoCargar =
            "Method_Public_Boolean_SongEntry_Boolean_EnumPublicSealedvaGuBaRhSiGuDrSiKePrUnique_EnumPublicSealedvaGuBaRhSiGuDrSiKePrUnique_0";
        private const string MetodoPrevia = "Method_Private_Void_PDM_10";          // m80
        private const string MetodoSalida = "Method_Private_Void_Int32_0";         // m77

        [ThreadStatic] private static string claveEnCarga;
        [ThreadStatic] private static bool cargandoPistas;

        private static int ultimoCanal;
        private static string ultimaClave;
        private static bool ultimaPrevia;

        public static void InstalarParches(HarmonyLib.Harmony harmony)
        {
            Cargar();
            try
            {
                Type t = typeof(Il2Cpp.BassAudioManager);
                BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic
                               | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                MethodInfo cargar = t.GetMethod(MetodoCargar, f);
                MethodInfo previa = t.GetMethod(MetodoPrevia, f);
                MethodInfo salida = t.GetMethod(MetodoSalida, f);
                if (cargar == null || previa == null || salida == null
                    || salida.GetParameters().Length != 1 || previa.GetParameters().Length != 0)
                {
                    MelonLogger.Warning("[Volumen] BassAudioManager no tiene la forma esperada:"
                        + " el volumen de las canciones no se ajusta");
                    return;
                }
                harmony.Patch(cargar, Metodo("AntesDeCargar"), Metodo("TrasCargar"));
                harmony.Patch(previa, Metodo("AntesDePrevia"));
                harmony.Patch(salida, null, Metodo("TrasCrearSalida"));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Volumen] parches: " + ex.Message);
            }
        }

        private static HarmonyLib.HarmonyMethod Metodo(string nombre)
        {
            return new HarmonyLib.HarmonyMethod(typeof(VolumenCanciones).GetMethod(
                nombre, BindingFlags.NonPublic | BindingFlags.Static));
        }

        private static void AntesDeCargar(Il2Cpp.SongEntry __0)
        {
            claveEnCarga = Clave(__0, out string _);
            cargandoPistas = true;
        }

        private static void TrasCargar()
        {
            cargandoPistas = false;
        }

        // m80 vacia f_80 nada mas empezar: hay que leerlo antes.
        private static void AntesDePrevia(Il2Cpp.BassAudioManager __instance)
        {
            try
            {
                claveEnCarga = Clave(__instance.field_Private_SongEntry_0, out string _);
            }
            catch (Exception)
            {
                claveEnCarga = null;
            }
            cargandoPistas = false;
        }

        private static void TrasCrearSalida(Il2Cpp.BassAudioManager __instance)
        {
            try
            {
                // Fuera de m106 solo se llega a m77 con un preview.* propio.
                Aplicar(__instance.field_Private_Int32_0, claveEnCarga, !cargandoPistas);   // linkedChannel
            }
            catch (Exception)
            {
            }
        }

        private static void Aplicar(int canal, string clave, bool previa)
        {
            ultimoCanal = canal;
            ultimaClave = clave;
            ultimaPrevia = previa;
            if (canal != 0)
            {
                float f = Factor(clave, previa);
                bool ok = BASS_ChannelSetAttribute((uint)canal, AtributoVolDsp, f);
                if (!ok && !avisado)
                {
                    avisado = true;
                    MelonLogger.Warning("[Volumen] BASS rechazo el ajuste de volumen (error "
                        + BASS_ErrorGetCode().ToString() + ")");
                }
            }
        }

        private static bool avisado;

        [DllImport("bass")]
        private static extern int BASS_ErrorGetCode();

        private static float Factor(string clave, bool previa)
        {
            Tabla t = tabla;
            if (clave == null)
            {
                return 1f;
            }
            if (previa && t.previa.TryGetValue(clave, out float p))
            {
                return p;
            }
            return t.cancion.TryGetValue(clave, out float c) ? c : 1f;
        }

        // Tras normalizar o restaurar, para que se oiga ya en lo que suena.
        private static void Reaplicar()
        {
            try
            {
                Il2Cpp.BassAudioManager m = Il2Cpp.BassAudioManager.instance;
                if (m != null && ultimoCanal != 0 && m.field_Private_Int32_0 == ultimoCanal)
                {
                    Aplicar(ultimoCanal, ultimaClave, ultimaPrevia);
                }
            }
            catch (Exception)
            {
            }
        }

        // La carpeta de la cancion, o el .sng si va empaquetada. null si esta
        // cifrada.
        private static string Clave(Il2Cpp.SongEntry s, out string sng)
        {
            sng = null;
            if (s == null || s.isEnc)
            {
                return null;
            }
            if (s.isSng)
            {
                string[] candidatas = { s.folderPath, s.ChartPath };
                for (int i = 0; i < candidatas.Length; i++)
                {
                    if (ArchivoSng.EsSng(candidatas[i]))
                    {
                        sng = candidatas[i];
                        return Normalizada(sng);
                    }
                }
                return null;
            }
            return string.IsNullOrEmpty(s.folderPath) ? null : Normalizada(s.folderPath);
        }

        private static string Normalizada(string ruta)
        {
            return ruta.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        }

        // ------------------------------------------------------------ medir
        private class Tarea
        {
            public string clave;
            public string carpeta;
            public string sng;
        }

        private static volatile bool corriendo;
        private static volatile bool terminado;
        private static int total;
        private static int hechas;
        private static int medidasNuevas;
        private static int alDia;
        private static int fallidas;
        private static int siguiente;

        public static bool Corriendo { get { return corriendo; } }
        public static int Total { get { return total; } }
        public static int Hechas { get { return hechas; } }
        public static int Medidas { get { return medidasNuevas; } }
        public static int AlDia { get { return alDia; } }
        public static int Fallidas { get { return fallidas; } }

        // Desde el menu, en el hilo principal: aqui se recoge la biblioteca
        // (objetos del juego) y lo demas va a hilos aparte.
        public static void Normalizar()
        {
            if (corriendo)
            {
                Aviso.Mostrar(NombreNormalizar, "Already measuring in the background: "
                    + hechas.ToString() + " / " + total.ToString() + ".");
                return;
            }
            cancelar = false;
            restaurarAlAcabar = false;
            List<Tarea> tareas = new List<Tarea>();
            try
            {
                var canciones = CalculadorDificultad.ListaCanciones();
                HashSet<string> vistas = new HashSet<string>();
                for (int i = 0; canciones != null && i < canciones.Count; i++)
                {
                    Il2Cpp.SongEntry s = canciones[i];
                    string clave = Clave(s, out string sng);
                    if (clave == null || !vistas.Add(clave))
                    {
                        continue;
                    }
                    tareas.Add(new Tarea { clave = clave, carpeta = sng == null ? s.folderPath : null, sng = sng });
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Volumen] no se pudo leer la biblioteca: " + ex);
                return;
            }
            if (tareas.Count == 0)
            {
                Aviso.Mostrar(NombreNormalizar, "No songs to measure. Scan your songs first.");
                return;
            }
            corriendo = true;
            total = tareas.Count;
            hechas = 0;
            medidasNuevas = 0;
            alDia = 0;
            fallidas = 0;
            siguiente = 0;
            Dictionary<string, Medida> previas = medidas;
            Thread hilo = new Thread(() => Procesar(tareas, previas));
            hilo.IsBackground = true;
            hilo.Name = "VolumenCanciones";
            hilo.Start();
        }

        private static void Procesar(List<Tarea> tareas, Dictionary<string, Medida> previas)
        {
            DateTime inicio = DateTime.Now;
            Dictionary<string, Medida> nuevas = new Dictionary<string, Medida>(previas);
            try
            {
                // Uno por nucleo logico menos uno, para el juego, y no mas de
                // cuatro: un .sng se lee entero a memoria.
                int hilos = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1));
                Thread[] equipo = new Thread[hilos];
                for (int h = 0; h < hilos; h++)
                {
                    equipo[h] = new Thread(() => Trabajar(tareas, previas, nuevas));
                    equipo[h].IsBackground = true;
                    equipo[h].Priority = ThreadPriority.BelowNormal;      // el juego primero
                    equipo[h].Name = "Volumen" + h.ToString();
                    equipo[h].Start();
                }
                // Mientras trabajan, se guarda lo medido cada pocos segundos:
                // si se cierra el juego no se pierde, y ya se oye.
                DateTime guardado = DateTime.Now;
                for (int h = 0; h < hilos; h++)
                {
                    while (!equipo[h].Join(500))
                    {
                        if (!cancelar && (DateTime.Now - guardado).TotalSeconds >= GuardarCada)
                        {
                            GuardarParcial(nuevas);
                            guardado = DateTime.Now;
                        }
                    }
                }
                if (!restaurarAlAcabar)
                {
                    GuardarParcial(nuevas);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Volumen] " + ex);
            }
            finally
            {
                MelonLogger.Msg("[Volumen] TERMINADO en " + ((int)(DateTime.Now - inicio).TotalSeconds).ToString()
                    + " s: " + medidasNuevas.ToString() + " medidas, " + alDia.ToString() + " al dia, "
                    + fallidas.ToString() + " sin audio legible; objetivo "
                    + objetivo.ToString("0.0", CultureInfo.InvariantCulture) + " LUFS");
                if (restaurarAlAcabar)
                {
                    Olvidar();
                }
                terminado = true;
                corriendo = false;
            }
        }

        private const double GuardarCada = 10.0;      // segundos

        private static void GuardarParcial(Dictionary<string, Medida> nuevas)
        {
            Dictionary<string, Medida> copia;
            lock (nuevas)
            {
                copia = new Dictionary<string, Medida>(nuevas);
            }
            Fijar(copia);
            try
            {
                Guardar();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Volumen] no se pudo guardar: " + ex.Message);
            }
        }

        private static void Trabajar(List<Tarea> tareas, Dictionary<string, Medida> previas,
                                     Dictionary<string, Medida> nuevas)
        {
            while (true)
            {
                // En segundo plano: mientras se toca una cancion, quieto. Medir
                // ahi le quitaria procesador al juego justo cuando mas lo nota.
                while (Buscador.EnJuego && !cancelar)
                {
                    Thread.Sleep(500);
                }
                if (cancelar)
                {
                    return;
                }
                int i = Interlocked.Increment(ref siguiente) - 1;
                if (i >= tareas.Count)
                {
                    return;
                }
                Tarea t = tareas[i];
                try
                {
                    Medida m = MedirCancion(t, previas);
                    if (m == null)
                    {
                        Interlocked.Increment(ref fallidas);
                    }
                    else
                    {
                        lock (nuevas)
                        {
                            nuevas[t.clave] = m;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref fallidas);
                    MelonLogger.Warning("[Volumen] " + t.clave + ": " + ex.Message);
                }
                Interlocked.Increment(ref hechas);
            }
        }

        // Una fuente de audio: un archivo, o un archivo sacado de un .sng.
        private class Fuente
        {
            public string ruta;
            public byte[] datos;
        }

        private static Medida MedirCancion(Tarea t, Dictionary<string, Medida> previas)
        {
            List<Fuente> pistas = new List<Fuente>();
            Fuente previa = null;
            string firma;
            if (t.sng != null)
            {
                FileInfo fi = new FileInfo(t.sng);
                firma = fi.Length.ToString() + ":" + fi.LastWriteTimeUtc.Ticks.ToString();
                if (previas.TryGetValue(t.clave, out Medida vieja) && vieja.firma == firma)
                {
                    Interlocked.Increment(ref alDia);
                    return vieja;
                }
                ArchivoSng s = ArchivoSng.Leer(t.sng);
                foreach (string pista in Pistas)
                {
                    ArchivoSng.Entrada e = EnSng(s, pista);
                    if (e != null)
                    {
                        pistas.Add(new Fuente { datos = s.LeerArchivo(e) });
                    }
                }
                ArchivoSng.Entrada ep = EnSng(s, Previa);
                if (ep != null)
                {
                    previa = new Fuente { datos = s.LeerArchivo(ep) };
                }
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                foreach (string pista in Pistas)
                {
                    string r = EnCarpeta(t.carpeta, pista, sb);
                    if (r != null)
                    {
                        pistas.Add(new Fuente { ruta = r });
                    }
                }
                string rp = EnCarpeta(t.carpeta, Previa, sb);
                if (rp != null)
                {
                    previa = new Fuente { ruta = rp };
                }
                firma = sb.ToString();
                if (previas.TryGetValue(t.clave, out Medida vieja) && vieja.firma == firma)
                {
                    Interlocked.Increment(ref alDia);
                    return vieja;
                }
            }
            if (pistas.Count == 0)
            {
                return null;
            }
            Medida m = new Medida { firma = firma };
            if (!Medir(pistas, out m.lufs, out m.pico))
            {
                return null;
            }
            if (previa != null)
            {
                Medir(new List<Fuente> { previa }, out m.lufsPrevia, out m.picoPrevia);
            }
            Interlocked.Increment(ref medidasNuevas);
            return m;
        }

        private static string EnCarpeta(string carpeta, string nombre, StringBuilder firma)
        {
            foreach (string ext in Formatos)
            {
                string r = Path.Combine(carpeta, nombre + ext);
                FileInfo fi = new FileInfo(r);
                if (fi.Exists)
                {
                    firma.Append(nombre).Append(ext).Append(':').Append(fi.Length)
                         .Append(':').Append(fi.LastWriteTimeUtc.Ticks).Append(';');
                    return r;
                }
            }
            return null;
        }

        private static ArchivoSng.Entrada EnSng(ArchivoSng s, string nombre)
        {
            foreach (string ext in Formatos)
            {
                ArchivoSng.Entrada e = s.Buscar(nombre + ext);
                if (e != null)
                {
                    return e;
                }
            }
            return null;
        }

        // Una pista abierta para medir.
        private class Pista
        {
            public uint canal;
            public uint frecuencia;
            public int canales;
            public float[] buffer;
        }

        // Mide la suma de las fuentes por fragmentos. false si no se pudo
        // leer nada o es silencio.
        private static bool Medir(List<Fuente> fuentes, out double lufs, out double pico)
        {
            lufs = double.NaN;
            pico = 0;
            List<Pista> pistas = new List<Pista>();
            List<GCHandle> fijados = new List<GCHandle>();
            try
            {
                double duracion = 0;
                foreach (Fuente f in fuentes)
                {
                    uint h;
                    if (f.datos != null)
                    {
                        GCHandle g = GCHandle.Alloc(f.datos, GCHandleType.Pinned);
                        fijados.Add(g);
                        h = CrearDeMemoria(true, g.AddrOfPinnedObject(), 0, (ulong)f.datos.Length,
                                           StreamDecode | MuestraFloat);
                    }
                    else
                    {
                        h = CrearDeArchivo(false, f.ruta, 0, 0, StreamDecode | MuestraFloat | Unicode);
                    }
                    if (h == 0)
                    {
                        continue;
                    }
                    if (!BASS_ChannelGetInfo(h, out InfoCanal info) || info.frecuencia == 0 || info.canales == 0)
                    {
                        BASS_StreamFree(h);
                        continue;
                    }
                    pistas.Add(new Pista { canal = h, frecuencia = info.frecuencia, canales = (int)info.canales });
                    duracion = Math.Max(duracion, BASS_ChannelBytes2Seconds(h, BASS_ChannelGetLength(h, 0)));
                }
                if (pistas.Count == 0 || duracion <= 0)
                {
                    return false;
                }

                // Los fragmentos, centrados en partes iguales de la cancion. Si
                // es tan corta que no da para ellos, entera.
                List<double> inicios = new List<double>();
                double largo = SegundosFragmento;
                if (duracion <= Fragmentos * SegundosFragmento * 2)
                {
                    inicios.Add(0);
                    largo = duracion;
                }
                else
                {
                    for (int k = 0; k < Fragmentos; k++)
                    {
                        inicios.Add((k + 0.5) / Fragmentos * duracion - SegundosFragmento / 2);
                    }
                }

                // Un grupo por frecuencia: dentro se suman las muestras.
                Dictionary<uint, Sonoridad> grupos = new Dictionary<uint, Sonoridad>();
                foreach (Pista p in pistas)
                {
                    if (!grupos.ContainsKey(p.frecuencia))
                    {
                        grupos[p.frecuencia] = new Sonoridad(p.frecuencia);
                    }
                }
                foreach (KeyValuePair<uint, Sonoridad> g in grupos)
                {
                    MedirGrupo(g.Value, pistas.FindAll(p => p.frecuencia == g.Key), inicios, largo);
                }
                return Sonoridad.Combinar(new List<Sonoridad>(grupos.Values), out lufs, out pico);
            }
            finally
            {
                foreach (Pista p in pistas)
                {
                    BASS_StreamFree(p.canal);
                }
                foreach (GCHandle g in fijados)
                {
                    g.Free();
                }
            }
        }

        private static void MedirGrupo(Sonoridad s, List<Pista> pistas, List<double> inicios, double largo)
        {
            uint fs = pistas[0].frecuencia;
            int bloque = (int)fs / 4;      // 250 ms por lectura
            float[] suma = new float[bloque * 2];
            foreach (double inicio in inicios)
            {
                foreach (Pista p in pistas)
                {
                    BASS_ChannelSetPosition(p.canal, BASS_ChannelSeconds2Bytes(p.canal, Math.Max(0, inicio)), 0);
                }
                s.NuevoFragmento();
                long restantes = (long)(largo * fs);
                while (restantes > 0)
                {
                    int quiero = (int)Math.Min(bloque, restantes);
                    Array.Clear(suma, 0, quiero * 2);
                    int leidos = 0;
                    foreach (Pista p in pistas)
                    {
                        int ch = p.canales;
                        if (p.buffer == null || p.buffer.Length < bloque * ch)
                        {
                            p.buffer = new float[bloque * ch];
                        }
                        int n = BASS_ChannelGetData(p.canal, p.buffer, (uint)(quiero * ch * 4));
                        if (n <= 0)
                        {
                            continue;
                        }
                        int frames = n / 4 / ch;
                        leidos = Math.Max(leidos, frames);
                        float[] x = p.buffer;
                        if (ch == 1)
                        {
                            for (int j = 0; j < frames; j++)
                            {
                                suma[2 * j] += x[j];
                                suma[2 * j + 1] += x[j];
                            }
                        }
                        else
                        {
                            // Estereo tal cual; con mas canales, pares a la
                            // izquierda e impares a la derecha.
                            for (int j = 0; j < frames; j++)
                            {
                                for (int q = 0; q < ch; q++)
                                {
                                    suma[2 * j + (q & 1)] += x[j * ch + q];
                                }
                            }
                        }
                    }
                    if (leidos == 0)
                    {
                        break;
                    }
                    s.Anadir(suma, leidos);
                    restantes -= leidos;
                }
            }
        }

        // Sonoridad segun ITU-R BS.1770-4. Los coeficientes del filtro K se
        // calculan para la frecuencia de la pista (la norma los da para 48 kHz;
        // la formula es la de pyloudnorm, que a 48 kHz da esos mismos).
        private class Sonoridad
        {
            private readonly double b0, b1, b2, a1, a2, c1, c2;
            private readonly int subbloque;      // 100 ms
            private readonly double[] z = new double[8];
            private double sumaL;
            private double sumaR;
            private int enSubbloque;
            public double pico;
            public readonly List<double> subbloques = new List<double>();
            public readonly List<int> cortes = new List<int>();     // donde empieza cada fragmento

            public Sonoridad(uint fs)
            {
                double k = Math.Tan(Math.PI * 1681.974450955533 / fs);
                double q = 0.7071752369554196;
                double vh = Math.Pow(10.0, 3.999843853973347 / 20.0);
                double vb = Math.Pow(vh, 0.4996667741545416);
                double a0 = 1.0 + k / q + k * k;
                b0 = (vh + vb * k / q + k * k) / a0;
                b1 = 2.0 * (k * k - vh) / a0;
                b2 = (vh - vb * k / q + k * k) / a0;
                a1 = 2.0 * (k * k - 1.0) / a0;
                a2 = (1.0 - k / q + k * k) / a0;
                double k2 = Math.Tan(Math.PI * 38.13547087602444 / fs);
                double q2 = 0.5003270373238773;
                double d0 = 1.0 + k2 / q2 + k2 * k2;
                c1 = 2.0 * (k2 * k2 - 1.0) / d0;
                c2 = (1.0 - k2 / q2 + k2 * k2) / d0;
                subbloque = (int)(fs / 10);
            }

            public void NuevoFragmento()
            {
                Array.Clear(z, 0, z.Length);
                sumaL = 0;
                sumaR = 0;
                enSubbloque = 0;
                cortes.Add(subbloques.Count);
            }

            // Muestras estereo intercaladas.
            public void Anadir(float[] x, int frames)
            {
                for (int j = 0; j < frames; j++)
                {
                    double l = x[2 * j];
                    double r = x[2 * j + 1];
                    double al = Math.Abs(l);
                    double ar = Math.Abs(r);
                    if (al > pico) { pico = al; }
                    if (ar > pico) { pico = ar; }
                    double kl = K(l, 0);
                    double kr = K(r, 4);
                    sumaL += kl * kl;
                    sumaR += kr * kr;
                    if (++enSubbloque == subbloque)
                    {
                        subbloques.Add((sumaL + sumaR) / subbloque);
                        sumaL = 0;
                        sumaR = 0;
                        enSubbloque = 0;
                    }
                }
            }

            // Las dos etapas del filtro K, en forma directa II traspuesta. La
            // segunda tiene numerador 1, -2, 1.
            private double K(double x, int o)
            {
                double y = b0 * x + z[o];
                z[o] = b1 * x - a1 * y + z[o + 1];
                z[o + 1] = b2 * x - a2 * y;
                double w = y + z[o + 2];
                z[o + 2] = -2.0 * y - c1 * w + z[o + 3];
                z[o + 3] = y - c2 * w;
                return w;
            }

            // Suma la energia de los grupos (subbloque a subbloque: todos
            // miden los mismos fragmentos) y aplica las puertas.
            public static bool Combinar(List<Sonoridad> grupos, out double lufs, out double pico)
            {
                lufs = double.NaN;
                pico = 0;
                int n = int.MaxValue;
                foreach (Sonoridad g in grupos)
                {
                    n = Math.Min(n, g.subbloques.Count);
                    pico = Math.Max(pico, g.pico);
                }
                double[] energia = new double[n];
                foreach (Sonoridad g in grupos)
                {
                    for (int i = 0; i < n; i++)
                    {
                        energia[i] += g.subbloques[i];
                    }
                }
                // Bloques de 400 ms que avanzan de 100 en 100, sin cruzar de
                // un fragmento al siguiente.
                List<double> bloques = new List<double>();
                List<int> cortes = grupos[0].cortes;
                int siguienteCorte = 1;
                for (int j = 0; j + 3 < n; j++)
                {
                    while (siguienteCorte < cortes.Count && cortes[siguienteCorte] <= j)
                    {
                        siguienteCorte++;
                    }
                    if (siguienteCorte < cortes.Count && j + 3 >= cortes[siguienteCorte])
                    {
                        continue;
                    }
                    bloques.Add((energia[j] + energia[j + 1] + energia[j + 2] + energia[j + 3]) / 4);
                }
                double suma = 0;
                int cuantos = 0;
                foreach (double b in bloques)
                {
                    if (Lufs(b) > -70.0)
                    {
                        suma += b;
                        cuantos++;
                    }
                }
                if (cuantos == 0)
                {
                    return false;      // silencio
                }
                double relativa = Lufs(suma / cuantos) - 10.0;
                suma = 0;
                cuantos = 0;
                foreach (double b in bloques)
                {
                    double l = Lufs(b);
                    if (l > -70.0 && l > relativa)
                    {
                        suma += b;
                        cuantos++;
                    }
                }
                if (cuantos == 0)
                {
                    return false;
                }
                lufs = Lufs(suma / cuantos);
                return true;
            }

            private static double Lufs(double potencia)
            {
                return potencia > 0 ? -0.691 + 10.0 * Math.Log10(potencia) : double.NegativeInfinity;
            }
        }

        // ------------------------------------------- ganancias y archivo
        // Con todas las medidas: el objetivo y el pico tipico (medianas de la
        // biblioteca) y lo que toca a cada cancion. Sustituye la tabla de golpe.
        private static void Fijar(Dictionary<string, Medida> todas)
        {
            List<double> niveles = new List<double>();
            List<double> picos = new List<double>();
            foreach (Medida m in todas.Values)
            {
                if (!double.IsNaN(m.lufs))
                {
                    niveles.Add(m.lufs);
                    picos.Add(m.pico);
                }
            }
            double meta = Mediana(niveles);
            double picoTipico = Mediana(picos);

            Tabla t = new Tabla();
            foreach (KeyValuePair<string, Medida> kv in todas)
            {
                t.cancion[kv.Key] = Ganancia(kv.Value.lufs, kv.Value.pico, meta, picoTipico);
                if (!double.IsNaN(kv.Value.lufsPrevia))
                {
                    t.previa[kv.Key] = Ganancia(kv.Value.lufsPrevia, kv.Value.picoPrevia, meta, picoTipico);
                }
            }
            medidas = todas;
            objetivo = meta;
            tabla = t;
        }

        private static double Mediana(List<double> v)
        {
            if (v.Count == 0)
            {
                return double.NaN;
            }
            v.Sort();
            return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
        }

        private static float Ganancia(double lufs, double pico, double meta, double picoTipico)
        {
            if (double.IsNaN(lufs) || double.IsNaN(meta))
            {
                return 1f;
            }
            double db = meta - lufs;
            if (db > 0 && pico > 0 && !double.IsNaN(picoTipico))
            {
                // Lo que se puede subir sin que su pico pase del tipico.
                db = Math.Min(db, Math.Max(0.0, 20.0 * Math.Log10(picoTipico / pico)));
            }
            db = Math.Max(-TopeDb, Math.Min(TopeDb, db));
            return (float)Math.Pow(10.0, db / 20.0);
        }

        private static void Cargar()
        {
            Dictionary<string, Medida> leidas = new Dictionary<string, Medida>();
            try
            {
                if (File.Exists(Ruta))
                {
                    foreach (string linea in File.ReadAllLines(Ruta, Encoding.UTF8))
                    {
                        if (linea.Length == 0 || linea[0] == '#')
                        {
                            continue;
                        }
                        string[] c = linea.Split('\t');
                        if (c.Length < 6)
                        {
                            continue;
                        }
                        leidas[c[0]] = new Medida
                        {
                            firma = c[1],
                            lufs = Numero(c[2]),
                            pico = Numero(c[3]),
                            lufsPrevia = Numero(c[4]),
                            picoPrevia = Numero(c[5])
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Volumen] no se pudo leer " + Ruta + ": " + ex.Message);
            }
            Fijar(leidas);
            if (leidas.Count > 0)
            {
                MelonLogger.Msg("[Volumen] " + leidas.Count.ToString() + " canciones normalizadas");
            }
        }

        private static void Guardar()
        {
            string dir = Path.GetDirectoryName(Ruta);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("# Cool Mod: measured loudness of each song. Delete this file, or use\n");
            sb.Append("# Settings > General > Restore Songs Volume, to play every song as it is.\n");
            foreach (KeyValuePair<string, Medida> kv in medidas)
            {
                Medida m = kv.Value;
                sb.Append(kv.Key).Append('\t').Append(m.firma).Append('\t')
                  .Append(Texto(m.lufs)).Append('\t').Append(Texto(m.pico)).Append('\t')
                  .Append(Texto(m.lufsPrevia)).Append('\t').Append(Texto(m.picoPrevia)).Append('\n');
            }
            File.WriteAllText(Ruta, sb.ToString(), new UTF8Encoding(false));
        }

        private static string Texto(double v)
        {
            return double.IsNaN(v) ? "-" : v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static double Numero(string s)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v : double.NaN;
        }

        // ------------------------------------------------------- restaurar
        // Si se esta midiendo, ademas lo para: es la forma de cancelar. Los
        // hilos acaban la cancion que tienen entre manos (menos de un segundo)
        // y es el que coordina quien borra, para que ningun guardado parcial
        // vuelva a escribir el archivo despues.
        public static void Restaurar()
        {
            if (corriendo)
            {
                restaurarAlAcabar = true;
                cancelar = true;
                return;      // el aviso lo da Tick al acabar
            }
            Olvidar();
            Reaplicar();
            Aviso.Mostrar(NombreRestaurar, TextoRestaurado);
            MelonLogger.Msg("[Volumen] restaurado");
        }

        private const string TextoRestaurado = "Every song plays at its original volume again.";

        private static volatile bool cancelar;
        private static volatile bool restaurarAlAcabar;

        private static void Olvidar()
        {
            try
            {
                if (File.Exists(Ruta))
                {
                    File.Delete(Ruta);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Volumen] no se pudo borrar " + Ruta + ": " + ex.Message);
            }
            Fijar(new Dictionary<string, Medida>());
        }

        // Cada fotograma de menu, en el hilo principal: el indicador de la
        // esquina mientras mide, y el aviso y el ajuste de lo que suena al
        // acabar.
        public static void Tick()
        {
            if (corriendo)
            {
                int t = total;
                int h = hechas;
                OverlayProgreso.Esquina(restaurarAlAcabar ? "Stopping volume normalization..."
                    : "Normalizing songs volume: " + h.ToString() + " / " + t.ToString()
                      + "  (" + (t > 0 ? h * 100 / t : 0).ToString() + "%)");
                return;
            }
            OverlayProgreso.Esquina(null);
            if (!terminado)
            {
                return;
            }
            terminado = false;
            Reaplicar();
            if (restaurarAlAcabar)
            {
                Aviso.Mostrar(NombreRestaurar, TextoRestaurado);
                MelonLogger.Msg("[Volumen] medicion cancelada y restaurado");
                return;
            }
            Aviso.Mostrar(NombreNormalizar, medidasNuevas.ToString() + " song(s) measured, "
                + alDia.ToString() + " already up to date"
                + (fallidas > 0 ? ", " + fallidas.ToString() + " without readable audio." : "."));
        }
    }
}
