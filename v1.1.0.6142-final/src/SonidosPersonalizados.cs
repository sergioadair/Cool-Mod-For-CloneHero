using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MelonLoader;

namespace CloneHeroMod
{
    // Reproduce archivos de audio propios desde PlayerData\Custom\Sounds.
    //
    // Se usa el BASS del propio juego en vez del audio de Unity porque BASS
    // reproduce .opus, que Unity no soporta de forma nativa (solo ogg, wav y
    // mp3). La clase y los metodos conservan los mismos nombres ofuscados que
    // en la v1.0, asi que el mapa de entonces sigue valiendo:
    //
    //   ʷʹʹʻʳʶʶʶʶˁʿ            -> Bass
    //   ʶʲʴʽˁʾʿʵʲʼʶ(string...) -> SampleLoad
    //   ʲʸʿʿʷʵˀˁʵʻʼ            -> SampleGetChannel
    //   ˀʲʿˀʾʽʳʸʾʲʳ            -> ChannelPlay
    //   ʵʴˀˁʹʼʽʽʵʻʻ            -> ChannelSetAttribute
    public static class SonidosPersonalizados
    {
        private const string TipoBass = "ʷʹʹʻʳʶʶʶʶˁʿ";

        private static readonly string[] Extensiones = { ".opus", ".ogg", ".mp3", ".wav" };

        private static MethodInfo cargarMuestra;   // SampleLoad(string, long, int, int, flags)
        private static MethodInfo obtenerCanal;    // SampleGetChannel(int, bool)
        private static MethodInfo reproducir;      // ChannelPlay(int, bool)
        private static bool resuelto;
        private static bool disponible;

        private static readonly Dictionary<string, int> muestras =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static string Carpeta
        {
            get { return RutasJuego.CarpetaCustom("Sounds"); }
        }

