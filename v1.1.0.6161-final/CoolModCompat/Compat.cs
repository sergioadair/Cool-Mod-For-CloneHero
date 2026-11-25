using System;
using System.IO;
using System.Reflection;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(CoolModCompat.Compat), "Cool Mod Compat", "1.0.0", "sergioadair")]
[assembly: MelonGame("srylain Inc.", "Clone Hero")]

namespace CoolModCompat
{
    // Deja convivir MelonLoader con GHLarp.
    //
    // EL CHOQUE: los dos enganchan las mismas funciones de Unity,
    // SceneManager.Internal_SceneLoaded e Internal_SceneUnloaded, reescribiendo
    // sus primeros bytes. GHLarp llega antes y pone un salto de 5 bytes;
    // MelonLoader (con Harmony, para avisar a los mods de cada escena) pone
    // despues uno de 6 encima. Cuando GHLarp devuelve el control a la funcion
    // original lo hace en el byte 5, que ya es un trozo del salto de
    // MelonLoader: el procesador ejecuta basura y .NET se cae al descargar la
    // primera escena, unos 20 segundos despues de abrir el juego.
    //
    // EL ARREGLO: cuando MelonLoader pide enganchar esas dos funciones, no se
    // escribe el salto nativo (se intercepta Il2CppDetourMethodPatcher.DetourTo,
    // que es quien lo escribe) y en su lugar se suscribe a los eventos de Unity
    // SceneManager.sceneLoaded y sceneUnloaded, que esas mismas funciones
    // disparan y no tocan codigo nativo. Los mods siguen recibiendo
    // OnSceneWasLoaded, OnSceneWasInitialized y OnSceneWasUnloaded igual.
    //
    // (No se puede hacer parcheando el propio MelonLoader: bloquea los parches
    // de Harmony sobre sus ensamblados.)
    //
    // Solo actua si GHLarp esta instalado (la carpeta ghlarp junto al juego):
    // sin el, MelonLoader se queda exactamente como viene.
    public class Compat : MelonPlugin
    {
        private const string TipoEscenas = "MelonLoader.Support.SceneHandler";

        private static MethodInfo alCargar;       // SceneHandler.OnSceneLoad(Scene, LoadSceneMode)
        private static MethodInfo alDescargar;    // SceneHandler.OnSceneUnload(Scene)
        private static bool suscritoCarga;
        private static bool suscritoDescarga;

        public override void OnPreInitialization()
        {
            if (!Directory.Exists(Path.Combine(MelonEnvironment.GameRootDirectory, "ghlarp")))
            {
                return;
            }
            try
            {
                // Il2CppDetourMethodPatcher es interno: por reflexion.
                Type patcher = Assembly.Load("Il2CppInterop.HarmonySupport")
                    .GetType("Il2CppInterop.HarmonySupport.Il2CppDetourMethodPatcher", false);
                MethodInfo detour = patcher?.GetMethod("DetourTo",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (detour == null)
                {
                    MelonLogger.Warning("[Compat] this MelonLoader version patches differently;"
                        + " nothing changed (the game may close with GHLarp installed)");
                    return;
                }
                new HarmonyLib.Harmony("CoolModCompat").Patch(detour, new HarmonyLib.HarmonyMethod(
                    typeof(Compat).GetMethod(nameof(AntesDeDetour), BindingFlags.NonPublic | BindingFlags.Static)));
                MelonLogger.Msg("[Compat] GHLarp found: MelonLoader will follow scenes without hooking them");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Compat] " + ex.Message);
            }
        }

        // Corre cada vez que Harmony va a escribir un salto nativo sobre un
        // metodo del juego. Solo se intercepta el de las dos funciones de
        // escena; todo lo demas sigue su curso.
        private static bool AntesDeDetour(object __instance, MethodBase replacement, ref MethodBase __result)
        {
            try
            {
                MethodBase original = ((HarmonyLib.Public.Patching.MethodPatcher)__instance).Original;
                if (original == null || original.DeclaringType != typeof(SceneManager))
                {
                    return true;
                }
                bool carga = original.Name == "Internal_SceneLoaded";
                bool descarga = original.Name == "Internal_SceneUnloaded";
                if (!carga && !descarga)
                {
                    return true;
                }
                if (!Resolver())
                {
                    return true;        // sin a donde avisar: mejor el gancho de siempre
                }
                // add_sceneLoaded viene recortado en el juego: se escribe el
                // campo del evento directamente, sumando a lo que ya hubiera.
                if (carga && !suscritoCarga)
                {
                    UnityAction<Scene, LoadSceneMode> nuestro = new Action<Scene, LoadSceneMode>(Cargada);
                    UnityAction<Scene, LoadSceneMode> antes = SceneManager.sceneLoaded;
                    SceneManager.sceneLoaded = antes == null ? nuestro : antes + nuestro;
                    suscritoCarga = true;
                }
                if (descarga && !suscritoDescarga)
                {
                    UnityAction<Scene> nuestro = new Action<Scene>(Descargada);
                    UnityAction<Scene> antes = SceneManager.sceneUnloaded;
                    SceneManager.sceneUnloaded = antes == null ? nuestro : antes + nuestro;
                    suscritoDescarga = true;
                }
                MelonLogger.Msg("[Compat] " + original.Name + ": followed through Unity's event, not hooked");
                __result = replacement;
                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Compat] " + ex.Message + " (MelonLoader hooks it as usual)");
                return true;
            }
        }

        // Los avisos de MelonLoader para los mods, en su modulo de soporte.
        private static bool Resolver()
        {
            if (alCargar != null && alDescargar != null)
            {
                return true;
            }
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(TipoEscenas, false);
                if (t == null)
                {
                    continue;
                }
                const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                alCargar = t.GetMethod("OnSceneLoad", f);
                alDescargar = t.GetMethod("OnSceneUnload", f);
                break;
            }
            return alCargar != null && alDescargar != null;
        }

        private static void Cargada(Scene escena, LoadSceneMode modo)
        {
            try
            {
                alCargar.Invoke(null, new object[] { escena, modo });
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Compat] scene loaded: " + ex.Message);
            }
        }

        private static void Descargada(Scene escena)
        {
            try
            {
                alDescargar.Invoke(null, new object[] { escena });
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Compat] scene unloaded: " + ex.Message);
            }
        }
    }
}
