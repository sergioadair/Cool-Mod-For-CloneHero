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
    // CUANDO SUENA. Costo dos intentos dar con el momento, y los dos
    // ensenaron algo:
    //
    //  1. Vigilando la pantalla de resultados hasta verla visible. Sonaba
    //     tardisimo, y no por el fundido: Diagnostico.OnUpdate NO llama a este
    //     Tick mientras la escena es la de juego —ahi solo corre el cartel de
    //     racha, por rendimiento—, asi que no nos enterabamos hasta mucho
    //     despues. Y aun entonces quedaba un segundo largo esperando al canvas.
    //
    //  2. Enganchando la corrutina de fundido de EndOfSong. No salto nunca: esa
    //     corrutina desvanece su propio panel, no la pantalla.
    //
    // Lo que resolvio el log fue ver que EndOfSong ES UNA ESCENA APARTE:
    //
    //     22:04:43  escena: Gameplay  (mod en pausa)
    //     22:08:41  escena: EndOfSong
    //
    // El fundido a negro pasa dentro de Gameplay, donde el mod esta parado a
    // proposito. Pero la escena de fin se carga justo cuando ese fundido
    // termina, asi que dispararlo al cargarla es lo mas pronto que se puede
    // sonar sin poner un sondeo por fotograma durante la cancion.
    //
    // La vigilancia de la pantalla se queda de red de seguridad por si algun
    // dia esa escena cambia de nombre: sonaria tarde, pero sonaria.
    public static class SfxFinDeCancion
    {
        public const string NombreSonido = "yourock";

        // La escena que el juego carga al acabar la cancion. Es una escena
        // propia, no un panel dentro de Gameplay.
        public const string EscenaFin = "EndOfSong";

        private static volatile bool yaSono;
        private static Il2Cpp.EndOfSong pantalla;
        private static PropertyInfo propCanvas;
        private static bool visibleAntes;
        private static bool avisadoSinArchivo;
        private static bool avisadoFallo;
        private static readonly Buscador.Intento intento = new Buscador.Intento(13);

        // AQUI es donde suena. Al entrar a una cancion se olvida lo de la
        // anterior, y al cargarse la escena de fin se dispara.
        public static void EscenaCambiada(string escena)
        {
            if (escena == Buscador.EscenaJuego)
            {
                yaSono = false;
                visibleAntes = false;
                return;
            }
            if (escena == EscenaFin)
            {
                Sonar();
            }
        }

        // Devuelve si de verdad sono. Se llama desde el parche y desde la red
        // de seguridad, y solo la primera gana.
        private static bool Sonar()
        {
            if (yaSono || !Ajustes.SfxFinActivo)
            {
                return false;
            }
            yaSono = true;
            if (Fallada())
            {
                // Al fallar, el juego ya suena lo suyo (gh3_sudden_death).
                // Felicitar ahi encima sobraria.
                if (!avisadoFallo)
                {
                    avisadoFallo = true;
                    MelonLogger.Msg("[SfxFin] cancion fallada: no se felicita");
                }
                return false;
            }
            if (!SonidosPersonalizados.Reproducir(NombreSonido) && !avisadoSinArchivo)
            {
                avisadoSinArchivo = true;
                MelonLogger.Msg("[SfxFin] no se pudo reproducir '" + NombreSonido
                    + "'. Deja el archivo en " + SonidosPersonalizados.Carpeta);
            }
            return true;
        }

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

                if (visible && !visibleAntes && Sonar())
                {
                    MelonLogger.Msg("[SfxFin] sono por la pantalla de resultados,"
                        + " no por el fundido");
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
