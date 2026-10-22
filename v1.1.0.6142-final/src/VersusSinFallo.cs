using System;
using System.Reflection;
using MelonLoader;

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
    public static class VersusSinFallo
    {
        // T8288.ChangeHealth(bool increase), del motor de StrikeCore.
        private const string MetodoCambiarVida = "Method_Protected_Void_Boolean_0";
        private const int ModoVersus = 2;      // GameMode: Quickplay, Practice, Versus

        public static void InstalarParche(HarmonyLib.Harmony harmony)
        {
            try
            {
                MethodInfo m = typeof(Il2Cpp.ObjectPublicAbstractDoBoDoInBoObDoInSiBoUnique).GetMethod(
                    MetodoCambiarVida, BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (m == null || m.ReturnType != typeof(void) || m.GetParameters().Length != 1
                    || m.GetParameters()[0].ParameterType != typeof(bool))
                {
                    MelonLogger.Warning("[Versus] no se localizo la vida del motor: en Versus se"
                        + " puede perder como en el juego sin mod");
                    return;
                }
                harmony.Patch(m, new HarmonyLib.HarmonyMethod(typeof(VersusSinFallo).GetMethod(
                    "AntesDeCambiarVida", BindingFlags.NonPublic | BindingFlags.Static)));
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
            if (red != null && red.prop_Boolean_1)      // partida online (CHNetManager.m19)
            {
                return false;
            }
            Il2Cpp.LeaderboardsOnlineManager tablas = Il2Cpp.LeaderboardsOnlineManager.instance;
            return tablas == null || !tablas.field_Private_Boolean_0;    // leaderboards activos
        }
    }
}
