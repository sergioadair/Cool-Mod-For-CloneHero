using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MelonLoader;

namespace CloneHeroMod
{
    // Leer un archivo de audio como muestras sueltas, para poder analizarlo.
    //
    // NO ENVIAMOS NINGUN DECODIFICADOR. El juego ya trae BASS cargado en el
    // proceso —bass.dll, bass_fx.dll, bassmix.dll y bassopus.dll viven en
    // Clone Hero_Data\Plugins\x86_64— y entre los 111 simbolos que exporta
    // estan los cuatro que hacen falta. Como el modulo ya esta cargado,
    // DllImport("bass") se engancha al que hay; no busca ni carga otro.
    //
    // Eso nos da ogg, opus, mp3, wav y flac sin anadir una sola dependencia,
    // y a velocidad nativa.
    //
    // La clave es BASS_STREAM_DECODE: crea un canal que NO suena. No toca la
    // tarjeta, no interfiere con la reproduccion del juego y se lee tan rapido
    // como de el disco. Con BASS_SAMPLE_FLOAT y BASS_SAMPLE_MONO, BASS ya
    // entrega muestras float mezcladas a un canal, que es lo que queremos.
    //
    // Esto es codigo nativo puro: no toca Il2Cpp, asi que puede correr en un
    // hilo de fondo como hace GeneradorLote.
    public static class AudioBass
    {
        private const uint StreamDecode = 0x200000;
        private const uint MuestraFloat = 256;
        private const uint MuestraMono = 2;
        private const uint Unicode = 0x80000000;
        private const uint PosicionByte = 0;

        [DllImport("bass", CharSet = CharSet.Unicode)]
        private static extern uint BASS_StreamCreateFile(bool memoria,
            [MarshalAs(UnmanagedType.LPWStr)] string archivo,
            ulong desde, ulong largo, uint banderas);

        [DllImport("bass")]
        private static extern bool BASS_StreamFree(uint canal);

        [DllImport("bass")]
        private static extern uint BASS_ChannelGetData(uint canal, IntPtr destino, uint largo);

        [DllImport("bass")]
        private static extern long BASS_ChannelGetLength(uint canal, uint modo);

        [DllImport("bass")]
        private static extern bool BASS_ChannelGetInfo(uint canal, out InfoCanal info);

        [DllImport("bass")]
        private static extern int BASS_ErrorGetCode();

        [DllImport("bass")]
        private static extern double BASS_ChannelBytes2Seconds(uint canal, long posicion);

        [DllImport("bass")]
        private static extern int BASS_GetVersion();

        [DllImport("bass")]
        private static extern bool BASS_Init(int dispositivo, uint frecuencia,
            uint banderas, IntPtr ventana, IntPtr guid);

        [DllImport("bass", CharSet = CharSet.Unicode)]
        private static extern uint BASS_PluginLoad(
            [MarshalAs(UnmanagedType.LPWStr)] string archivo, uint banderas);

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

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectoryW(string ruta);

        private static bool preparado;
        private static bool utilizable;

        // Dentro del juego no hay nada que preparar: BASS esta cargado e
        // inicializado desde antes que el mod. Esto existe para que el mismo
        // codigo sirva en el banco de pruebas, que corre fuera del juego y
        // necesita encontrar las DLL y arrancar el dispositivo mudo.
        public static bool Preparar(string carpetaPlugins)
        {
            if (preparado)
            {
                return utilizable;
            }
            preparado = true;
            try
            {
                if (!string.IsNullOrEmpty(carpetaPlugins) && Directory.Exists(carpetaPlugins))
                {
                    SetDllDirectoryW(carpetaPlugins);
                }
                if (BASS_GetVersion() == 0)
                {
                    return false;
                }
                // Dispositivo 0 = "sin sonido". Si el juego ya inicializo BASS
                // esto falla y da igual: para un canal de solo decodificar no
                // hace falta dispositivo ninguno.
                BASS_Init(0, 44100, 0, IntPtr.Zero, IntPtr.Zero);
                if (!string.IsNullOrEmpty(carpetaPlugins))
                {
                    foreach (string p in new[] { "bassopus.dll", "bassflac.dll", "bass_fx.dll" })
                    {
                        string ruta = Path.Combine(carpetaPlugins, p);
                        if (File.Exists(ruta))
                        {
                            BASS_PluginLoad(ruta, Unicode);
                        }
                    }
                }
                utilizable = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Audio] BASS no disponible: " + ex.Message);
                utilizable = false;
            }
            return utilizable;
        }


