using System;
using MelonLoader;
using UnityEngine;

namespace CloneHeroMod
{
    // Avisa de que hay una version nueva del mod, aprovechando el texto de la
    // version del juego de la esquina superior derecha.
    //
    // Es un sitio ideal: se ve casi siempre —menus y lista de canciones—, no
    // durante la cancion, y no tapa nada. Y no hay que crear ni colocar nada.
    //
    // COMO SE LOCALIZA: ese texto no tiene una clase propia que buscar, asi
    // que se reconoce POR SU CONTENIDO — es el TextMeshProUGUI que dice lo
    // mismo que GlobalVariables.gameVersionString. Misma tactica que con la
    // "opcion abierta" de los menus y el criterio de orden activo.
    //
    // El texto original se guarda para devolverlo tal cual en cuanto el mod
    // este al dia: nunca se deja el aviso puesto de mas.
    public static class AvisoVersion
    {
        public const string Aviso = "Update CoolMod now!";

        private static Il2CppTMPro.TextMeshProUGUI etiqueta;
        private static string original;
        private static Il2CppTMPro.FontStyles estiloOriginal;
        private static bool avisoPuesto;
        private static readonly Buscador.Intento intento = new Buscador.Intento(29);
        private static int revision;

        public static void EscenaCambiada()
        {
            // El objeto puede haberse destruido con la escena; si sigue vivo se
            // vuelve a encontrar enseguida.
            etiqueta = null;
            avisoPuesto = false;
        }

        // Corre en cada fotograma de MENU. Con la etiqueta ya localizada, el
        // coste normal es un contador.
        public static void Tick()
        {
            try
            {
                if (etiqueta == null)
                {
                    Localizar();
                    return;
                }
                bool quiere = Actualizador.HayNueva;
                if (quiere == avisoPuesto)
                {
                    // Ya esta como toca. Se comprueba de vez en cuando por si
                    // el juego reescribio su texto por su cuenta.
                    if (++revision < 120)
                    {
                        return;
                    }
                    revision = 0;
                    if (!quiere || etiqueta.text == Aviso)
                    {
                        return;
                    }
                }
                Aplicar(quiere);
            }
            catch (Exception)
            {
                etiqueta = null;      // se habra destruido; se busca otra vez
            }
        }

        private static void Aplicar(bool aviso)
        {
            avisoPuesto = aviso;
            if (aviso)
            {
                etiqueta.text = Aviso;
                etiqueta.fontStyle = Il2CppTMPro.FontStyles.Bold;
            }
            else
            {
                etiqueta.text = original;
                etiqueta.fontStyle = estiloOriginal;
            }
        }

        private static void Localizar()
        {
            if (!intento.Toca())
            {
                return;
            }
            try
            {
                Il2Cpp.GlobalVariables g = Il2Cpp.GlobalVariables.instance;
                string version = g != null ? g.gameVersionString : null;
                if (string.IsNullOrEmpty(version))
                {
                    intento.Fallo();
                    return;
                }
                var textos = UnityEngine.Object.FindObjectsOfType<Il2CppTMPro.TextMeshProUGUI>();
                for (int i = 0; i < textos.Length; i++)
                {
                    if (textos[i] == null)
                    {
                        continue;
                    }
                    string t = textos[i].text;
                    // Puede traer adornos alrededor de la version, asi que se
                    // acepta que la contenga, no solo que sea igual.
                    if (string.IsNullOrEmpty(t)
                        || t.IndexOf(version, StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }
                    etiqueta = textos[i];
                    original = t;
                    estiloOriginal = textos[i].fontStyle;
                    intento.Exito();
                    MelonLogger.Msg("[Aviso] etiqueta de version localizada ('" + t + "')");
                    return;
                }
                intento.Fallo();
            }
            catch (Exception)
            {
                intento.Fallo();
            }
        }
    }
}
