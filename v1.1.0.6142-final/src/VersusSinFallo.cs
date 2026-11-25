using System;
using System.IO;
using System.Reflection;
using MelonLoader;
using MelonLoader.Utils;

namespace CloneHeroMod
{
    // En Versus no se pierde, como en la v1.0.0.4080.
    //
    // QUE CAMBIO ENTRE VERSIONES. En la v1.0, BasePlayer.Awake ponia
    // healthDrain = 0 con No Fail, en partida online O EN VERSUS: la vida no
    // bajaba nunca y no habia forma de perder. En la v1.1 la vida la lleva el
    // motor de StrikeCore y el jugador solo decide si pierdes al llegar a cero,
    // mirando No Fail y nada mas. Versus sigue escondiendo la barra de vida
    // (BasePlayer.Awake_2, gameMode == 2), pero la vida baja igual: se podia
    // perder sin ningun aviso.
    //
    // LO QUE SE HACE: lo mismo que la v1.0. En Versus el motor no resta vida
    // al fallar una nota (ChangeHealth(false) no corre); la que se gana al
    // acertar sigue igual, aunque ya no importe. No se toca el ajuste No Fail:
    // el juego lo guarda en settings.ini, lo apunta en las puntuaciones y las
    // repeticiones ("no fail") y si el juego se cerrara a media cancion se
    // quedaria puesto.
    //
    // CUANDO NO SE HACE, para que se vea igual que se juega:
    //   - Con los leaderboards online activos el juego SI ensena la barra en
    //     Versus (Awake_2) y valida la partida en su servidor con tus entradas.
    //     Ahi la barra da el aviso que faltaba, y cambiar lo que pasa en local
    //     solo descuadraria la validacion.
    //   - En partida online y en practica el juego ya no deja perder.
    //
    // CON GHLARP. GHLarp dibuja su propio medidor de vida y lo pone "vacio"
    // (estilo No Fail) solo si el ajuste No Fail esta activado o es practica:
    // no sabe que en Versus tampoco se pierde, asi que ensenaba un medidor que
    // se movia sin consecuencias. Para que cuadre, mientras se juega una
    // cancion en Versus el ajuste No Fail RESPONDE que si a quien lo lea
    // (GameSetting.GetBoolValue), sin cambiar lo guardado. Solo dentro de la
    // escena Gameplay: la puntuacion y la repeticion se guardan en la escena
    // EndOfSong, que ya ve el valor de verdad. Dentro de la cancion solo lo
    // lee BasePlayer, para lo mismo que ya hace este archivo (no perder y no
    // ensenar la barra, que Versus ya esconde).
    // Ese lector es comun a todos los ajustes de si/no, asi que solo se
    // engancha si GHLarp esta instalado.
    public static class VersusSinFallo
    {
        private const int ModoVersus = 2;      // GameMode: Quickplay, Practice, Versus

        private static PropertyInfo enLinea;          // CHNetManager: partida online
        private static PropertyInfo tablasActivas;    // LeaderboardsOnlineManager

        private static PropertyInfo ajusteSinFallo;   // Settings.isNoFail (estatico)
        private static IntPtr sinFallo;               // el GameSetting de No Fail, mientras se finge
        private static volatile bool fingiendo;
        private static long lecturas;                 // cuantas veces se leyo un ajuste si/no
        private static DateTime desde;

        // Lo llama Diagnostico en cada cambio de escena.
        public static void EscenaCambiada(bool enJuego)
        {
            if (ajusteSinFallo == null)
            {
                return;          // sin GHLarp, o no se pudo enganchar
            }
            if (fingiendo)
            {
                double s = (DateTime.Now - desde).TotalSeconds;
                MelonLogger.Msg("[Versus] GHLarp vio No Fail durante " + s.ToString("0") + " s ("
                    + lecturas.ToString() + " lecturas de ajustes si/no)");
            }
            fingiendo = false;
            sinFallo = IntPtr.Zero;
            try
            {
                if (!enJuego || !Aplica())
                {
                    return;
                }
                Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase ajuste =
                    ajusteSinFallo.GetValue(null) as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (ajuste == null)
                {
                    return;
                }
                sinFallo = ajuste.Pointer;
                lecturas = 0;
                desde = DateTime.Now;
                fingiendo = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Versus] GHLarp: " + ex.Message);
            }
        }

