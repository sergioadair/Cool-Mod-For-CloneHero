using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;

namespace CloneHeroMod
{
    // Fila "Finished Song SFX" en Settings > Audio.
    //
    // Misma receta que la del slideshow en Video, ya probada:
    //   - la fila se anade en un PREFIX de OnEnable (si se anade despues, el
    //     menu cuenta una opcion de menos y queda inalcanzable);
    //   - el Select se empareja por RANURA VIRTUAL con el de GeneralSettingsMenu,
    //     porque los nombres que genera Il2CppInterop van numerados por clase;
    //   - solo se conmuta si la opcion esta ABIERTA (pulsada), no al pasar por
    //     encima. La propiedad de "abierta" se aprende sola: es la unica string
    //     del menu que alguna vez se ve vacia.
    //   - el estado va en el texto de la fila, porque el juego no tiene widget
    //     de valor para una fila que no conoce.
    public static class MenuAudio
    {
        public const string Prefijo = "Finished Song SFX";

        private static bool parcheado;
        private static object menuAudio;
        private static float ultimaConmutacion;
        private static bool avisadoTraza;

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
                Type tAudio = Ofuscado.Tipo("AudioSettingsMenu");
                Type tGeneral = Ofuscado.Tipo("GeneralSettingsMenu");
                if (tBase == null || tAudio == null || tGeneral == null)
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

                List<MethodInfo> bases = new List<MethodInfo>();
                MethodInfo[] gs = tGeneral.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                for (int i = 0; i < gs.Length; i++)
                {
                    if (EsCandidato(gs[i]))
                    {
                        bases.Add(gs[i].GetBaseDefinition());
                    }
                }

                MethodInfo postSelect = typeof(MenuAudio).GetMethod("PostSelect",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo[] ms = tAudio.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                int n = 0;
                for (int i = 0; i < ms.Length; i++)
                {
                    if (!EsCandidato(ms[i]))
                    {
                        continue;
                    }
                    MethodInfo baseV = ms[i].GetBaseDefinition();
                    bool coincide = false;
                    for (int j = 0; j < bases.Count; j++)
                    {
                        if (bases[j] == baseV)
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
                    }
                    catch (Exception)
                    {
                    }
                }
                MelonLogger.Msg("[MenuAudio] Select emparejados: " + n.ToString());
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[MenuAudio] " + ex);
            }
        }

        private static bool EsCandidato(MethodInfo m)
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
                AprenderVacias();
                FilasMenu.Anadir(a, Texto(), Prefijo);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[MenuAudio] PreOnEnable: " + ex);
            }
        }

        private static void PostSelect()
        {
            try
            {
                if (menuAudio == null)
                {
                    return;
                }
                // Se intento exigir que la opcion YA estuviera abierta antes de
                // la llamada, para que se comportara como el slideshow (pulsar
                // y luego mover). No funciona: en el menu de Audio el Select
                // llega UNA sola vez, al pulsar, y no hay eventos al mover. Con
                // esa condicion no conmutaba nunca.
                bool abierta = EstaAbierta();
                if (!avisadoTraza)
                {
                    avisadoTraza = true;
                    MelonLogger.Msg("[MenuAudio] Select recibido, abierta=" + abierta.ToString());
                }
                if (!abierta)
                {
                    return;      // solo resaltada: no se toca
                }
                float ahora = UnityEngine.Time.realtimeSinceStartup;
                if (ahora - ultimaConmutacion < 0.25f)
                {
                    return;
                }
                ultimaConmutacion = ahora;

                bool nuevo = !Ajustes.SfxFinActivo;
                Ajustes.GuardarSfxFin(nuevo);
                int fila = FilasMenu.IndiceDe(menuAudio, Prefijo);
                FilasMenu.CambiarTexto(menuAudio, fila, Texto());
                RefrescarFila(fila);
            }
            catch (Exception)
            {
            }
        }

        private static PropertyInfo[] candidatas;
        private static readonly List<string> vistasVacias = new List<string>();

        // Registra que propiedades string estan vacias en este momento.
        private static void AprenderVacias()
        {
            try
            {
                if (candidatas == null)
                {
                    candidatas = menuAudio.GetType().GetProperties(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                for (int i = 0; i < candidatas.Length; i++)
                {
                    PropertyInfo p = candidatas[i];
                    if (p.PropertyType != typeof(string) || p.GetIndexParameters().Length != 0)
                    {
                        continue;
                    }
                    string v;
                    try { v = p.GetValue(menuAudio) as string; }
                    catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(v) && !vistasVacias.Contains(p.Name))
                    {
                        vistasVacias.Add(p.Name);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static bool EstaAbierta()
        {
            try
            {
                if (candidatas == null)
                {
                    candidatas = menuAudio.GetType().GetProperties(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                bool abierta = false;
                for (int i = 0; i < candidatas.Length; i++)
                {
                    PropertyInfo p = candidatas[i];
                    if (p.PropertyType != typeof(string) || p.GetIndexParameters().Length != 0)
                    {
                        continue;
                    }
                    string v;
                    try { v = p.GetValue(menuAudio) as string; }
                    catch (Exception) { continue; }

                    if (string.IsNullOrEmpty(v))
                    {
                        if (!vistasVacias.Contains(p.Name))
                        {
                            vistasVacias.Add(p.Name);
                        }
                        continue;
                    }
                    if (vistasVacias.Contains(p.Name)
                        && v.StartsWith(Prefijo, StringComparison.Ordinal))
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
