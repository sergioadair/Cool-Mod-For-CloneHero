using System;
using System.Reflection;
using MelonLoader;

namespace CloneHeroMod
{
    // Una cancion al azar resaltada en la lista tras abrir el juego o
    // escanear, en vez de siempre la primera.
    //
    // COMO LO DECIDE EL JUEGO:
    //   - Al acabar cualquier escaneo (tambien el del arranque) llama a
    //     SongSelect.ResetIndex, que lleva el cursor a la primera cancion.
    //   - SongSelect.Start, la primera vez que se entra a la lista, la pone en
    //     la cancion jugada antes (GlobalVariables.selectedSong) o, si no hay
    //     ninguna, en la primera. Corre justo despues del primer OnEnable.
    //   - OnEnable, al volver a entrar, no toca la posicion: la que tenias.
    //
    // Asi que ResetIndex deja apuntado que toca una al azar, y se aplica al
    // entrar a la lista (OnEnable y, la primera vez, tambien tras Start, que
    // si no la pisaria). Si la lista se esta viendo cuando acaba el escaneo,
    // no: se queda en la primera, como en el juego. Todo lo demas sigue igual.
    public static class CancionAleatoria
    {
        private const string MetodoResetIndex = "Method_Public_Void_0";

        private static bool pendiente;
        private static IntPtr iniciada = IntPtr.Zero;      // la SongSelect que ya paso por Start
        private static readonly Random azar = new Random();

        public static void InstalarParches(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type t = typeof(Il2Cpp.SongSelect);
                BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic
                               | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                MethodInfo reset = t.GetMethod(MetodoResetIndex, f);
                MethodInfo onEnable = t.GetMethod("OnEnable", f);
                MethodInfo start = t.GetMethod("Start", f);
                if (reset == null || onEnable == null || start == null
                    || reset.GetParameters().Length != 0)
                {
                    MelonLogger.Warning("[Aleatoria] SongSelect no tiene la forma esperada:"
                        + " la lista empieza en la primera cancion");
                    return;
                }
                harmony.Patch(reset, null, Postfix("TrasResetIndex"));
                harmony.Patch(onEnable, null, Postfix("TrasOnEnable"));
                harmony.Patch(start, null, Postfix("TrasStart"));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Aleatoria] parches: " + ex.Message);
            }
        }

        private static HarmonyLib.HarmonyMethod Postfix(string nombre)
        {
            return new HarmonyLib.HarmonyMethod(typeof(CancionAleatoria).GetMethod(
                nombre, BindingFlags.NonPublic | BindingFlags.Static));
        }

        // Si el escaneo acaba con la lista a la vista, se deja lo del juego (la
        // primera cancion): que el cursor salte a cualquier sitio mientras la
        // estas mirando confunde. Y no queda nada pendiente para despues.
        private static void TrasResetIndex(Il2Cpp.SongSelect __instance)
        {
            try
            {
                pendiente = !(__instance != null && __instance.Pointer == iniciada
                              && __instance.gameObject.activeInHierarchy);
            }
            catch (Exception)
            {
                pendiente = true;
            }
        }

        private static void TrasOnEnable(Il2Cpp.SongSelect __instance)
        {
            if (!pendiente || __instance == null)
            {
                return;
            }
            Elegir(__instance);
            if (__instance.Pointer == iniciada)
            {
                pendiente = false;      // si no, Start todavia va a moverla
            }
        }

        private static void TrasStart(Il2Cpp.SongSelect __instance)
        {
            if (__instance == null)
            {
                return;
            }
            iniciada = __instance.Pointer;
            if (pendiente)
            {
                Elegir(__instance);
                pendiente = false;
            }
        }

        // Una posicion al azar de la lista visible (filtrada), saltando las
        // cabeceras de seccion. En partida online la lista es la del servidor:
        // no se toca.
        private static void Elegir(Il2Cpp.SongSelect lista)
        {
            try
            {
                Il2Cpp.CHNetManager red = Il2Cpp.CHNetManager.instance;
                if (red != null && red.prop_Boolean_1)
                {
                    return;
                }
                int n = Il2Cpp.ObjectPublicAbstractSealedLi1SoDi2ObInLi1SoUnique.prop_Int32_1;   // SongDirectory.m35
                if (n < 2)
                {
                    return;
                }
                int i = azar.Next(n);
                for (int k = 0; k < n; k++, i = (i + 1) % n)
                {
                    if (EsCancion(i))
                    {
                        lista.Method_Public_Virtual_New_Void_Int32_Boolean_1(i, true);      // m71
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Aleatoria] " + ex.Message);
            }
        }

        private static bool EsCancion(int i)
        {
            var e = Il2Cpp.ObjectPublicAbstractSealedLi1SoDi2ObInLi1SoUnique      // SongDirectory
                .Method_Public_Static_InterfacePublicAbstractIngeIngeInInUnique_Int32_0(i);   // m44
            return e != null && e.TryCast<Il2Cpp.SongEntry>() != null;
        }
    }
}
