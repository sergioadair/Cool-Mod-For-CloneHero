using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace CloneHeroMod
{
    // Filas "Finished Song SFX" (Yes/No) y "Finished Song SFX Volume" (barra)
    // en Settings > Audio.
    //
    // Misma receta que la del slideshow en Video, ya probada:
    //   - la fila se anade en un PREFIX de OnEnable (si se anade despues, el
    //     menu cuenta una opcion de menos y queda inalcanzable);
    //   - el Select se empareja por RANURA VIRTUAL con el de GeneralSettingsMenu,
    //     porque los nombres que genera Il2CppInterop van numerados por clase;
    //   - la conmutacion no cuelga de los eventos del menu sino de
    //     VigilanteMenu, que vigila cuando el jugador ABRE la fila; ahi esta
    //     explicado por que hacerlo por eventos daba problemas.
    //   - el estado va en el texto de la fila, porque el juego no tiene widget
    //     de valor para una fila que no conoce.
    public static class MenuAudio
    {
        public const string Prefijo = "Finished Song SFX";

        // Con los dos puntos: "Finished Song SFX Volume" empieza igual, y sin
        // ellos el interruptor la tomaba por suya.
        private const string PrefijoInterruptor = Prefijo + ":";

        private static bool parcheado;
        private static object menuAudio;

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
                    MelonLogger.Warning("[MenuAudio] tipos no localizados");
                    return;
                }

                MethodInfo onEnable = tBase.GetMethod("OnEnable",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (onEnable != null)
                {
                    harmony.Patch(onEnable, new HarmonyMethod(
                        typeof(MenuAudio).GetMethod("PreOnEnable",
                            BindingFlags.NonPublic | BindingFlags.Static)), null);
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Error("[MenuAudio] " + ex);
            }
        }

        private static string Texto()
        {
            return Prefijo + ": " + (Ajustes.SfxFinActivo ? "Yes" : "No");
        }

        private static void PreOnEnable(Il2CppSystem.Object __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }
                Il2Cpp.AudioSettingsMenu a = __instance.TryCast<Il2Cpp.AudioSettingsMenu>();
                if (a == null)
                {
                    return;
                }
                menuAudio = a;
                // Se aprende AQUI que propiedad es la de "opcion abierta": al
                // abrir el menu no hay nada seleccionado, asi que esa esta
                // vacia. Si se dejaba aprender solo desde el Select, la primera
                // pulsacion era la primera observacion y no conmutaba: habia
                // que pulsar dos veces tras arrancar el juego.
                vigilante.Preparar(a);
                FilasMenu.Anadir(a, Texto(), PrefijoInterruptor);
                volumen.Anadir(a);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[MenuAudio] PreOnEnable: " + ex);
            }
        }

        private static readonly VigilanteMenu vigilante =
            new VigilanteMenu("MenuAudio", PrefijoInterruptor);

        // 0 a 100 %, de 5 en 5, como los volumenes del juego.
        private static readonly FilaDeslizador volumen = new FilaDeslizador(
            Prefijo + " Volume", 0, 100, 5, 100,
            () => Ajustes.VolumenSfx * 100f,
            v => Ajustes.GuardarVolumenSfx(v / 100f),
            v => ((int)Math.Round(v)).ToString() + "%");

        public static void Tick()
        {
            try
            {
                volumen.Tick(menuAudio);
                if (vigilante.RecienAbierta(menuAudio) == null)
                {
                    return;
                }
                Ajustes.GuardarSfxFin(!Ajustes.SfxFinActivo);
                int fila = FilasMenu.IndiceDe(menuAudio, PrefijoInterruptor);
                FilasMenu.CambiarTexto(menuAudio, fila, Texto());
                RefrescarFila(fila);
            }
            catch (Exception)
            {
            }
        }


        private static void RefrescarFila(int indice)
        {
            try
            {
                PropertyInfo p = FilasMenu.Prop(menuAudio.GetType(), "textObjects");
                if (p == null || indice < 0)
                {
                    return;
                }
                object arr = p.GetValue(menuAudio);
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
