using System;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.Video;

namespace CloneHeroMod
{
    // Un video .webm como cartel de Note Streak o SP Ready, en vez de una
    // carpeta de fotogramas: Custom/Textures/cool_note_streak.webm o
    // cool_star_power.webm. Tiene prioridad sobre la carpeta y la imagen.
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

        // La ruta del video del cartel, o null si no hay.
        public static string Ruta(string nombre)
        {
            try
            {
                string carpeta = RutasJuego.CarpetaCustom(TexturasPersonalizadas.NombreCarpeta);
                if (string.IsNullOrEmpty(carpeta)) return null;
                string ruta = Path.Combine(carpeta, nombre + Extension);
                return File.Exists(ruta) ? ruta : null;
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