        // ------------------------------------------------------------ ritmo -
        //
        // BASS_FX trae detector de tempo propio, y el juego ya carga
        // bass_fx.dll. Merece la pena antes que escribir uno: lleva anios
        // rodado y es nativo.
        //
        // Dos funciones, y la segunda es la que de verdad interesa:
        //
        //   BPM_DecodeGet      devuelve un numero de pulsaciones por minuto
        //   BPM_BeatDecodeGet  llama a un callback por CADA tiempo, con su
        //                      posicion en segundos
        //
        // La segunda da rejilla Y fase a la vez. Con solo el bpm habria que
        // averiguar aparte donde cae el primer tiempo, que es justo lo que
        // peor salia midiendo por autocorrelacion.

        private const uint BassFxFreeSource = 0x10000;

        private delegate void ProcesoTiempo(uint canal, double posicion, IntPtr usuario);

        [DllImport("bass_fx")]
        private static extern float BASS_FX_BPM_DecodeGet(uint canal, double desde,
            double hasta, uint minMaxBpm, uint banderas, IntPtr proceso, IntPtr usuario);

        [DllImport("bass_fx")]
        private static extern bool BASS_FX_BPM_BeatDecodeGet(uint canal, double desde,
            double hasta, uint banderas, ProcesoTiempo proceso, IntPtr usuario);

        [DllImport("bass_fx")]
        private static extern bool BASS_FX_BPM_Free(uint canal);

        [DllImport("bass_fx")]
        private static extern bool BASS_FX_BPM_BeatFree(uint canal);

        [DllImport("bass_fx")]
        private static extern int BASS_FX_GetVersion();

