using System;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace CloneHeroMod
{
    // Reproduce un sonido propio al terminar una cancion.
    //
    // El archivo se busca en PlayerData\Custom\Sounds con el nombre "yourock"
    // (cualquier extension que soporte BASS: .opus, .ogg, .mp3, .wav).
    //
    // No se parchea nada: se vigila la pantalla de fin de cancion y se dispara
    // cuando pasa de oculta a visible. Es mas robusto que enganchar un metodo,
    // y ya sabemos por experiencia que parchear de mas en esta version sale
    // caro.
    public static class SfxFinDeCancion
    {
        public const string NombreSonido = "yourock";

        private static Il2Cpp.EndOfSong pantalla;
        private static PropertyInfo propCanvas;
        private static bool visibleAntes;
        private static bool avisadoSinArchivo;

        public static void Tick()
        {
            try
            {
                if (!Ajustes.SfxFinActivo)
                {
                    return;
                }
                GameObject canvas = CanvasDeFin();
                bool visible = canvas != null && canvas.activeInHierarchy;

                if (visible && !visibleAntes)
                {
                    if (!SonidosPersonalizados.Reproducir(NombreSonido) && !avisadoSinArchivo)
                    {
                        avisadoSinArchivo = true;
                        MelonLogger.Msg("[SfxFin] no se pudo reproducir '" + NombreSonido
                            + "'. Deja el archivo en " + SonidosPersonalizados.Carpeta);
                    }
                }
                visibleAntes = visible;
            }
            catch (Exception)
            {
            }
        }

        // canvasObject es un campo publico de EndOfSong (sin ofuscar).
        private static GameObject CanvasDeFin()
        {
            if (pantalla == null)
            {
                pantalla = UnityEngine.Object.FindObjectOfType<Il2Cpp.EndOfSong>();
                if (pantalla == null)
                {
                    return null;
                }
                propCanvas = null;
            }
            if (propCanvas == null)
            {
                propCanvas = pantalla.GetType().GetProperty("canvasObject",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (propCanvas == null)
                {
                    return null;
                }
            }
            return propCanvas.GetValue(pantalla) as GameObject;
        }
    }
}
