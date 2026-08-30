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
        private static bool avisadoFallo;
        private static readonly Buscador.Intento intento = new Buscador.Intento(13);

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
                    if (Fallada())
                    {
                        // Al fallar, el juego ya suena lo suyo
                        // (gh3_sudden_death). Felicitar ahi encima sobraria.
                        if (!avisadoFallo)
                        {
                            avisadoFallo = true;
                            MelonLogger.Msg("[SfxFin] cancion fallada: no se felicita");
                        }
                    }
                    else if (!SonidosPersonalizados.Reproducir(NombreSonido) && !avisadoSinArchivo)
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

        // Si la cancion termino por fallar.
        //
        // GlobalVariables esta sin ofuscar y ademas es un singleton con
        // instance publico, asi que no hay que buscar el objeto ni tirar de
        // reflexion: sale comprobado en compilacion.
        //
        // Ante cualquier duda se devuelve false, o sea que suene: es peor
        // callar un sonido que el jugador espera que colarlo de mas.
        private static bool Fallada()
        {
            try
            {
                Il2Cpp.GlobalVariables g = Il2Cpp.GlobalVariables.instance;
                return g != null && g.failed;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // canvasObject es un campo publico de EndOfSong (sin ofuscar).
        private static GameObject CanvasDeFin()
        {
            if (pantalla == null)
            {
                // Los reintentos se espacian solos: ver Buscador.
                if (!intento.Toca())
                {
                    return null;
                }
                pantalla = UnityEngine.Object.FindObjectOfType<Il2Cpp.EndOfSong>();
                if (pantalla == null)
                {
                    intento.Fallo();
                    return null;
                }
                intento.Exito();
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
