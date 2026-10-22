using System;
using MelonLoader;
using UnityEngine;

namespace CloneHeroMod
{
    // La dificultad del chart que cada jugador va a tocar, en la pantalla de
    // elegir instrumento y nivel: a la izquierda del icono del instrumento,
    // bajo su nombre.
    //
    // CADA JUGADOR TIENE SU PROPIO PlayerSelectionMenu, y la clase guarda ella
    // misma la cancion, el instrumento y el nivel que esta ensenando (campos
    // songEntry, instrument y difficulty). Asi que no hay que averiguar que
    // jugador es cual, como en SeleccionJugador: cada menu lleva lo suyo.
    //
    // El texto cuelga del icono (headerDifficultyIcon) y no de la pagina: el
    // juego lo esconde cuando la cancion no trae ese chart ("No Part") y con
    // las demas paginas, y nuestro numero se va con el sin tener que vigilar
    // nada. Se copia del texto del nivel ("Expert") para llevar su letra.
    //
    // El numero es el mismo calculo que la nota por instrumento del song.ini,
    // pero sobre el chart elegido; en el nivel mas alto coincide con ella. Se
    // calcula en otro hilo (PerfilChart) y aparece en cuanto esta.
    //
    // SE PINTA DESDE EL Update DE CADA MENU (un postfix), no buscandolos: el
    // lienzo de los jugadores esta apagado hasta que se elige cancion, y en
    // este binario no viene ninguna forma de buscar objetos inactivos
    // (Resources.FindObjectsOfTypeAll falla con "Method unstripping failed",
    // y la version que pide el tipo ni siquiera llega al interop). Asi cada
    // menu se pinta solo mientras se ve, y sin listas que mantener.
    public static class DificultadJugador
    {
        // Por menu (su puntero nativo): el texto creado y lo ultimo escrito.
        private static readonly System.Collections.Generic.Dictionary<IntPtr, Il2CppTMPro.TextMeshProUGUI> textos =
            new System.Collections.Generic.Dictionary<IntPtr, Il2CppTMPro.TextMeshProUGUI>();
        private static readonly System.Collections.Generic.Dictionary<IntPtr, string> puestos =
            new System.Collections.Generic.Dictionary<IntPtr, string>();
        private static bool avisado;

        public static void EscenaCambiada()
        {
            textos.Clear();      // se van con la escena
            puestos.Clear();
        }

        public static void InstalarParche(HarmonyLib.Harmony harmony)
        {
            try
            {
                System.Reflection.MethodInfo update = typeof(Il2Cpp.PlayerSelectionMenu).GetMethod("Update",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                if (update == null)
                {
                    MelonLogger.Warning("[DificultadJugador] PlayerSelectionMenu sin Update: no se pinta");
                    return;
                }
                harmony.Patch(update, null, new HarmonyLib.HarmonyMethod(typeof(DificultadJugador).GetMethod(
                    "TrasUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[DificultadJugador] parche: " + ex.Message);
            }
        }

        private static void TrasUpdate(Il2Cpp.PlayerSelectionMenu __instance)
        {
            try
            {
                Pintar(__instance);
            }
            catch (Exception ex)
            {
                if (!avisado)
                {
                    avisado = true;
                    MelonLogger.Warning("[DificultadJugador] " + ex);
                }
            }
        }

        private static void Pintar(Il2Cpp.PlayerSelectionMenu m)
        {
            if (m == null)
            {
                return;
            }
            var icono = m.headerDifficultyIcon;
            if (icono == null || !icono.gameObject.activeInHierarchy)
            {
                return;      // otra pagina, o "No Part": no se ve nada
            }
            IntPtr clave = m.Pointer;
            if (!textos.TryGetValue(clave, out Il2CppTMPro.TextMeshProUGUI t) || t == null)
            {
                t = Crear(m, icono);
                if (t == null)
                {
                    return;
                }
                textos[clave] = t;
                puestos[clave] = null;
            }
            string texto = Ajustes.MostrarDificultad ? Valor(m) : "";
            if (!puestos.TryGetValue(clave, out string antes) || texto != antes)
            {
                puestos[clave] = texto;
                t.text = texto;
            }
        }

        // "" mientras no se sabe, o si no hay archivo que leer.
        private static string Valor(Il2Cpp.PlayerSelectionMenu m)
        {
            Il2Cpp.SongEntry c = m.field_Private_SongEntry_0;      // songEntry
            if (c == null || c.isEnc || c.isSng)
            {
                return "";      // cifrada o empaquetada: no hay chart suelto
            }
            string pista = SeleccionJugador.Pista(
                m.field_Private_EnumPublicSealedvaGuBaRhSiGuDrSiKePrUnique_0.ToString());   // instrument
            int nivel = SeleccionJugador.IndiceDificultad(
                m.field_Private_EnumPublicSealedvaNoEaMeHaExCo7vUnique_0.ToString());      // difficulty
            if (pista == null || nivel < 0)
            {
                return "";
            }
            Dificultad.Perfil p = PerfilChart.Pedir(c.ChartPath, c.IsMIDIChart, pista, nivel,
                                                    out bool listo);
            return p != null && p.nota >= 0 ? p.nota.ToString() : "";
        }

        private static Il2CppTMPro.TextMeshProUGUI Crear(Il2Cpp.PlayerSelectionMenu m,
                                                         Il2Cpp.DifficultyDisplay icono)
        {
            Il2CppTMPro.TextMeshProUGUI modelo = m.difficultyHeaderText;
            RectTransform ri = icono.transform.TryCast<RectTransform>();
            if (modelo == null || ri == null)
            {
                return null;
            }
            Il2CppTMPro.TextMeshProUGUI t = UnityEngine.Object.Instantiate(modelo);
            t.name = "mod_difficulty";
            t.transform.SetParent(icono.transform, false);
            for (int k = t.transform.childCount - 1; k >= 0; k--)
            {
                UnityEngine.Object.Destroy(t.transform.GetChild(k).gameObject);
            }
            // Del mismo tamano en pantalla que "Expert", aunque el icono este
            // escalado.
            Vector3 a = modelo.transform.lossyScale;
            Vector3 b = icono.transform.lossyScale;
            t.transform.localScale = new Vector3(b.x != 0f ? a.x / b.x : 1f,
                                                 b.y != 0f ? a.y / b.y : 1f, 1f);
            t.transform.localRotation = Quaternion.identity;

            // Pegado al borde izquierdo del icono, creciendo hacia la izquierda.
            RectTransform r = t.rectTransform;
            r.anchorMin = new Vector2(0f, 0.5f);
            r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(1f, 0.5f);
            r.anchoredPosition = new Vector2(-10f, 0f);
            r.sizeDelta = new Vector2(160f, ri.rect.height > 0f ? ri.rect.height : 80f);
            t.alignment = Il2CppTMPro.TextAlignmentOptions.MidlineRight;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            t.text = "";
            return t;
        }
    }
}
