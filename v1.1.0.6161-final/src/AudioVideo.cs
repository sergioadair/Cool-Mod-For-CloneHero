using System;
using System.IO;
using System.Runtime.InteropServices;
using MelonLoader;

namespace CloneHeroMod
{
    // Sacar el audio de un archivo de video.
    //
    // POR QUE NO VALE BASS. Se probo, que es lo suyo antes de escribir nada:
    //
    //     video.mp4   -> canal 0, error 17  (formato no soportado)
    //     video.webm  -> canal 0, error 41  (falta el codec)
    //
    // BASS sabe de ogg, mp3, wav y —con el complemento que trae el juego— de
    // opus. De contenedores de video, nada. Y anadir bassaac.dll o similar
    // seria empezar a enviar binarios de terceros, que es justo lo que este
    // mod no hace.
    //
    // LA SALIDA ES MEDIA FOUNDATION, que viene DENTRO de Windows. No se envia
    // nada: mfplat.dll y mfreadwrite.dll ya estan en el sistema, y de hecho es
    // lo que usa el propio Unity para reproducir los videos de fondo del
    // juego. Decodifica mp4, mov, m4a, avi y wmv con los codecs del sistema.
    //
    // MEDIDO en esta maquina: mp4 con AAC y webm con Opus, los dos salen
    // bien. Lo de webm va por las extensiones de medios que trae Windows 10;
    // si en alguna instalacion faltan, ese formato fallara y se dira en el
    // log. Los mp4, que es la inmensa mayoria de lo que se descarga, no
    // dependen de nada anadido.
    //
    // EL INTERFAZ ES FEO Y NO HAY REMEDIO. Media Foundation es COM: para
    // llamar al metodo numero 39 de una interfaz hay que declarar los 38 de
    // antes, aunque no se usen, porque lo que importa es la POSICION en la
    // tabla. De ahi los "Hueco" que se ven mas abajo: son ranuras, no codigo
    // muerto. Quitar una desplaza todas las siguientes y la llamada acaba en
    // el metodo equivocado, que en COM significa que el proceso se cae.
    public static class AudioVideo
    {
        public static readonly string[] Extensiones =
            { ".mp4", ".m4a", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".m4v" };

        // NORMALIZAR EL VOLUMEN. Un video descargado puede venir bajisimo o
        // pegado al techo, y la cancion generada desentona con el resto de la
        // biblioteca.
        //
        // Los numeros no son de catalogo: salen de medir 70 canciones de una
        // biblioteca real con volumedetect. Volumen medio mediano -14,1 dB
        // (p10 -17,9 / p90 -12,0) y pico mediano -1,1 dB. Asi que se apunta a
        // -14 dB de media con el pico sin pasar de -1, que ademas coincide con
        // lo que usan las plataformas de musica.
        //
        // Se ajusta por volumen MEDIO, no por pico. Normalizar por pico no
        // sirve de nada aqui: un solo golpe fuerte —un portazo, un grito— deja
        // el resto igual de bajo que estaba. El limite de pico esta solo para
        // no recortar la onda al subir.
        public const double MediaObjetivo = 0.1995;      // -14 dB
        public const double PicoMaximo = 0.891;          // -1 dB
        public const double GananciaMaxima = 32.0;       // +30 dB y no mas
        public const double CodoLimitador = 0.6;         // donde empieza a frenar

        private const uint VersionMf = 0x00020070;
        private const uint PrimeraPistaAudio = 0xFFFFFFFD;
        private const uint FinDeFlujo = 0x00000002;

        private static readonly Guid TipoAudio =
            new Guid("73647561-0000-0010-8000-00AA00389B71");
        private static readonly Guid FormatoPcm =
            new Guid("00000001-0000-0010-8000-00AA00389B71");
        private static readonly Guid ClaveTipoPrincipal =
            new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static readonly Guid ClaveSubtipo =
            new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static readonly Guid ClaveCanales =
            new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        private static readonly Guid ClaveFrecuencia =
            new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        private static readonly Guid ClaveBits =
            new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

        [DllImport("mfplat", ExactSpelling = true)]
        private static extern int MFStartup(uint version, uint banderas);

        [DllImport("mfplat", ExactSpelling = true)]
        private static extern int MFShutdown();

        [DllImport("mfplat", ExactSpelling = true)]
        private static extern int MFCreateMediaType(out IMFMediaType tipo);