        private static void EngancharGHLarp(HarmonyLib.Harmony harmony)
        {
            if (!Directory.Exists(Path.Combine(MelonEnvironment.GameRootDirectory, "ghlarp")))
            {
                return;
            }
            Type tipoAjuste = typeof(Il2Cpp.Object2PublicBoSiInSiDoStSiStStUnique);      // GameSetting
            MethodInfo leer = Nativo.Metodo(tipoAjuste, Real.AjusteSiNo);
            PropertyInfo campo = Nativo.Campo(typeof(Il2Cpp.ObjectPublicAbstractSealedBoObObObObObObObObObUnique),
                Real.SinFallo);                                                         // Settings
            if (leer == null || leer.ReturnType != typeof(bool) || campo == null)
            {
                MelonLogger.Warning("[Versus] no se localizo el ajuste No Fail: GHLarp ensenara"
                    + " el medidor en Versus");
                return;
            }
            harmony.Patch(leer, new HarmonyLib.HarmonyMethod(typeof(VersusSinFallo).GetMethod(
                "AntesDeLeerSiNo", BindingFlags.NonPublic | BindingFlags.Static)));
            ajusteSinFallo = campo;
            MelonLogger.Msg("[Versus] GHLarp instalado: en Versus vera No Fail");
        }

        // Corre en CADA lectura de un ajuste si/no del juego: lo primero es
        // salir si no se esta fingiendo.
        private static bool AntesDeLeerSiNo(Il2Cpp.Object2PublicBoSiInSiDoStSiStStUnique __instance,
                                            ref bool __result)
        {
            if (!fingiendo)
            {
                return true;
            }
            lecturas++;
            if (__instance == null || __instance.Pointer != sinFallo)
            {
                return true;
            }
            __result = true;
            return false;
        }

        public static void InstalarParche(HarmonyLib.Harmony harmony)
        {
            try
            {
                // T8288.ChangeHealth(bool increase), del motor de StrikeCore.
                MethodInfo m = Nativo.Metodo(typeof(Il2Cpp.ObjectPublicAbstractDoBoDoInBoObDoInSiBoUnique),
                    Real.CambiarVida, typeof(bool));
                enLinea = Nativo.Propiedad(typeof(Il2Cpp.CHNetManager), Real.EnLinea);
                tablasActivas = Nativo.Campo(typeof(Il2Cpp.LeaderboardsOnlineManager), Real.TablasActivas);
                if (m == null || m.ReturnType != typeof(void) || enLinea == null || tablasActivas == null)
                {
                    MelonLogger.Warning("[Versus] no se localizo la vida del motor: en Versus se"
                        + " puede perder como en el juego sin mod");
                    return;
                }
                harmony.Patch(m, new HarmonyLib.HarmonyMethod(typeof(VersusSinFallo).GetMethod(
                    "AntesDeCambiarVida", BindingFlags.NonPublic | BindingFlags.Static)));
                EngancharGHLarp(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Versus] parche: " + ex.Message);
            }
        }

        // Corre en cada nota acertada o fallada; con un acierto sale a la
        // primera. Ante cualquier duda, se deja hacer al juego.
        private static bool AntesDeCambiarVida(bool __0)
        {
            if (__0)
            {
                return true;
            }
            try
            {
                return !Aplica();
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static bool Aplica()
        {
            Il2Cpp.GlobalVariables g = Il2Cpp.GlobalVariables.instance;
            if (g == null || (int)g.gameMode != ModoVersus || g.isPracticeEnabled)
            {
                return false;
            }
            Il2Cpp.CHNetManager red = Il2Cpp.CHNetManager.instance;
            if (red != null && (bool)enLinea.GetValue(red))      // partida online (CHNetManager.m19)
            {
                return false;
            }
            Il2Cpp.LeaderboardsOnlineManager tablas = Il2Cpp.LeaderboardsOnlineManager.instance;
            return tablas == null || !(bool)tablasActivas.GetValue(tablas);    // leaderboards activos
        }
    }
}