        // Pulsaciones por minuto segun BASS_FX, o 0 si no lo saca.
        public static double Bpm(string ruta, int minimo = 60, int maximo = 200)
        {
            uint canal = 0;
            try
            {
                canal = Abrir(ruta);
                if (canal == 0)
                {
                    return 0;
                }
                double fin = Duracion(canal);
                if (fin <= 0)
                {
                    return 0;
                }
                uint rango = (uint)minimo | ((uint)maximo << 16);
                float bpm = BASS_FX_BPM_DecodeGet(canal, 0, fin, rango, 0,
                                                  IntPtr.Zero, IntPtr.Zero);
                BASS_FX_BPM_Free(canal);
                return bpm > 0 ? bpm : 0;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Audio] bpm: " + ex.Message);
                return 0;
            }
            finally
            {
                if (canal != 0) BASS_StreamFree(canal);
            }
        }

        // Los tiempos del compas, en segundos. Lista vacia si no hay pulso
        // reconocible, que en un audio hablado es lo normal y lo correcto.
        public static List<double> Tiempos(string ruta)
        {
            List<double> salida = new List<double>();
            uint canal = 0;
            try
            {
                canal = Abrir(ruta);
                if (canal == 0)
                {
                    return salida;
                }
                // el delegado tiene que seguir vivo durante toda la llamada:
                // si lo recoge el basurero a mitad, el nativo salta a un
                // puntero muerto y se lleva el proceso por delante
                ProcesoTiempo eco = delegate (uint c, double pos, IntPtr u)
                {
                    salida.Add(pos);
                };
                double fin = Duracion(canal);
                if (fin <= 0)
                {
                    return salida;
                }
                GCHandle ancla = GCHandle.Alloc(eco);
                try
                {
                    BASS_FX_BPM_BeatDecodeGet(canal, 0, fin, 0, eco, IntPtr.Zero);
                }
                finally
                {
                    ancla.Free();
                }
                BASS_FX_BPM_BeatFree(canal);
                salida.Sort();
                return salida;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Audio] tiempos: " + ex.Message);
                return salida;
            }
            finally
            {
                if (canal != 0) BASS_StreamFree(canal);
            }
        }

        // Diagnostico: version de las dos bibliotecas y ultimo codigo de error
        // de BASS. Sin esto, un fallo aqui es un cero silencioso.
        public static string Diagnostico(string ruta)
        {
            string txt = "bass=" + BASS_GetVersion().ToString("X8");
            try { txt += "  bass_fx=" + BASS_FX_GetVersion().ToString("X8"); }
            catch (Exception e) { txt += "  bass_fx NO CARGA: " + e.GetType().Name; }
            uint c = Abrir(ruta);
            txt += "  canal=" + c + " err=" + BASS_ErrorGetCode();
            if (c != 0)
            {
                InfoCanal info;
                BASS_ChannelGetInfo(c, out info);
                txt += "  " + info.frecuencia + "Hz x" + info.canales;
                long largo = BASS_ChannelGetLength(c, PosicionByte);
                double seg = largo > 0 ? largo / 4.0 / info.frecuencia / Math.Max(1, info.canales) : 0;
                txt += "  ~" + seg.ToString("0.0") + "s";
                try
                {
                    float b = BASS_FX_BPM_DecodeGet(c, 0, seg > 0 ? seg : 120, 
                        (uint)60 | ((uint)200 << 16), 0, IntPtr.Zero, IntPtr.Zero);
                    txt += "  bpm=" + b.ToString("0.0") + " err=" + BASS_ErrorGetCode();
                    BASS_FX_BPM_Free(c);
                }
                catch (Exception e) { txt += "  bpm EXCEPCION: " + e.Message; }
                BASS_StreamFree(c);
            }
            return txt;
        }

        // Lo que dura el canal, en segundos. Las funciones de ritmo de
        // BASS_FX necesitan un final concreto: con -1 devuelven cero y ademas
        // sin codigo de error, que es lo que despista.
        private static double Duracion(uint canal)
        {
            long bytes = BASS_ChannelGetLength(canal, PosicionByte);
            if (bytes <= 0)
            {
                return 0;
            }
            double seg = BASS_ChannelBytes2Seconds(canal, bytes);
            return seg > 0 ? seg : 0;
        }

        private static uint Abrir(string ruta)
        {
            return BASS_StreamCreateFile(false, ruta, 0, 0,
                StreamDecode | MuestraFloat | Unicode);
        }

        // Muestras mono normalizadas a la frecuencia pedida. Devuelve null si
        // el archivo no se puede abrir.
        public static float[] LeerMono(string ruta, int frecuenciaDestino)
        {
            uint canal = 0;
            try
            {
                canal = BASS_StreamCreateFile(false, ruta, 0, 0,
                    StreamDecode | MuestraFloat | MuestraMono | Unicode);
                if (canal == 0)
                {
                    return null;
                }
                InfoCanal info;
                if (!BASS_ChannelGetInfo(canal, out info) || info.frecuencia == 0)
                {
                    return null;
                }

                float[] crudo = Volcar(canal);
                if (crudo == null || crudo.Length < 16)
                {
                    return null;
                }
                // NO SE DA POR BUENA LA BANDERA DE MONO. Se le pide a BASS que
                // mezcle a un canal, pero no siempre lo hace: con un .wav
                // devuelve el estereo intercalado y la bandera se ignora en
                // silencio. Sintoma: todo duraba el doble, porque cada pareja
                // de muestras se contaba como dos instantes.
                //
                // Lo que si dice la verdad es info.canales, que informa de lo
                // que va a entregar de verdad. Asi que se mira eso.
                if (info.canales > 1)
                {
                    crudo = Mezclar(crudo, (int)info.canales);
                }
                return Remuestrear(crudo, (int)info.frecuencia, frecuenciaDestino);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Audio] " + Path.GetFileName(ruta) + ": " + ex.Message);
                return null;
            }
            finally
            {
                if (canal != 0)
                {
                    BASS_StreamFree(canal);
                }
            }
        }

        // BASS_ChannelGetLength sobre un canal de decodificar da el tamano
        // exacto en la mayoria de formatos, pero en algunos comprimidos es una
        // estimacion. Asi que sirve para reservar de una vez, no para fiarse:
        // se lee hasta que no da mas y se recorta a lo leido de verdad.
        private static float[] Volcar(uint canal)
        {
            long bytes = BASS_ChannelGetLength(canal, PosicionByte);
            int estimadas = bytes > 0 && bytes < int.MaxValue
                ? (int)(bytes / 4) : 4 * 1024 * 1024;
            if (estimadas < 1024)
            {
                estimadas = 1024;
            }
            float[] salida = new float[estimadas];
            int escritas = 0;

            const int trozo = 1 << 16;      // muestras por lectura
            float[] tampon = new float[trozo];
            GCHandle mano = GCHandle.Alloc(tampon, GCHandleType.Pinned);
            try
            {
                IntPtr p = mano.AddrOfPinnedObject();
                while (true)
                {
                    uint leidos = BASS_ChannelGetData(canal, p, trozo * 4);
                    if (leidos == 0xFFFFFFFF || leidos == 0)
                    {
                        break;      // fin del archivo o error
                    }
                    int n = (int)(leidos / 4);
                    if (escritas + n > salida.Length)
                    {
                        Array.Resize(ref salida, Math.Max(salida.Length * 2, escritas + n));
                    }
                    Array.Copy(tampon, 0, salida, escritas, n);
                    escritas += n;
                }
            }
            finally
            {
                mano.Free();
            }
            if (escritas == 0)
            {
                return null;
            }
            Array.Resize(ref salida, escritas);
            return salida;
        }

        // A la frecuencia de analisis. Primero una media movil, que hace de
        // filtro para que lo agudo no se doble sobre lo grave al quitar
        // muestras —sin eso aparecen ataques donde no los hay—, y luego
        // interpolacion lineal.
        // Publico porque AudioVideo lo necesita: el audio que saca de un
        // video viene a la frecuencia que quiera el sistema y hay que llevarlo
        // a la del analisis igual que el que viene de BASS.
        public static float[] AjustarFrecuencia(float[] x, int origen, int destino)
        {
            return Remuestrear(x, origen, destino);
        }

        private static float[] Remuestrear(float[] x, int origen, int destino)
        {
            if (origen == destino)
            {
                return x;
            }
            double razon = (double)origen / destino;
            if (razon > 1.0)
            {
                int ancho = (int)Math.Round(razon);
                if (ancho > 1)
                {
                    x = MediaMovil(x, ancho);
                }
            }
            int n = (int)(x.Length / razon);
            if (n < 2)
            {
                return new float[0];
            }
            float[] salida = new float[n];
            for (int i = 0; i < n; i++)
            {
                double pos = i * razon;
                int j = (int)pos;
                if (j + 1 >= x.Length)
                {
                    salida[i] = x[x.Length - 1];
                    continue;
                }
                float f = (float)(pos - j);
                salida[i] = x[j] * (1f - f) + x[j + 1] * f;
            }
            return salida;
        }

        private static float[] Mezclar(float[] x, int canales)
        {
            int cuadros = x.Length / canales;
            float[] salida = new float[cuadros];
            for (int i = 0; i < cuadros; i++)
            {
                float suma = 0;
                int b = i * canales;
                for (int k = 0; k < canales; k++)
                {
                    suma += x[b + k];
                }
                salida[i] = suma / canales;
            }
            return salida;
        }

        private static float[] MediaMovil(float[] x, int ancho)
        {
            float[] salida = new float[x.Length];
            double suma = 0;
            for (int i = 0; i < x.Length; i++)
            {
                suma += x[i];
                if (i >= ancho)
                {
                    suma -= x[i - ancho];
                }
                salida[i] = (float)(suma / Math.Min(i + 1, ancho));
            }
            return salida;
        }
    }
}
