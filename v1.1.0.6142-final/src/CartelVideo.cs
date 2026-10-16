using System;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.Video;

namespace CloneHeroMod
{
    // Un video .webm como cartel de Note Streak o SP Ready: cualquier .webm
    // dentro de la carpeta del cartel (Custom/Textures/cool_note_streak o
    // cool_star_power), se llame como se llame. Tiene prioridad sobre los
    // fotogramas de esa misma carpeta y sobre la imagen suelta.
    //
    // Va DENTRO de la carpeta, no suelto en Textures, para que cada cartel
    // tenga todo lo suyo en un sitio y el nombre del archivo no importe: se
    // puede dejar el video tal como sale del programa con que se hizo.
    //
    // POR QUE. Un usuario probo animaciones por fotogramas y la carga se hacia
    // eterna: cada fotograma se guarda entero en memoria (a 3840x2160, unos
    // 33 MB), asi que 61 fotogramas eran unos 2 GB. Un video se decodifica
    // sobre la marcha: no hay nada que cargar y ocupa lo que un fotograma. Sus
    // 61 fotogramas de 800x450 en webm VP8 con transparencia pesan 232 KB.
    //
    // COMO. El reproductor del propio juego, con las mismas piezas que usa el
    // (todas salen en el volcado ISIL: Prepare, Play, Pause, set_time,
    // get_length, get_texture...). Modo APIOnly: decodifica en su textura y se
    // la pasamos al RawImage del cartel. Se prepara al montar el cartel, al
    // empezar la cancion, y se queda parado en el principio; al saltar el
    // cartel se reproduce una vez y al acabar se rebobina, listo para la
    // siguiente. Prepararlo en el momento seria un retraso visible.
    //
    // La transparencia depende de que el decodificador VP8 de Unity respete
    // el canal alfa al abrir el archivo directamente. Si no lo respeta, el
    // video sale con fondo negro.
    public class CartelVideo
    {
        public const string Extension = ".webm";

        private VideoPlayer vp;
        private bool fallido;
        private double largo;

        // La ruta del video del cartel, o null si no hay. Si hubiera varios
        // .webm en la carpeta, el primero por nombre, y se avisa.
        public static string Ruta(string nombre)
        {
            try
            {
                string raiz = RutasJuego.CarpetaCustom(TexturasPersonalizadas.NombreCarpeta);
                if (string.IsNullOrEmpty(raiz)) return null;
                string carpeta = Path.Combine(raiz, nombre);
                if (!Directory.Exists(carpeta)) return null;
                string[] videos = Directory.GetFiles(carpeta, "*" + Extension);
                if (videos.Length == 0) return null;
                Array.Sort(videos, StringComparer.OrdinalIgnoreCase);
                if (videos.Length > 1)
                {
                    MelonLogger.Warning("[Carteles] hay " + videos.Length.ToString()
                        + " videos en " + nombre + "; se usa " + Path.GetFileName(videos[0]));
                }
                return videos[0];
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Crea el reproductor en "anfitrion", que tiene que estar activo: un
        // VideoPlayer en un objeto inactivo no se prepara.
        public bool Montar(GameObject anfitrion, string ruta)
        {
            try
            {
                vp = anfitrion.AddComponent<VideoPlayer>();
                vp.playOnAwake = false;
                vp.renderMode = VideoRenderMode.APIOnly;
                vp.audioOutputMode = VideoAudioOutputMode.None;
                vp.isLooping = false;
                vp.skipOnDrop = true;
                vp.waitForFirstFrame = true;
                vp.url = ruta;
                vp.Prepare();
                MelonLogger.Msg("[Carteles] video propio: " + Path.GetFileName(ruta));
                return true;
            }
            catch (Exception ex)
            {
                fallido = true;
                MelonLogger.Warning("[Carteles] video " + Path.GetFileName(ruta) + ": " + ex.Message);
                return false;
            }
        }

        // La textura donde el reproductor va pintando, o null mientras no
        // haya ninguna.
        public Texture Textura
        {
            get
            {
                if (vp == null || fallido) return null;
                try { return vp.texture; }
                catch (Exception) { return null; }
            }
        }

        public void Arrancar()
        {
            if (vp == null || fallido) return;
            try
            {
                vp.time = 0;
                vp.Play();
            }
            catch (Exception ex)
            {
                fallido = true;
                MelonLogger.Warning("[Carteles] reproducir video: " + ex.Message);
            }
        }

        // Si ya se reproducio entero, "t" segundos despues de arrancarlo.
        public bool Terminado(float t)
        {
            if (vp == null || fallido) return true;
            try
            {
                if (largo <= 0) largo = vp.length;
                if (largo > 0) return t >= largo;
                // sin duracion conocida: cuando deje de reproducirse
                return t > 0.25f && !vp.isPlaying;
            }
            catch (Exception)
            {
                return true;
            }
        }

        // LA PAUSA DEL JUEGO. El reproductor no se entera: el juego pausa
        // poniendo Time.timeScale a 0. La animacion del cartel se congela sola
        // (cuenta con Time.deltaTime), pero el video seguia corriendo y al
        // reanudar iba por delante. Se llama en cada fotograma mientras el
        // cartel esta en pantalla.
        private bool pausado;

        public void SeguirPausa()
        {
            if (vp == null || fallido) return;
            bool pausa = Time.timeScale < 0.001f;
            if (pausa == pausado) return;
            pausado = pausa;
            try
            {
                if (pausa) vp.Pause(); else vp.Play();
            }
            catch (Exception)
            {
            }
        }

        // Parado y de vuelta al principio, preparado para la proxima vez.
        public void Rebobinar()
        {
            if (vp == null || fallido) return;
            try
            {
                vp.Pause();
                vp.time = 0;
            }
            catch (Exception)
            {
            }
        }
    }
}
