using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace CloneHeroMod
{
    // Fila "Show Cool Note Streak" en Settings > Gameplay.
    //
    // Es el sitio que le toca: enciende y apaga un cartel que sale durante la
    // cancion. Estuvo un rato en Audio por falta de hueco, cuando las filas de
    // mas caian en la banda difuminada del final de la lista; con el degradado
    // apagado (ver FilasMenu.QuitarDegradado) esa restriccion desaparecio.
    //
    // Receta base, la misma de Video y Audio:
    //   - la fila se anade en un PREFIX de OnEnable, porque ahi se calculan los
    //     limites de navegacion; si se anade despues queda inalcanzable;
    //   - la conmutacion no cuelga de los eventos del menu sino de
    //     VigilanteMenu, que vigila cuando el jugador ABRE la fila;
    //   - el estado va en el propio texto de la fila: el juego no tiene widget
    //     de valor para una fila que no conoce.
    //
    // Este menu fue el que dejo claro que engancharse a los eventos no vale:
    // el metodo que se localizaba emparejando la ranura virtual con
    // GeneralSettingsMenu resulto ser el que corre al SALIR, asi que la opcion
    // se conmutaba sola al abandonar el menu. Ver VigilanteMenu.
    public static class MenuGameplay
    {
        public const string Prefijo = "Show Cool Note Streak";

        private static bool parcheado;
        private static object menu;

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
                if (tBase == null)
                {
                    MelonLogger.Warning("[MenuGameplay] tipos no localizados");
                    return;
                }

                MethodInfo onEnable = tBase.GetMethod("OnEnable",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (onEnable != null)
                {
                    harmony.Patch(onEnable, new HarmonyMethod(
                        typeof(MenuGameplay).GetMethod("PreOnEnable",
                            BindingFlags.NonPublic | BindingFlags.Static)), null);
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Error("[MenuGameplay] " + ex);
            }
        }

        private static string Texto()
        {
            return Prefijo + ": " + (Ajustes.MostrarRacha ? "Yes" : "No");
        }

        // __instance se pide como Il2CppSystem.Object y se convierte a mano:
        // BaseSettingMenu es abstracta y pedirla directamente hace fallar la
        // conversion del trampolin.
        private static void PreOnEnable(Il2CppSystem.Object __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }
                Il2Cpp.GameplaySettingsMenu g = __instance.TryCast<Il2Cpp.GameplaySettingsMenu>();
                if (g == null)
                {
                    return;      // es otro submenu de ajustes
                }
                menu = g;
                vigilante.Preparar(g);
                FilasMenu.Anadir(g, Texto(), Prefijo);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[MenuGameplay] PreOnEnable: " + ex);
            }
        }

        private static readonly VigilanteMenu vigilante =
            new VigilanteMenu("MenuGameplay", Prefijo);

        public static void Tick()
        {
            try
            {
                if (vigilante.RecienAbierta(menu) == null)
                {
                    return;
                }
                Ajustes.GuardarMostrarRacha(!Ajustes.MostrarRacha);
                int fila = FilasMenu.IndiceDe(menu, Prefijo);
                FilasMenu.CambiarTexto(menu, fila, Texto());
                RefrescarFila(fila);
            }
            catch (Exception)
            {
            }
        }

        // Cual es la propiedad de "opcion abierta" no se sabe de antemano: los
        // nombres los genera Il2CppInterop. Se aprende sola — es la unica string
        // del menu que alguna vez se ve vacia, porque la de la opcion resaltada
        // nunca lo esta mientras el menu se ve.

        // menuStrings solo se relee al redibujar el menu entero, asi que el
        // texto se escribe tambien en la fila fisica.
        private static void RefrescarFila(int indice)
        {
            try
            {
                PropertyInfo p = FilasMenu.Prop(menu.GetType(), "textObjects");
                if (p == null || indice < 0)
                {
                    return;
                }
                object arr = p.GetValue(menu);
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
                    t.text = Texto();
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