        // ------------------------------------------------------------ resolver
        private static bool Resolver()
        {
            if (resuelto)
            {
                return disponible;
            }
            resuelto = true;
            try
            {
                Type t = Ofuscado.Tipo(TipoBass);
                if (t == null)
                {
                    MelonLogger.Warning("[Sonidos] no se localizo BASS");
                    return false;
                }
                MethodInfo[] ms = t.GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < ms.Length; i++)
                {
                    MethodInfo m = ms[i];
                    ParameterInfo[] ps = m.GetParameters();

                    // SampleLoad: (string ruta, long, int, int, flags) -> int
                    if (cargarMuestra == null && m.ReturnType == typeof(int) && ps.Length == 5
                        && ps[0].ParameterType == typeof(string)
                        && ps[1].ParameterType == typeof(long))
                    {
                        cargarMuestra = m;
                    }
                    // SampleGetChannel: (int, bool) -> int
                    else if (obtenerCanal == null && m.ReturnType == typeof(int) && ps.Length == 2
                             && ps[0].ParameterType == typeof(int)
                             && ps[1].ParameterType == typeof(bool))
                    {
                        obtenerCanal = m;
                    }
                    // ChannelPlay: (int, bool) -> bool
                    else if (reproducir == null && m.ReturnType == typeof(bool) && ps.Length == 2
                             && ps[0].ParameterType == typeof(int)
                             && ps[1].ParameterType == typeof(bool))
                    {
                        reproducir = m;
                    }
                }
                disponible = cargarMuestra != null && obtenerCanal != null && reproducir != null;
                MelonLogger.Msg("[Sonidos] BASS " + (disponible ? "listo" : "incompleto")
                    + "  load=" + (cargarMuestra != null ? "ok" : "NO")
                    + "  canal=" + (obtenerCanal != null ? "ok" : "NO")
                    + "  play=" + (reproducir != null ? "ok" : "NO"));
                return disponible;
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Sonidos] " + ex);
                return false;
            }
        }

        // Sin esto el sonido sale al 100 %, muy por encima del resto del juego.
        // BassAudioManager tiene un escalador que aplica el volumen maestro y
        // el de efectos; es el mismo que usa el juego para sus propios sonidos.
        private static MethodInfo ponerAtributo;   // ChannelSetAttribute(int, attr, float)
        private static object atributoVolumen;
        private static MethodInfo escalarVolumen;  // instancia: float -> float
        private static PropertyInfo instanciaAudio;
        private static bool volumenResuelto;
        private static bool avisadoVolumen;
        private static bool usaDouble;

        private static void AjustarVolumen(int canal)
        {
            try
            {
                if (!ResolverVolumen())
                {
                    return;
                }
                float escalado = 1f;
                object inst = instanciaAudio != null ? instanciaAudio.GetValue(null) : null;
                if (inst != null && escalarVolumen != null)
                {
                    object r = escalarVolumen.Invoke(inst, new object[] { 1f });
                    if (r is float f)
                    {
                        escalado = f;
                    }
                }
                // El escalador del juego resulto no bastar (devolvia practicamente
                // 1), asi que el volumen final se multiplica ademas por un factor
                // ajustable en settings.ini: [mods] finished_song_sfx_volume.
                float v = escalado * Ajustes.VolumenSfx;
                if (v < 0f) { v = 0f; }
                if (v > 1f) { v = 1f; }

                volumenPendiente = v;
                // El valor se pasa con el tipo exacto que espera la sobrecarga:
                // por reflexion, un float donde se espera double no encaja.
                object valor = usaDouble ? (object)(double)v : (object)v;
                object ok = ponerAtributo.Invoke(null,
                    new object[] { canal, atributoVolumen, valor });
                bool aplicado = ok is bool b && b;
                if (!avisadoVolumen)
                {
                    avisadoVolumen = true;
                    MelonLogger.Msg("[Sonidos] volumen: escalador=" + escalado.ToString("0.###")
                        + "  factor=" + Ajustes.VolumenSfx.ToString("0.###")
                        + "  final=" + v.ToString("0.###")
                        + "  aplicado=" + (aplicado ? "si" : "NO")
                        + "  atributo=" + Convert.ToInt32(atributoVolumen).ToString()
                        + " (" + atributoVolumen.ToString() + ")  tipo=" + (usaDouble ? "double" : "float"));
                    if (!aplicado)
                    {
                        VolcarAtributos();
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Sonidos] volumen: " + ex.Message);
            }
        }

        private static float volumenPendiente = 1f;

        private static void ReintentarVolumen(int canal)
        {
            try
            {
                if (ponerAtributo == null || atributoVolumen == null)
                {
                    return;
                }
                object v2 = usaDouble
                    ? (object)(double)volumenPendiente
                    : (object)volumenPendiente;
                object ok = ponerAtributo.Invoke(null,
                    new object[] { canal, atributoVolumen, v2 });
                if (!avisadoReintento)
                {
                    avisadoReintento = true;
                    MelonLogger.Msg("[Sonidos] reintento tras reproducir: "
                        + (ok is bool b && b ? "si" : "NO"));
                }
            }
            catch (Exception)
            {
            }
        }

        private static bool avisadoReintento;

        // Si el atributo de volumen falla, esto deja ver que miembros tiene el
        // enum y con que numero, para dar con el correcto.
        private static void VolcarAtributos()
        {
            try
            {
                Type tEnum = ponerAtributo.GetParameters()[1].ParameterType;
                string[] nombres = Enum.GetNames(tEnum);
                Array valores = Enum.GetValues(tEnum);
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("[Sonidos] atributos del enum (").Append(nombres.Length).Append("): ");
                for (int i = 0; i < nombres.Length && i < 20; i++)
                {
                    sb.Append(nombres[i]).Append('=')
                      .Append(Convert.ToInt32(valores.GetValue(i))).Append("  ");
                }
                MelonLogger.Msg(sb.ToString());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Sonidos] volcar atributos: " + ex.Message);
            }
        }

        private static bool ResolverVolumen()
        {
            if (volumenResuelto)
            {
                return ponerAtributo != null && atributoVolumen != null;
            }
            volumenResuelto = true;
            try
            {
                // ChannelSetAttribute: (int canal, enum atributo, float valor)
                Type tBass = Ofuscado.Tipo(TipoBass);
                MethodInfo[] ms = tBass.GetMethods(BindingFlags.Public | BindingFlags.Static);
                // Hay tres sobrecargas de ChannelSetAttribute: (int, attr,
                // double), (int, attr, IntPtr, int) y (int, attr, float). Se
                // prefiere la de DOUBLE: es el envoltorio gestionado, mientras
                // que la de float es extern (P/Invoke crudo) y devolvia false.
                MethodInfo porFloat = null;
                for (int i = 0; i < ms.Length; i++)
                {
                    ParameterInfo[] ps = ms[i].GetParameters();
                    if (ms[i].ReturnType != typeof(bool) || ps.Length != 3
                        || ps[0].ParameterType != typeof(int)
                        || !ps[1].ParameterType.IsEnum)
                    {
                        continue;
                    }
                    if (ps[2].ParameterType == typeof(float))
                    {
                        porFloat = ms[i];
                        continue;
                    }
                    if (ps[2].ParameterType == typeof(double))
                    {
                        ponerAtributo = ms[i];
                        usaDouble = true;
                        try
                        {
                            atributoVolumen = Enum.Parse(ps[1].ParameterType, "Volume");
                        }
                        catch (Exception)
                        {
                            // Si el nombre no sobrevivio, Volume vale 2 en BASS.
                            atributoVolumen = Enum.ToObject(ps[1].ParameterType, 2);
                        }
                        break;
                    }
                }
                // Si no existe la de double, se usa la de float.
                if (ponerAtributo == null && porFloat != null)
                {
                    ponerAtributo = porFloat;
                    usaDouble = false;
                    ParameterInfo[] ps = porFloat.GetParameters();
                    try
                    {
                        atributoVolumen = Enum.Parse(ps[1].ParameterType, "Volume");
                    }
                    catch (Exception)
                    {
                        atributoVolumen = Enum.ToObject(ps[1].ParameterType, 2);
                    }
                }

                Type tGestor = Ofuscado.Tipo("BassAudioManager");
                if (tGestor != null)
                {
                    instanciaAudio = tGestor.GetProperty("instance",
                        BindingFlags.Public | BindingFlags.Static);
                    MethodInfo[] gm = tGestor.GetMethods(
                        BindingFlags.Public | BindingFlags.Instance);
                    for (int i = 0; i < gm.Length; i++)
                    {
                        ParameterInfo[] ps = gm[i].GetParameters();
                        if (gm[i].ReturnType == typeof(float) && ps.Length == 1
                            && ps[0].ParameterType == typeof(float)
                            && gm[i].Name.StartsWith("Method_Public_"))
                        {
                            escalarVolumen = gm[i];
                            break;
                        }
                    }
                }
                MelonLogger.Msg("[Sonidos] volumen  atributo=" + (ponerAtributo != null ? "ok" : "NO")
                    + "  escalador=" + (escalarVolumen != null ? "ok" : "NO")
                    + "  instancia=" + (instanciaAudio != null ? "ok" : "NO"));
                return ponerAtributo != null && atributoVolumen != null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Sonidos] resolver volumen: " + ex.Message);
                return false;
            }
        }

        // Busca el archivo por nombre, probando las extensiones conocidas.
        public static string Localizar(string nombre)
        {
            try
            {
                if (string.IsNullOrEmpty(nombre))
                {
                    return null;
                }
                if (File.Exists(nombre))
                {
                    return nombre;
                }
                string carpeta = Carpeta;
                string directo = Path.Combine(carpeta, nombre);
                if (File.Exists(directo))
                {
                    return directo;
                }
                for (int i = 0; i < Extensiones.Length; i++)
                {
                    string con = directo + Extensiones[i];
                    if (File.Exists(con))
                    {
                        return con;
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        // Reproduce el sonido. Devuelve false si no se pudo (archivo ausente,
        // BASS no disponible...); nunca lanza.
        public static bool Reproducir(string nombre)
        {
            try
            {
                if (!Resolver())
                {
                    return false;
                }
                string ruta = Localizar(nombre);
                if (ruta == null)
                {
                    MelonLogger.Warning("[Sonidos] no se encontro '" + nombre + "' en " + Carpeta);
                    return false;
                }

                int muestra;
                if (!muestras.TryGetValue(ruta, out muestra) || muestra == 0)
                {
                    // (ruta, offset 0, longitud 0, polifonia 4, flags 0)
                    object flags = Activator.CreateInstance(
                        cargarMuestra.GetParameters()[4].ParameterType);
                    object r = cargarMuestra.Invoke(null,
                        new object[] { ruta, 0L, 0, 4, flags });
                    muestra = r is int ? (int)r : 0;
                    if (muestra == 0)
                    {
                        MelonLogger.Warning("[Sonidos] BASS no pudo cargar " + ruta);
                        return false;
                    }
                    muestras[ruta] = muestra;
                    MelonLogger.Msg("[Sonidos] cargado " + Path.GetFileName(ruta));
                }

                object c = obtenerCanal.Invoke(null, new object[] { muestra, false });
                int canal = c is int ? (int)c : 0;
                if (canal == 0)
                {
                    return false;
                }
                AjustarVolumen(canal);
                reproducir.Invoke(null, new object[] { canal, false });
                // Reintento DESPUES de arrancar: algunos canales de muestra no
                // aceptan atributos hasta que estan sonando, y en la primera
                // prueba ChannelSetAttribute devolvia false.
                ReintentarVolumen(canal);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Sonidos] " + ex.Message);
                return false;
            }
        }
    }
}
