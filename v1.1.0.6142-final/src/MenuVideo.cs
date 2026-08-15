using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;

namespace CloneHeroMod
{
    // Dos anadidos a Settings > Video:
    //
    //   1. El nombre del archivo cuando el fondo elegido es uno nuestro. El
    //      juego no tiene texto para indices que no conoce, asi que esas
    //      opciones salian en blanco.
    //   2. La fila "Menu BG Slideshow", que hasta ahora solo existia en
    //      settings.ini.
    //
    // El estado del slideshow va en el propio texto de la fila
    // ("Menu BG Slideshow: Yes") en vez de en un widget Yes/No aparte: el juego
    // no tiene hueco de valor para una fila que no conoce, y crear uno a mano
    // fue justo lo que descoloco el menu de Audio en la version anterior.
    public static class MenuVideo
    {
        public const string PrefijoSlideshow = "Menu BG Slideshow";
        public const string OpcionFondos = "Menu Backgrounds";

        // Nombres de los 14 fondos de serie. Sirven para localizar el widget de
        // valor de la fila "Menu Backgrounds": es el unico que muestra uno de
        // estos textos.
        private static readonly string[] NombresDeSerie =
        {
            "Surfer", "Classic", "Dark", "Light", "Autumn", "Alien", "Blue Rays",
            "Grains", "Groovy", "Pastel Burst", "Rainbow", "Spray", "Default"
        };

        private static bool parcheado;

        public static void InstalarParches(HarmonyLib.Harmony harmony)
        {
            if (parcheado)
            {
                return;
            }
            parcheado = true;
            try
            {
                Type tBase = Ofuscado.Tipo("BaseSettingMenu");
                Type tVideo = Ofuscado.Tipo("VideoSettingsMenu");
                if (tBase == null || tVideo == null)
                {
                    MelonLogger.Warning("[MenuVideo] tipos no localizados");
                    return;
                }

                MethodInfo onEnable = tBase.GetMethod("OnEnable",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (onEnable != null)
                {
                    harmony.Patch(onEnable, new HarmonyMethod(
                        typeof(MenuVideo).GetMethod("PreOnEnable",
                            BindingFlags.NonPublic | BindingFlags.Static)), null);
                }

                // El metodo de etiquetas: unico "public virtual void (string)".
                MethodInfo etiquetas = null;
                MethodInfo[] ms = tVideo.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].ReturnType != typeof(void) || !ms[i].IsVirtual
                        || ms[i].IsSpecialName || !ms[i].Name.StartsWith("Method_Public_"))
                    {
                        continue;
                    }
                    ParameterInfo[] ps = ms[i].GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                    {
                        etiquetas = ms[i];
                        break;
                    }
                }
                if (etiquetas != null)
                {
                    harmony.Patch(etiquetas, null, new HarmonyMethod(
                        typeof(MenuVideo).GetMethod("PostEtiquetas",
                            BindingFlags.NonPublic | BindingFlags.Static)));
                    MelonLogger.Msg("[MenuVideo] etiquetas parcheadas: " + etiquetas.Name);
                }
                else
                {
                    MelonLogger.Warning("[MenuVideo] no se localizo el metodo de etiquetas");
                }

