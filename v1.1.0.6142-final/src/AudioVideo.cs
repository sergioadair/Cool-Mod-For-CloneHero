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
                long datos = Copiar(lector, f);
                if (datos <= 0)
                {
                    return false;
                }
                f.Seek(0, SeekOrigin.Begin);
                Cabecera(f, canales, frecuencia, datos);
                f.Flush();
                MelonLogger.Msg("[Video] audio extraido: " + Path.GetFileName(destino)
                    + "  " + frecuencia.ToString() + " Hz x" + canales.ToString()
                    + "  " + (datos / (1024 * 1024)).ToString() + " MB");
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

        private static long Copiar(IMFSourceReader lector, Stream destino)
        {
            long total = 0;
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
