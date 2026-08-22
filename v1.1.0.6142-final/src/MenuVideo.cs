using System;
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
    //   2. Las filas "Menu BG Slideshow" y "Show Difficulty", que hasta ahora
    //      solo existian en settings.ini.
    //
    // NO CABEN MAS: el contenido del scroll tiene una altura fija puesta a mano
    // — Video 1600 = (18 filas + 2) x 80 — y con estas dos ya va exactamente
    // lleno. Una tercera fila se sale del area desplazable y no hay forma de
    // llegar a ella. Se intento agrandar el contenedor y sale caro: segun como
    // este montado el menu en ese momento, las filas cuelgan de una caja propia
    // de alto fijo o directamente y ancladas en estiramiento, y en ese segundo
    // caso agrandar el contenedor estira todas las filas y las solapa. La
    // tercera opcion vive en Audio, que si tiene un hueco libre.
    //
    // El estado va en el propio texto de la fila ("Menu BG Slideshow: Yes") en
    // vez de en un widget Yes/No aparte: el juego no tiene hueco de valor para
    // una fila que no conoce, y crear uno a mano fue justo lo que descoloco el
    // menu de Audio en la version anterior.
    //
    // La conmutacion no cuelga de los eventos del menu, sino de VigilanteMenu:
    // ahi esta explicado por que, con los tres problemas distintos que dio
    // hacerlo por eventos.
    public static class MenuVideo
    {
        public const string PrefijoSlideshow = "Menu BG Slideshow";
        public const string PrefijoMostrarDificultad = "Show Difficulty";

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
                    // OnEnable de BaseSettingMenu, o sea TODOS los submenus de
                    // ajustes. El prefix anade nuestras filas al de Video; el
                    // postfix quita el degradado del final de la lista, y ese
                    // vale para todos. Va DESPUES a proposito: el juego elige
                    // ahi cual de los dos sprites poner.
                    harmony.Patch(onEnable,
                        new HarmonyMethod(typeof(MenuVideo).GetMethod("PreOnEnable",
                            BindingFlags.NonPublic | BindingFlags.Static)),
                        new HarmonyMethod(typeof(MenuVideo).GetMethod("PostOnEnable",
                            BindingFlags.NonPublic | BindingFlags.Static)));
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
                FilasMenu.Anadir(v, TextoMostrarDificultad(), PrefijoMostrarDificultad);
                vigilante.Preparar(v);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[MenuVideo] PreOnEnable: " + ex);
            }
        }

        // __instance se pide como Il2CppSystem.Object y se convierte a mano:
        // BaseSettingMenu es abstracta y pedirla directamente hace fallar la
        // conversion del trampolin.
        private static void PostOnEnable(Il2CppSystem.Object __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }
                FilasMenu.QuitarDegradado(__instance.TryCast<Il2Cpp.BaseSettingMenu>());
            }
            catch (Exception)
            {
            }
        }

        private static string TextoSlideshow()
        {
            return PrefijoSlideshow + ": " + (Ajustes.SlideshowActivo ? "Yes" : "No");
        }

        private static string TextoMostrarDificultad()
        {
            return PrefijoMostrarDificultad + ": " + (Ajustes.MostrarDificultad ? "Yes" : "No");
        }

        // El texto que le toca a cada una de nuestras filas segun su ajuste.
        private static string TextoDe(string prefijo)
        {
            return prefijo == PrefijoSlideshow ? TextoSlideshow() : TextoMostrarDificultad();
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

        private static readonly VigilanteMenu vigilante = new VigilanteMenu(
            "MenuVideo", PrefijoSlideshow, PrefijoMostrarDificultad);

        public static void Tick()
        {
            string prefijo = vigilante.RecienAbierta(menuVideo);
            if (prefijo != null)
            {
                Conmutar(prefijo);
            }
        }

        private static void Conmutar(string prefijo)
        {
            if (prefijo == PrefijoSlideshow)
            {
                Ajustes.GuardarSlideshow(!Ajustes.SlideshowActivo);
            }
            else
            {
                Ajustes.GuardarMostrarDificultad(!Ajustes.MostrarDificultad);
            }
            int fila = FilasMenu.IndiceDe(menuVideo, prefijo);
            FilasMenu.CambiarTexto(menuVideo, fila, TextoDe(prefijo));
            RefrescarFila(fila, TextoDe(prefijo));
        }

        // El texto de menuStrings solo se relee al redibujar, asi que se
        // escribe tambien directamente en la fila fisica.
        private static void RefrescarFila(int indice, string texto)
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
                    t.text = texto;
                }
            }
            catch (Exception)
            {
            }
        }


    }
}