                // Select: NO se eligen por nombre. Los nombres que genera
                // Il2CppInterop van numerados por clase, asi que el Select de
                // Video no tiene por que llamarse igual que el de General; de
                // hecho por nombre caian 5 candidatos y el unico que se
                // disparaba corria en cada fotograma.
                //
                // Se emparejan por RANURA VIRTUAL: se toman los candidatos de
                // GeneralSettingsMenu (donde el Select si funciona, es el que
                // activa Calculate Difficulty), se coge su declaracion base con
                // GetBaseDefinition() y se parchea en Video el override de esa
                // misma ranura.
                Type tGeneral = Ofuscado.Tipo("GeneralSettingsMenu");
                MethodInfo postSelect = typeof(MenuVideo).GetMethod("PostSelect",
                    BindingFlags.NonPublic | BindingFlags.Static);
                int n = 0;
                if (tGeneral != null)
                {
                    List<MethodInfo> basesBuenas = new List<MethodInfo>();
                    MethodInfo[] gs = tGeneral.GetMethods(
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    for (int i = 0; i < gs.Length; i++)
                    {
                        if (EsCandidatoSelect(gs[i]))
                        {
                            basesBuenas.Add(gs[i].GetBaseDefinition());
                        }
                    }
                    for (int i = 0; i < ms.Length; i++)
                    {
                        if (!EsCandidatoSelect(ms[i]))
                        {
                            continue;
                        }
                        MethodInfo baseV = ms[i].GetBaseDefinition();
                        bool coincide = false;
                        for (int j = 0; j < basesBuenas.Count; j++)
                        {
                            if (basesBuenas[j] == baseV)
                            {
                                coincide = true;
                                break;
                            }
                        }
                        if (!coincide)
                        {
                            continue;
                        }
                        try
                        {
                            harmony.Patch(ms[i], null, new HarmonyMethod(postSelect));
                            n++;
                            MelonLogger.Msg("[MenuVideo] Select emparejado: " + ms[i].Name);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
                MelonLogger.Msg("[MenuVideo] candidatos a Select parcheados: " + n.ToString());
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[MenuVideo] " + ex);
            }
        }

        // ---------------------------------------------------------------------
        private static object menuVideo;

        private static void PreOnEnable(Il2CppSystem.Object __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }
                Il2Cpp.VideoSettingsMenu v = __instance.TryCast<Il2Cpp.VideoSettingsMenu>();
                if (v == null)
                {
                    return;
                }
                menuVideo = v;
                // Prefix, igual que en el menu General: OnEnable calcula los
                // limites de navegacion, y si la fila se anade despues queda
                // visible pero inalcanzable.
                FilasMenu.Anadir(v, TextoSlideshow(), PrefijoSlideshow);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[MenuVideo] PreOnEnable: " + ex);
            }
        }

        private static string TextoSlideshow()
        {
            return PrefijoSlideshow + ": " + (Ajustes.SlideshowActivo ? "Yes" : "No");
        }

        // Tras refrescar etiquetas: pone el nombre del archivo si el fondo
        // elegido es uno nuestro.
        private static void PostEtiquetas()
        {
            try
            {
                if (menuVideo == null)
                {
                    return;
                }
                VolcarDropdowns();
                string nombre = FondosPersonalizados.NombreDelSeleccionado();
                if (nombre == null)
                {
                    return;      // fondo de serie: lo pone el juego
                }
                Il2CppTMPro.TextMeshProUGUI destino = WidgetDeFondos();
                if (destino != null)
                {
                    destino.text = nombre;
                }
            }
            catch (Exception)
            {
            }
        }

        // El widget de valor de "Menu Backgrounds" es el unico de los dropdowns
        // cuyo texto es uno de los nombres de fondo de serie (el juego deja el
        // anterior cuando el indice no lo conoce).
        private static Il2CppTMPro.TextMeshProUGUI WidgetDeFondos()
        {
            PropertyInfo p = FilasMenu.Prop(menuVideo.GetType(), "dropdowns");
            if (p == null)
            {
                return null;
            }
            object arr = p.GetValue(menuVideo);
            if (arr == null)
            {
                return null;
            }
            PropertyInfo len = arr.GetType().GetProperty("Length");
            PropertyInfo idx = arr.GetType().GetProperty("Item");
            int n = (int)len.GetValue(arr);
            if (widgetFondos >= 0 && widgetFondos < n)
            {
                return idx.GetValue(arr, new object[] { widgetFondos }) as Il2CppTMPro.TextMeshProUGUI;
            }

            // Los dropdowns NO estan indexados por fila: son 7 para 19 filas.
            // Primero se intenta reconocer el widget porque muestre un nombre
            // de fondo de serie...
            for (int i = 0; i < n; i++)
            {
                var t = idx.GetValue(arr, new object[] { i }) as Il2CppTMPro.TextMeshProUGUI;
                if (t == null || string.IsNullOrEmpty(t.text))
                {
                    continue;
                }
                for (int j = 0; j < NombresDeSerie.Length; j++)
                {
                    if (t.text == NombresDeSerie[j])
                    {
                        widgetFondos = i;
                        MelonLogger.Msg("[MenuVideo] widget de fondos: [" + i.ToString() + "]");
                        return t;
                    }
                }
            }

            // ...y si no, por descarte: con un fondo nuestro seleccionado el
            // juego no sabe que texto poner y lo deja VACIO. Si hay exactamente
            // uno vacio, ese es.
            int vacio = -1;
            int cuantosVacios = 0;
            for (int i = 0; i < n; i++)
            {
                var t = idx.GetValue(arr, new object[] { i }) as Il2CppTMPro.TextMeshProUGUI;
                if (t != null && string.IsNullOrEmpty(t.text))
                {
                    vacio = i;
                    cuantosVacios++;
                }
            }
            if (cuantosVacios == 1)
            {
                widgetFondos = vacio;
                MelonLogger.Msg("[MenuVideo] widget de fondos (por descarte): ["
                                + vacio.ToString() + "]");
                return idx.GetValue(arr, new object[] { vacio }) as Il2CppTMPro.TextMeshProUGUI;
            }
            return null;
        }