        [DllImport("mfreadwrite", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int MFCreateSourceReaderFromURL(
            [MarshalAs(UnmanagedType.LPWStr)] string url,
            IntPtr atributos, out IMFSourceReader lector);

        private static bool arrancado;

        public static bool EsVideo(string ruta)
        {
            if (string.IsNullOrEmpty(ruta))
            {
                return false;
            }
            string ext = Path.GetExtension(ruta).ToLowerInvariant();
            return Array.IndexOf(Extensiones, ext) >= 0;
        }

        // Deja el audio del video en un .wav al lado. Hace falta porque el
        // juego no suena con la pista de un video: reproduce la imagen y nada
        // mas. Si la carpeta solo trae el video, sin esto la cancion se
        // jugaria en silencio.
        //
        // Se escribe segun va leyendo, sin juntarlo todo en memoria: un video
        // largo son cientos de megas de PCM.
        //
        // WAV y no ogg porque comprimir necesitaria una biblioteca que no
        // tenemos y no vamos a enviar. Cuesta disco —unos 10 MB por minuto en
        // estereo— y el juego lo reproduce igual.
        public static bool ExtraerWav(string video, string destino)
        {
            IMFSourceReader lector = null;
            FileStream f = null;
            try
            {
                int canales, frecuencia;
                if (!Abrir(video, out lector, out canales, out frecuencia))
                {
                    return false;
                }
                f = new FileStream(destino, FileMode.Create, FileAccess.Write);
                for (int i = 0; i < 44; i++)
                {
                    f.WriteByte(0);      // sitio para la cabecera
                }
                double sumaCuadrados;
                int pico;
                long datos = Copiar(lector, f, out sumaCuadrados, out pico);
                if (datos <= 0)
                {
                    return false;
                }
                f.Seek(0, SeekOrigin.Begin);
                Cabecera(f, canales, frecuencia, datos);
                f.Flush();
                f.Dispose();
                f = null;

                double ganancia = Ganancia(sumaCuadrados, pico, datos / 2);
                if (ganancia > 0 && Math.Abs(ganancia - 1.0) > 0.06)
                {
                    Normalizar(destino, ganancia);
                }
                MelonLogger.Msg("[Video] audio extraido: " + Path.GetFileName(destino)
                    + "  " + frecuencia.ToString() + " Hz x" + canales.ToString()
                    + "  " + (datos / (1024 * 1024)).ToString() + " MB"
                    + "  volumen x" + ganancia.ToString("0.00")
                    + " (" + (20.0 * Math.Log10(Math.Max(0.0001, ganancia))).ToString("+0.0;-0.0")
                    + " dB)");
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Video] extraer: " + ex.Message);
                return false;
            }
            finally
            {
                if (f != null) f.Dispose();
                Soltar(lector);
            }
        }

        // Mide mientras escribe: la suma de cuadrados y el pico salen gratis
        // aqui, y evitan tener que volver a decodificar el video solo para
        // saber como de fuerte sonaba.
        private static long Copiar(IMFSourceReader lector, Stream destino,
                                   out double sumaCuadrados, out int pico)
        {
            long total = 0;
            sumaCuadrados = 0;
            pico = 0;
            byte[] tampon = null;
            while (true)
            {
                uint flujo, banderas;
                long marca;
                IMFSample muestra;
                if (lector.ReadSample(PrimeraPistaAudio, 0, out flujo,
                                      out banderas, out marca, out muestra) < 0)
                {
                    break;
                }
                if ((banderas & FinDeFlujo) != 0)
                {
                    Soltar(muestra);
                    break;
                }
                if (muestra == null)
                {
                    continue;
                }
                IMFMediaBuffer bloque = null;
                try
                {
                    if (muestra.ConvertToContiguousBuffer(out bloque) < 0 || bloque == null)
                    {
                        continue;
                    }
                    IntPtr p;
                    int maximo, largo;
                    if (bloque.Lock(out p, out maximo, out largo) < 0)
                    {
                        continue;
                    }
                    try
                    {
                        if (tampon == null || tampon.Length < largo)
                        {
                            tampon = new byte[Math.Max(largo, 1 << 16)];
                        }
                        Marshal.Copy(p, tampon, 0, largo);
                        for (int i = 0; i + 1 < largo; i += 2)
                        {
                            int m = (short)(tampon[i] | (tampon[i + 1] << 8));
                            sumaCuadrados += (double)m * m;
                            int a = m < 0 ? -m : m;
                            if (a > pico) pico = a;
                        }
                        destino.Write(tampon, 0, largo);
                        total += largo;
                    }
                    finally
                    {
                        bloque.Unlock();
                    }
                }
                finally
                {
                    Soltar(bloque);
                    Soltar(muestra);
                }
            }
            return total;
        }

        // Cuanto hay que subir o bajar. Manda el volumen medio; el pico solo
        // pone el techo, para que subir no acabe recortando la onda.
        private static double Ganancia(double sumaCuadrados, int pico, long muestras)
        {
            if (muestras <= 0 || sumaCuadrados <= 0 || pico <= 0)
            {
                return 1.0;
            }
            double media = Math.Sqrt(sumaCuadrados / muestras) / 32768.0;
            if (media <= 0.0001)
            {
                return 1.0;      // practicamente silencio: mejor no tocarlo
            }
            // MANDA LA MEDIA, NO EL PICO. La primera version se quedaba con el
            // menor de los dos y el resultado no normalizaba nada: un audio
            // con picos altos y media baja solo subia 2 dB —de -19,8 a -17,8—
            // porque el techo de pico lo frenaba enseguida. Y uno muy flojo se
            // quedaba a 12 dB del objetivo por el tope de ganancia.
            //
            // Ahora se sube hasta donde pide la media y del pico se encarga el
            // limitador de mas abajo, que dobla suavemente lo que se pasa en
            // vez de cortarlo en seco.
            double g = MediaObjetivo / media;
            if (g > GananciaMaxima) g = GananciaMaxima;
            if (g < 1.0 / GananciaMaxima) g = 1.0 / GananciaMaxima;
            return g;
        }

        // Freno suave para lo que se sale. Por debajo del codo no toca nada;
        // por encima, la curva se va acercando al techo sin llegar nunca, asi
        // que no hay recorte. Cortar en seco en el techo mete distorsion que
        // se oye; esto no.
        private static double Limitar(double x)
        {
            double a = x < 0 ? -x : x;
            double codo = CodoLimitador * PicoMaximo;
            if (a <= codo)
            {
                return x;
            }
            double margen = PicoMaximo - codo;
            double doblado = codo + margen * Math.Tanh((a - codo) / margen);
            return x < 0 ? -doblado : doblado;
        }

        // Se aplica sobre el archivo ya escrito, en trozos. Hacerlo asi evita
        // tener la cancion entera en memoria y evita decodificar dos veces.
        private static void Normalizar(string ruta, double ganancia)
        {
            try
            {
                using (FileStream f = new FileStream(ruta, FileMode.Open, FileAccess.ReadWrite))
                {
                    byte[] trozo = new byte[1 << 16];
                    long pos = 44;      // detras de la cabecera
                    f.Seek(pos, SeekOrigin.Begin);
                    while (true)
                    {
                        int leidos = f.Read(trozo, 0, trozo.Length);
                        if (leidos <= 0)
                        {
                            break;
                        }
                        for (int i = 0; i + 1 < leidos; i += 2)
                        {
                            int m = (short)(trozo[i] | (trozo[i + 1] << 8));
                            int v = (int)Math.Round(Limitar(m / 32768.0 * ganancia) * 32767.0);
                            if (v > 32767) v = 32767;
                            if (v < -32768) v = -32768;
                            trozo[i] = (byte)(v & 0xFF);
                            trozo[i + 1] = (byte)((v >> 8) & 0xFF);
                        }
                        f.Seek(pos, SeekOrigin.Begin);
                        f.Write(trozo, 0, leidos);
                        pos += leidos;
                        f.Seek(pos, SeekOrigin.Begin);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Video] normalizar: " + ex.Message);
            }
        }

        private static void Cabecera(Stream f, int canales, int frecuencia, long datos)
        {
            BinaryWriter w = new BinaryWriter(f);
            int porSegundo = frecuencia * canales * 2;
            w.Write(new char[] { 'R', 'I', 'F', 'F' });
            w.Write((uint)(36 + datos));
            w.Write(new char[] { 'W', 'A', 'V', 'E' });
            w.Write(new char[] { 'f', 'm', 't', ' ' });
            w.Write((uint)16);
            w.Write((ushort)1);                  // PCM sin comprimir
            w.Write((ushort)canales);
            w.Write((uint)frecuencia);
            w.Write((uint)porSegundo);
            w.Write((ushort)(canales * 2));
            w.Write((ushort)16);
            w.Write(new char[] { 'd', 'a', 't', 'a' });
            w.Write((uint)datos);
            w.Flush();
        }

        // Monta el lector y deja dicho con que formato va a entregar.
        private static bool Abrir(string ruta, out IMFSourceReader lector,
                                  out int canales, out int frecuencia)
        {
            lector = null;
            canales = 2;
            frecuencia = 44100;
            IMFMediaType salida = null;
            IMFMediaType actual = null;
            try
            {
                if (!arrancado)
                {
                    if (MFStartup(VersionMf, 0) < 0)
                    {
                        MelonLogger.Warning("[Video] Media Foundation no arranca");
                        return false;
                    }
                    arrancado = true;
                }
                if (MFCreateSourceReaderFromURL(ruta, IntPtr.Zero, out lector) < 0
                    || lector == null)
                {
                    MelonLogger.Warning("[Video] no se pudo abrir " + Path.GetFileName(ruta));
                    return false;
                }
                if (MFCreateMediaType(out salida) < 0 || salida == null)
                {
                    return false;
                }
                Guid g1 = ClaveTipoPrincipal, v1 = TipoAudio;
                Guid g2 = ClaveSubtipo, v2 = FormatoPcm;
                salida.SetGUID(ref g1, ref v1);
                salida.SetGUID(ref g2, ref v2);
                if (lector.SetCurrentMediaType(PrimeraPistaAudio, IntPtr.Zero, salida) < 0)
                {
                    MelonLogger.Warning("[Video] sin pista de audio utilizable en "
                        + Path.GetFileName(ruta));
                    return false;
                }
                if (lector.GetCurrentMediaType(PrimeraPistaAudio, out actual) < 0
                    || actual == null)
                {
                    return false;
                }
                Guid gc = ClaveCanales, gf = ClaveFrecuencia, gb = ClaveBits;
                uint tmp;
                if (actual.GetUINT32(ref gc, out tmp) >= 0) canales = (int)tmp;
                if (actual.GetUINT32(ref gf, out tmp) >= 0) frecuencia = (int)tmp;
                int bits = 16;
                if (actual.GetUINT32(ref gb, out tmp) >= 0) bits = (int)tmp;
                if (canales < 1) canales = 1;
                if (frecuencia < 8000) frecuencia = 44100;
                if (bits != 16)
                {
                    MelonLogger.Warning("[Video] el sistema devolvio " + bits.ToString()
                        + " bits y se esperaban 16");
                    return false;
                }
                return true;
            }
            finally
            {
                Soltar(actual);
                Soltar(salida);
            }
        }

        // Muestras mono a la frecuencia pedida, o null si Windows no sabe
        // abrir ese archivo.
        public static float[] LeerMono(string ruta, int frecuenciaDestino)
        {
            IMFSourceReader lector = null;
            IMFMediaType salida = null;
            IMFMediaType actual = null;
            try
            {
                if (!arrancado)
                {
                    int hr0 = MFStartup(VersionMf, 0);
                    if (hr0 < 0)
                    {
                        MelonLogger.Warning("[Video] Media Foundation no arranca: "
                            + hr0.ToString("X8"));
                        return null;
                    }
                    arrancado = true;
                }

                int hr = MFCreateSourceReaderFromURL(ruta, IntPtr.Zero, out lector);
                if (hr < 0 || lector == null)
                {
                    MelonLogger.Warning("[Video] no se pudo abrir " + Path.GetFileName(ruta)
                        + ": " + hr.ToString("X8"));
                    return null;
                }

                // Se pide PCM de 16 bits. El decodificador del sistema se
                // encarga de convertir lo que haya dentro.
                if (MFCreateMediaType(out salida) < 0 || salida == null)
                {
                    return null;
                }
                Guid g1 = ClaveTipoPrincipal, v1 = TipoAudio;
                Guid g2 = ClaveSubtipo, v2 = FormatoPcm;
                salida.SetGUID(ref g1, ref v1);
                salida.SetGUID(ref g2, ref v2);
                hr = lector.SetCurrentMediaType(PrimeraPistaAudio, IntPtr.Zero, salida);
                if (hr < 0)
                {
                    MelonLogger.Warning("[Video] sin pista de audio utilizable en "
                        + Path.GetFileName(ruta));
                    return null;
                }

                // Que frecuencia y cuantos canales han salido de verdad: no
                // tiene por que ser lo que traia el archivo.
                if (lector.GetCurrentMediaType(PrimeraPistaAudio, out actual) < 0
                    || actual == null)
                {
                    return null;
                }
                int canales = 2, frecuencia = 44100, bits = 16;
                Guid gc = ClaveCanales, gf = ClaveFrecuencia, gb = ClaveBits;
                uint tmp;
                if (actual.GetUINT32(ref gc, out tmp) >= 0) canales = (int)tmp;
                if (actual.GetUINT32(ref gf, out tmp) >= 0) frecuencia = (int)tmp;
                if (actual.GetUINT32(ref gb, out tmp) >= 0) bits = (int)tmp;
                if (canales < 1) canales = 1;
                if (frecuencia < 8000) frecuencia = 44100;
                if (bits != 16)
                {
                    MelonLogger.Warning("[Video] el sistema devolvio " + bits.ToString()
                        + " bits y se esperaban 16");
                    return null;
                }

                float[] mono = Volcar(lector, canales);
                if (mono == null || mono.Length < frecuencia / 4)
                {
                    return null;
                }
                return AudioBass.AjustarFrecuencia(mono, frecuencia, frecuenciaDestino);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Video] " + Path.GetFileName(ruta) + ": " + ex.Message);
                return null;
            }
            finally
            {
                Soltar(actual);
                Soltar(salida);
                Soltar(lector);
            }
        }

        // Se van pidiendo trozos hasta que se acaba el archivo. Cada trozo son
        // enteros de 16 bits intercalados por canal; aqui se pasan a un solo
        // canal y a float, que es lo que come el analisis.
        private static float[] Volcar(IMFSourceReader lector, int canales)
        {
            float[] salida = new float[1 << 20];
            int escritas = 0;
            byte[] tampon = null;

            while (true)
            {
                uint flujo, banderas;
                long marca;
                IMFSample muestra;
                int hr = lector.ReadSample(PrimeraPistaAudio, 0, out flujo,
                                           out banderas, out marca, out muestra);
                if (hr < 0)
                {
                    break;
                }
                if ((banderas & FinDeFlujo) != 0)
                {
                    Soltar(muestra);
                    break;
                }
                if (muestra == null)
                {
                    continue;      // hueco en el flujo; se sigue pidiendo
                }

                IMFMediaBuffer bloque = null;
                try
                {
                    if (muestra.ConvertToContiguousBuffer(out bloque) < 0 || bloque == null)
                    {
                        continue;
                    }
                    IntPtr p;
                    int maximo, largo;
                    if (bloque.Lock(out p, out maximo, out largo) < 0)
                    {
                        continue;
                    }
                    try
                    {
                        if (tampon == null || tampon.Length < largo)
                        {
                            tampon = new byte[Math.Max(largo, 1 << 16)];
                        }
                        Marshal.Copy(p, tampon, 0, largo);
                        int cuadros = largo / (2 * canales);
                        if (escritas + cuadros > salida.Length)
                        {
                            Array.Resize(ref salida,
                                Math.Max(salida.Length * 2, escritas + cuadros));
                        }
                        for (int c = 0; c < cuadros; c++)
                        {
                            int suma = 0;
                            int b = c * 2 * canales;
                            for (int k = 0; k < canales; k++)
                            {
                                suma += (short)(tampon[b + k * 2] | (tampon[b + k * 2 + 1] << 8));
                            }
                            salida[escritas + c] = suma / (float)canales / 32768f;
                        }
                        escritas += cuadros;
                    }
                    finally
                    {
                        bloque.Unlock();
                    }
                }
                finally
                {
                    Soltar(bloque);
                    Soltar(muestra);
                }
            }
            if (escritas == 0)
            {
                return null;
            }
            Array.Resize(ref salida, escritas);
            return salida;
        }

        private static void Soltar(object com)
        {
            try
            {
                if (com != null && Marshal.IsComObject(com))
                {
                    Marshal.ReleaseComObject(com);
                }
            }
            catch (Exception)
            {
            }
        }

        // ----------------------------------------------------------- interfaces
        //
        // Los "Hueco" son ranuras de la tabla de metodos. Hay que declararlas
        // para que las de despues caigan en su sitio; no se llaman nunca.

        [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSourceReader
        {
            [PreserveSig] int GetStreamSelection(uint flujo, out bool elegido);
            [PreserveSig] int SetStreamSelection(uint flujo, bool elegido);
            [PreserveSig] int GetNativeMediaType(uint flujo, uint indice, out IMFMediaType tipo);
            [PreserveSig] int GetCurrentMediaType(uint flujo, out IMFMediaType tipo);
            [PreserveSig] int SetCurrentMediaType(uint flujo, IntPtr reservado, IMFMediaType tipo);
            [PreserveSig] int SetCurrentPosition(ref Guid formato, IntPtr posicion);
            [PreserveSig] int ReadSample(uint flujo, uint banderas, out uint flujoReal,
                out uint banderasSalida, out long marca, out IMFSample muestra);
            [PreserveSig] int Flush(uint flujo);
            [PreserveSig] int GetServiceForStream(uint flujo, ref Guid servicio,
                ref Guid interfaz, out IntPtr objeto);
            [PreserveSig] int GetPresentationAttribute(uint flujo, ref Guid clave, IntPtr valor);
        }

        [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaType
        {
            // --- IMFAttributes: 30 ranuras
            [PreserveSig] int Hueco01();
            [PreserveSig] int Hueco02();
            [PreserveSig] int Hueco03();
            [PreserveSig] int Hueco04();
            [PreserveSig] int GetUINT32(ref Guid clave, out uint valor);
            [PreserveSig] int Hueco06();
            [PreserveSig] int Hueco07();
            [PreserveSig] int Hueco08();
            [PreserveSig] int Hueco09();
            [PreserveSig] int Hueco10();
            [PreserveSig] int Hueco11();
            [PreserveSig] int Hueco12();
            [PreserveSig] int Hueco13();
            [PreserveSig] int Hueco14();
            [PreserveSig] int Hueco15();
            [PreserveSig] int Hueco16();
            [PreserveSig] int Hueco17();
            [PreserveSig] int Hueco18();
            [PreserveSig] int SetUINT32(ref Guid clave, uint valor);
            [PreserveSig] int Hueco20();
            [PreserveSig] int Hueco21();
            [PreserveSig] int SetGUID(ref Guid clave, ref Guid valor);
            [PreserveSig] int Hueco23();
            [PreserveSig] int Hueco24();
            [PreserveSig] int Hueco25();
            [PreserveSig] int Hueco26();
            [PreserveSig] int Hueco27();
            [PreserveSig] int Hueco28();
            [PreserveSig] int Hueco29();
            [PreserveSig] int Hueco30();
            // --- lo propio de IMFMediaType
            [PreserveSig] int GetMajorType(out Guid principal);
        }

        [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSample
        {
            // --- IMFAttributes: 30 ranuras
            [PreserveSig] int Hueco01();
            [PreserveSig] int Hueco02();
            [PreserveSig] int Hueco03();
            [PreserveSig] int Hueco04();
            [PreserveSig] int Hueco05();
            [PreserveSig] int Hueco06();
            [PreserveSig] int Hueco07();
            [PreserveSig] int Hueco08();
            [PreserveSig] int Hueco09();
            [PreserveSig] int Hueco10();
            [PreserveSig] int Hueco11();
            [PreserveSig] int Hueco12();
            [PreserveSig] int Hueco13();
            [PreserveSig] int Hueco14();
            [PreserveSig] int Hueco15();
            [PreserveSig] int Hueco16();
            [PreserveSig] int Hueco17();
            [PreserveSig] int Hueco18();
            [PreserveSig] int Hueco19();
            [PreserveSig] int Hueco20();
            [PreserveSig] int Hueco21();
            [PreserveSig] int Hueco22();
            [PreserveSig] int Hueco23();
            [PreserveSig] int Hueco24();
            [PreserveSig] int Hueco25();
            [PreserveSig] int Hueco26();
            [PreserveSig] int Hueco27();
            [PreserveSig] int Hueco28();
            [PreserveSig] int Hueco29();
            [PreserveSig] int Hueco30();
            // --- lo propio de IMFSample
            [PreserveSig] int GetSampleFlags(out uint banderas);
            [PreserveSig] int SetSampleFlags(uint banderas);
            [PreserveSig] int GetSampleTime(out long marca);
            [PreserveSig] int SetSampleTime(long marca);
            [PreserveSig] int GetSampleDuration(out long duracion);
            [PreserveSig] int SetSampleDuration(long duracion);
            [PreserveSig] int GetBufferCount(out uint cuantos);
            [PreserveSig] int GetBufferByIndex(uint indice, out IMFMediaBuffer bloque);
            [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer bloque);
        }

        [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaBuffer
        {
            [PreserveSig] int Lock(out IntPtr datos, out int maximo, out int largo);
            [PreserveSig] int Unlock();
            [PreserveSig] int GetCurrentLength(out int largo);
            [PreserveSig] int SetCurrentLength(int largo);
            [PreserveSig] int GetMaxLength(out int largo);
        }
    }
}
