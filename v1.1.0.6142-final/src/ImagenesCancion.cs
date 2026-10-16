using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace CloneHeroMod
{
    // Imagenes fijas durante la cancion: todo lo que haya en
    // Custom/Textures/during_song se pinta estirado a toda la pantalla, sea
    // cual sea su resolucion y la del jugador, mientras se toca, y se va
    // cuando la cancion acaba.
    //
    // ENCIMA DE TODO, MENOS DE DOS COSAS: el menu de pausa y el fundido a
    // negro del final, y por debajo de los carteles del mod. Lo coloca Capas.
    //
    // DESAPARECER AL ACABAR sale solo: el lienzo pertenece a la escena de la
    // cancion, y al acabar el juego carga otra (EndOfSong). Lo mismo al
    // salir al menu.
    //
    // ORDEN ENTRE ELLAS: el natural de los nombres (1, 2 ... 10), y cada una
    // queda por encima de las anteriores, como capas numeradas de abajo
    // arriba.
    //
    // VIDEOS TAMBIEN: un .webm en la carpeta se reproduce en bucle, mudo y a
    // pantalla completa durante toda la cancion, en su sitio dentro del orden
    // (un 1.webm de fondo con un 2.png encima, por ejemplo). Con el mismo
    // reproductor que los carteles (ver CartelVideo), preparado al cargar la
    // cancion. Y SE PARA CON LA PAUSA: el reproductor no se entera de la
    // pausa del juego, que lo que hace es poner Time.timeScale a 0 (lo pone
    // GameManager, y PauseMenu lo devuelve a 1). Se mira ese valor en cada
    // fotograma y se pausa o reanuda con el.
    public static class ImagenesCancion
    {
        public const string NombreCarpeta = "during_song";

        // Mientras Capas no sepa donde van la pausa y el fundido: por debajo
        // de los carteles del mod (31000) y por encima de casi cualquier cosa.
        private const int OrdenProvisional = 30000;

        // Cada capa, en orden: su archivo y, si es imagen, su textura (null en
        // los videos, que no se cargan: se decodifican al reproducirse).
        private static string[] rutas;
        private static Texture2D[] imagenes;
        private static bool cargadas;

        private static readonly List<RawImage> pantallas = new List<RawImage>();
        private static readonly List<UnityEngine.Video.VideoPlayer> videos =
            new List<UnityEngine.Video.VideoPlayer>();
        private static bool pausados;

        private static bool activo;
        private static GameObject raiz;
        private static Canvas lienzo;

        // ------------------------------------------------------------ carga -
        // Desde los menus, nunca durante la cancion.
        public static void Cargar()
        {
            if (cargadas)
            {
                return;
            }
            cargadas = true;
            try
            {
                string carpeta = Carpeta();
                firmaCargada = CartelImagen.FirmaCarpeta(carpeta);
                if (carpeta == null || !Directory.Exists(carpeta))
                {
                    return;
                }
                List<string> archivos = new List<string>();
                foreach (string f in Directory.GetFiles(carpeta))
                {
                    if (CartelImagen.EsImagen(f)
                        || string.Equals(Path.GetExtension(f), CartelVideo.Extension,
                                         StringComparison.OrdinalIgnoreCase))
                    {
                        archivos.Add(f);
                    }
                }
                archivos.Sort(CartelImagen.OrdenNatural);
                List<string> buenas = new List<string>();
                List<Texture2D> texturas = new List<Texture2D>();
                for (int i = 0; i < archivos.Count; i++)
                {
                    Texture2D t = null;
                    if (CartelImagen.EsImagen(archivos[i]))
                    {
                        t = CartelImagen.Leer(archivos[i]);
                        if (t == null) continue;
                    }
                    buenas.Add(archivos[i]);
                    texturas.Add(t);
                }
                if (buenas.Count == 0)
                {
                    return;
                }
                rutas = buenas.ToArray();
                imagenes = texturas.ToArray();
                string[] nombres = new string[rutas.Length];
                for (int i = 0; i < rutas.Length; i++) nombres[i] = Path.GetFileName(rutas[i]);
                MelonLogger.Msg("[DuranteCancion] " + NombreCarpeta + ", " + rutas.Length.ToString()
                    + " capa(s), de abajo arriba: " + string.Join(", ", nombres));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[DuranteCancion] " + ex.Message);
            }
        }

        private static string firmaCargada;

        private static string Carpeta()
        {
            string raizTexturas = RutasJuego.CarpetaCustom(TexturasPersonalizadas.NombreCarpeta);
            return string.IsNullOrEmpty(raizTexturas) ? null : Path.Combine(raizTexturas, NombreCarpeta);
        }

        // Las texturas viejas se destruyen: llevan HideAndDontSave, asi que
        // Unity no las soltaria nunca por su cuenta.
        private static void Descartar()
        {
            if (imagenes != null)
            {
                for (int i = 0; i < imagenes.Length; i++)
                {
                    if (imagenes[i] != null) UnityEngine.Object.Destroy(imagenes[i]);
                }
            }
            imagenes = null;
            rutas = null;
            cargadas = false;
        }

        // ------------------------------------------------------------ escena -
        public static void EscenaCambiada(string nombre, bool enJuego)
        {
            raiz = null;      // lo destruye Unity con la escena
            lienzo = null;
            pantallas.Clear();
            videos.Clear();
            pausados = false;

            // RECARGA AL VOLVER AL MENU PRINCIPAL, SI CAMBIO ALGO. Antes las
            // imagenes se leian una vez por sesion, y cambiar la carpeta con el
            // juego abierto no tenia efecto hasta reiniciarlo. Luego se
            // releian siempre, y con imagenes grandes salir de una cancion
            // tardaba. Ahora se compara la firma de la carpeta (nombres,
            // tamanos y fechas) y solo se relee si es distinta.
            if (nombre == Buscador.EscenaMenu)
            {
                string firma = CartelImagen.FirmaCarpeta(Carpeta());
                if (firma != firmaCargada)
                {
                    Descartar();
                    Cargar();
                }
            }
            if (enJuego && !cargadas)
            {
                Cargar();
            }
            activo = enJuego && rutas != null && rutas.Length > 0;
            if (activo)
            {
                Montar();
            }
        }

        // Solo hay trabajo por fotograma si hay videos.
        public static void Tick()
        {
            if (!activo || lienzo == null || videos.Count == 0)
            {
                return;
            }
            Videos();
        }

        // ------------------------------------------------------------ montar -
        private static void Montar()
        {
            try
            {
                raiz = new GameObject("DuringSongImages");
                lienzo = raiz.AddComponent<Canvas>();
                lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
                lienzo.sortingOrder = OrdenProvisional;
                Capas.Registrar(lienzo, Capas.Fondo);
                CanvasScaler escala = raiz.AddComponent<CanvasScaler>();
                escala.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                escala.referenceResolution = new Vector2(1920f, 1080f);
                for (int i = 0; i < rutas.Length; i++)
                {
                    // a toda la pantalla, sea cual sea su resolucion
                    RawImage img = CartelImagen.Poner(raiz.transform, imagenes[i], true);
                    img.gameObject.name = "Capa" + (i + 1).ToString();
                    if (imagenes[i] == null)
                    {
                        Video(img, rutas[i]);
                    }
                }
                MelonLogger.Msg("[DuranteCancion] " + rutas.Length.ToString()
                    + " capa(s) en pantalla, " + videos.Count.ToString() + " de video");
            }
            catch (Exception ex)
            {
                activo = false;
                MelonLogger.Error("[DuranteCancion] al montar: " + ex);
            }
        }

        // Un video en bucle sobre esta capa. Hasta que de su primer fotograma
        // la capa no se pinta: un RawImage sin textura sale blanco.
        private static void Video(RawImage img, string ruta)
        {
            try
            {
                img.enabled = false;
                UnityEngine.Video.VideoPlayer vp =
                    img.gameObject.AddComponent<UnityEngine.Video.VideoPlayer>();
                vp.playOnAwake = false;
                vp.renderMode = UnityEngine.Video.VideoRenderMode.APIOnly;
                vp.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
                vp.isLooping = true;
                vp.skipOnDrop = true;
                vp.waitForFirstFrame = true;
                vp.url = ruta;
                vp.Play();      // si no esta preparado, Play lo prepara
                pantallas.Add(img);
                videos.Add(vp);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[DuranteCancion] video " + Path.GetFileName(ruta) + ": "
                    + ex.Message);
            }
        }

        // Cada fotograma: la textura de cada video a su capa, y la pausa del
        // juego a los reproductores.
        private static void Videos()
        {
            try
            {
                bool pausa = Time.timeScale < 0.001f;
                for (int i = 0; i < videos.Count; i++)
                {
                    UnityEngine.Video.VideoPlayer vp = videos[i];
                    if (vp == null) continue;
                    if (pausa != pausados)
                    {
                        if (pausa) vp.Pause(); else vp.Play();
                    }
                    Texture tex = vp.texture;
                    if (tex != null && pantallas[i].texture != tex)
                    {
                        pantallas[i].texture = tex;
                        pantallas[i].enabled = true;
                    }
                }
                pausados = pausa;
            }
            catch (Exception ex)
            {
                videos.Clear();      // nunca a costa de la cancion
                MelonLogger.Warning("[DuranteCancion] videos: " + ex.Message);
            }
        }
    }
}