        private static int widgetFondos = -1;
        private static bool dropdownsVolcados;

        // Los widgets de valor con su indice y su texto: sin esto no hay forma
        // de saber cual corresponde a "Menu Backgrounds".
        private static void VolcarDropdowns()
        {
            try
            {
                if (dropdownsVolcados)
                {
                    return;
                }
                PropertyInfo p = FilasMenu.Prop(menuVideo.GetType(), "dropdowns");
                if (p == null)
                {
                    MelonLogger.Warning("[MenuVideo] no hay propiedad dropdowns");
                    dropdownsVolcados = true;
                    return;
                }
                object arr = p.GetValue(menuVideo);
                if (arr == null)
                {
                    return;
                }
                dropdownsVolcados = true;
                PropertyInfo len = arr.GetType().GetProperty("Length");
                PropertyInfo idx = arr.GetType().GetProperty("Item");
                int n = (int)len.GetValue(arr);
                MelonLogger.Msg("[MenuVideo] dropdowns: " + n.ToString());
                for (int i = 0; i < n; i++)
                {
                    var t = idx.GetValue(arr, new object[] { i }) as Il2CppTMPro.TextMeshProUGUI;
                    MelonLogger.Msg("[MenuVideo]   [" + i.ToString() + "] "
                        + (t == null ? "(null)" : "'" + t.text + "'  obj=" + t.gameObject.name));
                }

                Il2CppStringArray filas = FilasMenu.Opciones(menuVideo);
                if (filas != null)
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder("[MenuVideo] filas: ");
                    for (int i = 0; i < filas.Length; i++)
                    {
                        sb.Append(i).Append('=').Append(filas[i]).Append(" | ");
                    }
                    MelonLogger.Msg(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[MenuVideo] volcar dropdowns: " + ex.Message);
            }
        }

        // Al activar la fila del slideshow, se conmuta y se reescribe su texto.
        private static string quienLlama = "?";

        private static void PostSelect(MethodBase __originalMethod)
        {
            try
            {
                quienLlama = __originalMethod != null ? __originalMethod.Name : "?";
                if (menuVideo == null)
                {
                    return;
                }
                string actual = OpcionResaltada();
                if (actual == null || !actual.StartsWith(PrefijoSlideshow, StringComparison.Ordinal))
                {
                    return;
                }
                // Se comporta como cualquier ajuste del juego: hay que PULSAR la
                // opcion y entonces moverse cambia el valor. Solo pasar por
                // encima no debe tocarla.
                //
                // La diferencia esta en la propiedad "opcion abierta" del menu,
                // que es nula mientras la fila solo esta resaltada. No se sabe
                // de antemano cual de las propiedades string es: se aprende
                // sola, marcando las que alguna vez se han visto vacias
                // (la de la fila resaltada nunca lo esta dentro del menu).
                // Conmuta con la opcion ABIERTA: se pulsa y luego se mueve.
                //
                // Se intento igualarlo al menu de Audio (conmutar solo al
                // pulsar) detectando la transicion de cerrada a abierta, y
                // quedo peor: en Video la propiedad de "abierta" todavia no
                // esta puesta cuando corre el postfix de la pulsacion, asi que
                // la transicion se detectaba en el primer movimiento y de forma
                // intermitente. Los dos menus entregan eventos distintos y no
                // se pueden unificar; cada uno se queda con lo que funciona.
                if (!EstaAbierta())
                {
                    return;
                }

                // Guarda contra rebotes por si el metodo se dispara mas de una
                // vez por pulsacion.
                float ahora = UnityEngine.Time.realtimeSinceStartup;
                if (ahora - ultimaConmutacion < 0.25f)
                {
                    return;
                }
                ultimaConmutacion = ahora;

                bool nuevo = !Ajustes.SlideshowActivo;
                Ajustes.GuardarSlideshow(nuevo);
                int fila = FilasMenu.IndiceDe(menuVideo, PrefijoSlideshow);
                FilasMenu.CambiarTexto(menuVideo, fila, TextoSlideshow());
                RefrescarFila(fila);
            }
            catch (Exception)
            {
            }
        }

        private static float ultimaConmutacion;
        private static readonly Dictionary<string, int> ultimoFrame = new Dictionary<string, int>();
        private static readonly List<string> vetados = new List<string>();

        // Firma comun de los candidatos a Select.
        private static bool EsCandidatoSelect(MethodInfo m)
        {
            if (m.ReturnType != typeof(void) || m.GetParameters().Length != 0
                || !m.IsVirtual || m.IsSpecialName || !m.Name.StartsWith("Method_Public_"))
            {
                return false;
            }
            string nm = m.Name;
            return nm != "Update" && nm != "Start" && nm != "Awake"
                && nm != "OnEnable" && nm != "OnDisable" && nm != "OnDestroy";
        }

        // Propiedades string del menu que en algun momento se han visto vacias:
        // ahi esta la de "opcion abierta". La de la fila resaltada nunca lo
        // esta mientras el menu se ve, asi que queda descartada sola.
        private static PropertyInfo[] candidatasAbierta;
        private static readonly List<string> vistasVacias = new List<string>();

        private static bool EstaAbierta()
        {
            try
            {
                if (candidatasAbierta == null)
                {
                    candidatasAbierta = menuVideo.GetType().GetProperties(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                bool abierta = false;
                for (int i = 0; i < candidatasAbierta.Length; i++)
                {
                    PropertyInfo p = candidatasAbierta[i];
                    if (p.PropertyType != typeof(string) || p.GetIndexParameters().Length != 0)
                    {
                        continue;
                    }
                    string v;
                    try { v = p.GetValue(menuVideo) as string; }
                    catch (Exception) { continue; }

                    if (string.IsNullOrEmpty(v))
                    {
                        if (!vistasVacias.Contains(p.Name))
                        {
                            vistasVacias.Add(p.Name);
                        }
                        continue;
                    }
                    // Solo cuenta si esa propiedad se ha visto vacia alguna vez
                    // (es decir, es la de "abierta" y no la de "resaltada") y
                    // ahora contiene nuestra fila.
                    if (vistasVacias.Contains(p.Name)
                        && v.StartsWith(PrefijoSlideshow, StringComparison.Ordinal))
                    {
                        abierta = true;
                    }
                }
                return abierta;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // El texto de menuStrings solo se relee al redibujar, asi que se
        // escribe tambien directamente en la fila fisica.
        private static void RefrescarFila(int indice)
        {
            try
            {
                PropertyInfo p = FilasMenu.Prop(menuVideo.GetType(), "textObjects");
                if (p == null || indice < 0)
                {
                    return;
                }
                object arr = p.GetValue(menuVideo);
                if (arr == null)
                {
                    return;
                }
                PropertyInfo len = arr.GetType().GetProperty("Length");
                PropertyInfo idx = arr.GetType().GetProperty("Item");
                if ((int)len.GetValue(arr) <= indice)
                {
                    return;
                }
                var t = idx.GetValue(arr, new object[] { indice }) as Il2CppTMPro.TextMeshProUGUI;
                if (t != null)
                {
                    t.text = TextoSlideshow();
                }
            }
            catch (Exception)
            {
            }
        }

        private static PropertyInfo propOpcionActual;

        private static string OpcionResaltada()
        {
            if (propOpcionActual == null)
            {
                Il2CppStringArray filas = FilasMenu.Opciones(menuVideo);
                if (filas == null)
                {
                    return null;
                }
                PropertyInfo[] props = menuVideo.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < props.Length; i++)
                {
                    if (props[i].PropertyType != typeof(string)
                        || props[i].GetIndexParameters().Length != 0)
                    {
                        continue;
                    }
                    string v;
                    try { v = props[i].GetValue(menuVideo) as string; }
                    catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(v))
                    {
                        continue;
                    }
                    for (int j = 0; j < filas.Length; j++)
                    {
                        if (filas[j] == v)
                        {
                            propOpcionActual = props[i];
                            return v;
                        }
                    }
                }
                return null;
            }
            return propOpcionActual.GetValue(menuVideo) as string;
        }
    }
}
