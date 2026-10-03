using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace CloneHeroMod
{
    // Fondos de menu propios, tomados de PlayerData\Custom\Menu Backgrounds, y
    // slideshow opcional entre ellos.
    //
    // El juego NO soporta fondos personalizados: sus 14 fondos son campos
    // Texture2D fijos de MenuBackground y el ajuste menu_background va de 0 a
    // 13. Se le sube el maximo y, cuando el valor cae por encima de 13, se le
    // pone nuestra textura al RawImage del fondo.
    //
    // VIDEOS TAMBIEN. Van en la misma carpeta y la misma lista que las
    // imagenes; la unica diferencia es de donde sale la textura: en vez de un
    // archivo cargado una vez, la que va pintando un VideoPlayer.
    //
    // Se usa el reproductor del propio juego y de la misma forma que lo usa
    // el, porque lo que el juego no usa puede venir recortado de la build. En
    // el volcado ISIL salen todas las piezas que hacen falta, y en uso:
    // get_texture (18 llamadas), set_url, set_renderMode, set_audioOutputMode,
    // set_isLooping, Play, Stop. El modo es APIOnly: el reproductor decodifica
    // en su propia textura y se la pasamos al RawImage del fondo, sin
    // RenderTexture de por medio.
    //
    // Mudo, en bucle, y parado durante la cancion: alli el mod no gasta.
    public static class FondosPersonalizados
    {
        public const int FondosDeSerie = 14;      // indices 0..13

        // Los mismos formatos que el juego admite para sus fondos de video
        // en Windows, segun su propio README de Custom.
        public static readonly string[] ExtensionesVideo =
            { ".mp4", ".webm", ".avi", ".ogv", ".mpeg", ".mpg", ".mov", ".m4v" };

        // Si un video no da su primer fotograma en este tiempo, se da por
        // perdido. El README del juego avisa de que hay equipos que no
        // reproducen video en absoluto; quedarse esperando dejaria el fondo
        // en negro para siempre.
        public const float EsperaPrimerFotograma = 8f;

        private static string[] rutas;
        private static Texture2D[] texturas;

        private static object ajusteFondo;        // el GameSetting menu_background
        private static PropertyInfo propValor;    // prop_T_0 = valor actual
        private static PropertyInfo propMaximo;   // prop_T_1 = maximo

        private static PropertyInfo propRawImage;

        private static bool instalado;
        private static int indiceSlideshow = -1;
        private static float proximoCambio;
        private static int valorVisto = -1;       // el del menu en el tick anterior
        private static float serieHasta;          // un fondo de serie elegido a mano

        public static int Cantidad
        {
            get
            {
                Escanear();
                return rutas.Length;
            }
        }

        public static string Carpeta
        {
            get { return RutasJuego.CarpetaCustom("Menu Backgrounds"); }
        }

        // ------------------------------------------------------------ escaneo
        private static void Escanear()
        {
            if (rutas != null)
            {
                return;
            }
            List<string> lista = new List<string>();
            try
            {
                string carpeta = Carpeta;
                Directory.CreateDirectory(carpeta);
                string[] archivos = Directory.GetFiles(carpeta);
                for (int i = 0; i < archivos.Length; i++)
                {
                    string ext = Path.GetExtension(archivos[i]).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg"
                        || Array.IndexOf(ExtensionesVideo, ext) >= 0)
                    {
                        lista.Add(archivos[i]);
                    }
                }
                lista.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Fondos] escaneo: " + ex.Message);
            }
            rutas = lista.ToArray();
            texturas = new Texture2D[rutas.Length];
        }

        // ---------------------------------------------------------- instalar -
        public static void Instalar()
        {
            try
            {
                Escanear();
                if (rutas.Length == 0)
                {
                    MelonLogger.Msg("[Fondos] no hay imagenes ni videos en " + Carpeta);
                    return;
                }
                if (!ResolverAjuste())
                {
                    MelonLogger.Warning("[Fondos] no se localizo el ajuste menu_background");
                    return;
                }
                propMaximo.SetValue(ajusteFondo, FondosDeSerie - 1 + rutas.Length);
                instalado = true;
                MelonLogger.Msg("[Fondos] " + rutas.Length.ToString() + " fondo(s); maximo -> "
                    + (FondosDeSerie - 1 + rutas.Length).ToString());

                // Restaurar el fondo propio: el juego ya recorto su ajuste al
                // cargar (su esquema dice maximo 13), asi que el valor bueno
                // esta en nuestra clave de [mods].
                int guardado = Ajustes.LeerFondoGuardado();
                if (guardado >= FondosDeSerie && guardado < FondosDeSerie + rutas.Length)
                {
                    propValor.SetValue(ajusteFondo, guardado);
                    MelonLogger.Msg("[Fondos] restaurado el fondo " + guardado.ToString());
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Fondos] " + ex);
            }
        }

        // menu_background es el UNICO ajuste con maximo 13 y minimo 0.
        private static bool ResolverAjuste()
        {
            Type t = Ofuscado.Tipo("ʹʺʽˁʽˁˀʼʶʷʼ");
            if (t == null)
            {
                return false;
            }
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            MemberInfo[] ms = t.GetMembers(f);
            for (int i = 0; i < ms.Length; i++)
            {
                object ajuste;
                try
                {
                    if (ms[i] is PropertyInfo p && p.GetIndexParameters().Length == 0)
                    {
                        ajuste = p.GetValue(null);
                    }
                    else if (ms[i] is FieldInfo c)
                    {
                        ajuste = c.GetValue(null);
                    }
                    else
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }
                if (ajuste == null || ajuste is string || ajuste.GetType().IsPrimitive)
                {
                    continue;
                }
                PropertyInfo pMax = ajuste.GetType().GetProperty("prop_T_1",
                    BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo pMin = ajuste.GetType().GetProperty("prop_T_2",
                    BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo pVal = ajuste.GetType().GetProperty("prop_T_0",
                    BindingFlags.Public | BindingFlags.Instance);
                if (pMax == null || pMin == null || pVal == null
                    || pMax.PropertyType != typeof(int))
                {
                    continue;
                }
                try
                {
                    if ((int)pMax.GetValue(ajuste) == FondosDeSerie - 1
                        && (int)pMin.GetValue(ajuste) == 0)
                    {
                        ajusteFondo = ajuste;
                        propMaximo = pMax;
                        propValor = pVal;
                        return true;
                    }
                }
                catch (Exception)
                {
                }
            }
            return false;
        }

        // Nombre del archivo del fondo elegido, o null si es uno de serie.
        // Lo usa el menu de Video para etiquetar la opcion.
        public static string NombreDelSeleccionado()
        {
            try
            {
                if (!instalado)
                {
                    return null;
                }
                int valor = (int)propValor.GetValue(ajusteFondo);
                if (valor < FondosDeSerie)
                {
                    return null;
                }
                int i = valor - FondosDeSerie;
                if (i < 0 || i >= rutas.Length)
                {
                    return null;
                }
                return Path.GetFileNameWithoutExtension(rutas[i]);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // -------------------------------------------------------------- tick -
        public static void Tick()
        {
            try
            {
                if (!instalado)
                {
                    return;
                }
                int valor = (int)propValor.GetValue(ajusteFondo);
                bool slideshow = Ajustes.SlideshowActivo && rutas.Length > 1;

                // ELEGIR A MANO CON EL SLIDESHOW PUESTO. Antes el slideshow no
                // miraba el valor del menu, asi que cambiar el fondo desde
                // Settings no hacia nada. Ahora, si el valor cambia, se pone
                // ese fondo y el slideshow sigue desde ahi: el siguiente cambio
                // llega al cabo de un intervalo completo. Si se eligio uno de
                // serie, se ve ese mismo intervalo y luego vuelven los propios.
                if (slideshow && valorVisto >= 0 && valor != valorVisto)
                {
                    float ahora = Time.realtimeSinceStartup;
                    proximoCambio = ahora + Ajustes.SlideshowSegundos;
                    if (valor >= FondosDeSerie && valor < FondosDeSerie + rutas.Length)
                    {
                        indiceSlideshow = valor - FondosDeSerie;
                        serieHasta = 0;
                        fondoGuardado = valor;
                        Ajustes.GuardarFondo(valor);
                    }
                    else
                    {
                        serieHasta = proximoCambio;
                    }
                    MelonLogger.Msg("[Fondos] elegido a mano con el slideshow puesto: "
                        + valor.ToString() + "; el slideshow sigue desde aqui");
                }
                valorVisto = valor;

                int indice;
                if (slideshow && Time.realtimeSinceStartup < serieHasta)
                {
                    Detener();
                    DevolverMateriales();
                    return;
                }
                if (slideshow)
                {
                    indice = IndiceSlideshow();
                }
                else if (valor >= FondosDeSerie)
                {
                    indice = valor - FondosDeSerie;
                    // Se persiste en cuanto cambia, para que sobreviva al
                    // recorte que hace el juego al arrancar.
                    if (valor != fondoGuardado)
                    {
                        fondoGuardado = valor;
                        Ajustes.GuardarFondo(valor);
                    }
                }
                else
                {
                    // Fondo de serie: hay que devolverle su material, o se
                    // quedaria con el nuestro y perderia su efecto.
                    Detener();
                    DevolverMateriales();
                    return;
                }

                Texture tex = TexturaDe(indice);
                if (tex == null)
                {
                    // Un video que no arranca se queda con el fondo del juego
                    // en vez de en negro. Uno que esta cargando, se espera.
                    if (rotos.Contains(indice))
                    {
                        DevolverMateriales();
                    }
                    return;
                }
                List<UnityEngine.UI.RawImage> destinos = RawImagesDelFondo();
                if (destinos.Count == 0)
                {
                    if (!avisadoSinDestino)
                    {
                        avisadoSinDestino = true;
                        MelonLogger.Warning("[Fondos] no se localizo el RawImage del fondo");
                    }
                    return;
                }
                foreach (UnityEngine.UI.RawImage destino in destinos)
                {
                // El RawImage dibuja a traves de un material (MenuBackground
                // tiene defaultMaterial y surferMaterial) cuyo shader usa su
                // propia textura, asi que asignar destino.texture no se ve.
                // Con material nulo se usa el shader de UI por defecto, que si
                // pinta la textura del RawImage. Se guarda el original para
                // devolverlo al volver a un fondo de serie.
                // El material original se guarda POR INSTANCIA: hay dos
                // MenuBackground y cada uno trae el suyo (animatedBackground2 y
                // defaultBackground). Con uno solo, al volver a un fondo de
                // serie le habriamos puesto a uno el material del otro.
                int id = destino.GetInstanceID();
                if (destino.material != null)
                {
                    if (!materialesOriginales.ContainsKey(id))
                    {
                        materialesOriginales[id] = destino.material;
                        MelonLogger.Msg("[Fondos] material de " + destino.gameObject.name
                                        + ": " + destino.material.name);
                    }
                    destino.material = null;
                }

                if (destino.texture != tex)
                {
                    destino.texture = tex;
                    if (ultimoAplicado != indice)
                    {
                        ultimoAplicado = indice;
                        MelonLogger.Msg("[Fondos] aplicado " + Path.GetFileName(rutas[indice]));
                    }
                }
                }
            }
            catch (Exception ex)
            {
                if (!avisadoError)
                {
                    avisadoError = true;
                    MelonLogger.Error("[Fondos] tick: " + ex);
                }
            }
        }

        private static bool avisadoSinDestino;
        private static bool avisadoError;
        private static int ultimoAplicado = -1;
        private static readonly Dictionary<int, Material> materialesOriginales =
            new Dictionary<int, Material>();

        // Va rotando entre las imagenes cada N segundos.
        private static int IndiceSlideshow()
        {
            float ahora = Time.realtimeSinceStartup;
            if (indiceSlideshow < 0)
            {
                indiceSlideshow = UnityEngine.Random.Range(0, rutas.Length);
                proximoCambio = ahora + Ajustes.SlideshowSegundos;
            }
            else if (ahora >= proximoCambio)
            {
                proximoCambio = ahora + Ajustes.SlideshowSegundos;
                int siguiente = UnityEngine.Random.Range(0, rutas.Length - 1);
                if (siguiente >= indiceSlideshow)
                {
                    siguiente++;      // nunca repetir la misma seguida
                }
                indiceSlideshow = siguiente;
            }
            if (indiceSlideshow >= rutas.Length)
            {
                indiceSlideshow = 0;
            }
            return indiceSlideshow;
        }

        private static void DevolverMateriales()
        {
            if (materialesOriginales.Count == 0)
            {
                return;
            }
            foreach (UnityEngine.UI.RawImage r in RawImagesDelFondo())
            {
                Material suyo;
                if (materialesOriginales.TryGetValue(r.GetInstanceID(), out suyo)
                    && r.material != suyo)
                {
                    r.material = suyo;
                    ultimoAplicado = -1;
                }
            }
        }

        public static bool EsVideo(string ruta)
        {
            string ext = Path.GetExtension(ruta).ToLowerInvariant();
            return Array.IndexOf(ExtensionesVideo, ext) >= 0;
        }

        // La textura del fondo i: la imagen cargada, o el fotograma actual del
        // video. null si aun no hay nada que pintar.
        private static Texture TexturaDe(int i)
        {
            if (i < 0 || i >= rutas.Length)
            {
                return null;
            }
            if (!EsVideo(rutas[i]))
            {
                Detener();      // una imagen no necesita el reproductor
                return Textura(i);
            }
            if (rotos.Contains(i))
            {
                return null;
            }
            try
            {
                UnityEngine.Video.VideoPlayer vp = Reproductor();
                if (vp == null)
                {
                    return null;
                }
                if (urlActual != rutas[i])
                {
                    urlActual = rutas[i];
                    vp.url = rutas[i];
                    vp.Play();      // si no esta preparado, Play lo prepara
                    pedidoEn = Time.realtimeSinceStartup;
                }
                Texture t = vp.texture;
                if (t == null
                    && Time.realtimeSinceStartup - pedidoEn > EsperaPrimerFotograma)
                {
                    rotos.Add(i);
                    MelonLogger.Warning("[Fondos] el video " + Path.GetFileName(rutas[i])
                        + " no ha dado imagen en " + EsperaPrimerFotograma.ToString("0")
                        + " s; se deja el fondo del juego. Puede ser el formato o el equipo.");
                    Detener();
                }
                return t;
            }
            catch (Exception ex)
            {
                rotos.Add(i);
                MelonLogger.Warning("[Fondos] video " + Path.GetFileName(rutas[i]) + ": "
                    + ex.Message);
                Detener();
                reproductor = null;
                return null;
            }
        }

        // Un solo reproductor para todo, que sobrevive a los cambios de escena.
        private static UnityEngine.Video.VideoPlayer Reproductor()
        {
            if (reproductor != null)
            {
                return reproductor;
            }
            GameObject go = new GameObject("CoolModMenuVideo");
            UnityEngine.Object.DontDestroyOnLoad(go);
            UnityEngine.Video.VideoPlayer vp = go.AddComponent<UnityEngine.Video.VideoPlayer>();
            vp.playOnAwake = false;
            vp.renderMode = UnityEngine.Video.VideoRenderMode.APIOnly;
            vp.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
            vp.isLooping = true;
            vp.skipOnDrop = true;        // mejor saltar fotogramas que retrasarse
            vp.waitForFirstFrame = true;
            reproductor = vp;
            MelonLogger.Msg("[Fondos] reproductor de video creado");
            return vp;
        }

        // Para el video: al pasar a una imagen, a un fondo de serie o a la
        // cancion. Un video parado no decodifica, que es lo que cuesta.
        private static void Detener()
        {
            if (urlActual == null)
            {
                return;
            }
            urlActual = null;
            try
            {
                if (reproductor != null)
                {
                    reproductor.Stop();
                }
            }
            catch (Exception)
            {
                reproductor = null;
            }
        }

        // Durante la cancion el mod no gasta, y un video decodificando por
        // detras de la partida seria justo eso. Al volver al menu, el
        // siguiente Tick lo arranca solo.
        public static void EscenaCambiada(bool enJuego)
        {
            if (enJuego)
            {
                Detener();
            }
        }

        private static UnityEngine.Video.VideoPlayer reproductor;
        private static string urlActual;
        private static float pedidoEn;
        private static readonly HashSet<int> rotos = new HashSet<int>();

        private static Texture2D Textura(int i)
        {
            if (i < 0 || i >= rutas.Length)
            {
                return null;
            }
            if (texturas[i] != null)
            {
                return texturas[i];
            }
            try
            {
                byte[] datos = File.ReadAllBytes(rutas[i]);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!UnityEngine.ImageConversion.LoadImage(tex, datos))
                {
                    MelonLogger.Warning("[Fondos] no se pudo decodificar " + rutas[i]);
                    return null;
                }
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                tex.hideFlags = HideFlags.HideAndDontSave;
                texturas[i] = tex;
                return tex;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Fondos] " + ex.Message);
                return null;
            }
        }

        // El RawImage del fondo es un campo privado serializado de
        // MenuBackground; Il2CppInterop lo expone como propiedad con el mismo
        // nombre, que no esta ofuscado.
        // Puede haber VARIAS instancias de MenuBackground activas (el juego
        // tiene al menos una normal y otra de leaderboards), asi que se aplica
        // a todas: quedarse con la primera que devuelve FindObjectOfType
        // significaba estar pintando sobre una que no se ve.
        private static List<UnityEngine.UI.RawImage> cacheRawImages;
        private static readonly Buscador.Intento intento = new Buscador.Intento(7);

        // RENDIMIENTO: FindObjectsOfType es aun mas caro que la version en
        // singular, porque ademas reserva un array. La lista se guarda y solo
        // se rehace cuando toca (ver Buscador) o si alguna referencia murio.

        private static List<UnityEngine.UI.RawImage> RawImagesDelFondo()
        {
            if (cacheRawImages != null && !intento.Toca())
            {
                bool validas = cacheRawImages.Count > 0;
                for (int i = 0; validas && i < cacheRawImages.Count; i++)
                {
                    if (cacheRawImages[i] == null)
                    {
                        validas = false;
                    }
                }
                if (validas)
                {
                    return cacheRawImages;
                }
            }

            List<UnityEngine.UI.RawImage> lista = new List<UnityEngine.UI.RawImage>();
            var todos = UnityEngine.Object.FindObjectsOfType<Il2Cpp.MenuBackground>();
            if (todos == null)
            {
                cacheRawImages = lista;
                intento.Fallo();
                return lista;
            }
            for (int i = 0; i < todos.Length; i++)
            {
                Il2Cpp.MenuBackground mb = todos[i];
                if (mb == null)
                {
                    continue;
                }
                if (propRawImage == null)
                {
                    PropertyInfo[] props = mb.GetType().GetProperties(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    for (int j = 0; j < props.Length; j++)
                    {
                        if (props[j].PropertyType == typeof(UnityEngine.UI.RawImage))
                        {
                            propRawImage = props[j];
                            break;
                        }
                    }
                    if (propRawImage == null)
                    {
                        return lista;
                    }
                }
                var ri = propRawImage.GetValue(mb) as UnityEngine.UI.RawImage;
                if (ri != null)
                {
                    lista.Add(ri);
                }
            }

            if (!inventariado && lista.Count > 0)
            {
                inventariado = true;
                MelonLogger.Msg("[Fondos] instancias de MenuBackground: " + todos.Length.ToString());
                for (int i = 0; i < lista.Count; i++)
                {
                    UnityEngine.UI.RawImage ri = lista[i];
                    MelonLogger.Msg("[Fondos]   [" + i.ToString() + "] " + ri.gameObject.name
                        + "  activo=" + ri.gameObject.activeInHierarchy.ToString()
                        + "  enabled=" + ri.enabled.ToString()
                        + "  color=" + ri.color.ToString()
                        + "  mat=" + (ri.material != null ? ri.material.name : "null")
                        + "  padre=" + (ri.transform.parent != null ? ri.transform.parent.name : "-"));
                }
            }
            cacheRawImages = lista;
            if (lista.Count > 0) { intento.Exito(); } else { intento.Fallo(); }
            return lista;
        }

        private static bool inventariado;
        private static int fondoGuardado = -1;
    }
}
