using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace CloneHeroMod
{
    // Texturas propias, cambiadas EN MEMORIA.
    //
    // La forma habitual de cambiarle los graficos a este juego es abrir
    // resources.assets con UABEA, reemplazar la textura y guardar. Aqui no se
    // toca ni un archivo del juego: se localiza el Texture2D ya cargado y se le
    // sobrescriben los pixeles. Como el objeto sigue siendo el mismo, todos los
    // sprites recortados del atlas conservan sus UV y cambian de golpe.
    //
    // POR QUE NO EL ARCHIVO. Medido en la instalacion de prueba:
    // resources.assets son 71 MB y los pixeles de verdad viven aparte, en
    // resources.assets.resS, otros 54 MB. Reescribir eso desde el mod es
    // reimplementar UABEA —tablas de offsets, type trees, alineacion— y ademas
    // llegariamos tarde: MelonLoader arranca con el motor en marcha y el
    // archivo ya abierto. Cada actualizacion del juego lo reemplaza, asi que
    // una copia de seguridad envejece y restaurarla rompe el juego. En memoria
    // no hay copia que mantener: deshacerlo es borrar el PNG.
    //
    // DONDE SE PONEN. En Custom/Textures, y se busca tambien dentro de sus
    // subcarpetas, para que valga organizarlas por el archivo del que salieron
    // (Custom/Textures/resources.assets/loquesea.png). Backups se ignora.
    //
    // COMO SE EMPAREJA. Por el nombre del archivo: el entero de la textura del
    // juego (sactx-0-4096x4096-BC7-fiveFretAtlas-f7e3ae2a.png) o un trozo que
    // la identifique (fiveFretAtlas.png). Y si el nombre no cuadra con nada,
    // por tamano: si hay UNA sola textura cargada con las medidas exactas del
    // PNG, es esa. Esto ultimo hace falta porque las texturas que devuelve el
    // juego pueden venir sin nombre, ver mas abajo.
    public static class TexturasPersonalizadas
    {
        public const string NombreCarpeta = "Textures";
        public const string CarpetaIgnorada = "Backups";

        // Cuantas veces se reintenta ya dentro de la cancion, y cada cuantos
        // fotogramas. El atlas de trastes solo esta cargado ahi, asi que hay
        // que mirar durante la partida; pero rastrear cuesta, asi que se hace
        // un punado de veces al principio y no se vuelve a tocar.
        public const int IntentosEnJuego = 12;
        public const int FotogramasEntreIntentos = 30;

        // Y cuantos barridos como mucho por escena fuera de la cancion. Hace
        // falta un tope: una imagen que nunca llega a cuadrar —el avatar de un
        // charter que no tienes, por ejemplo— dejaria el rastreo dando vueltas
        // cada cinco segundos para siempre. Al cambiar de escena se reinicia,
        // que es cuando de verdad puede haber aparecido algo nuevo.
        public const int BarridosPorEscena = 20;

        private class Objetivo
        {
            public string clave;
            public string ruta;
            public int ancho;
            public int alto;
            public bool resuelto;
            public bool avisado;
        }

        private static readonly List<Objetivo> objetivos = new List<Objetivo>();
        private static bool hayTrabajo;
        private static bool escaneado;
        private static int pendientes;
        private static readonly Buscador.Intento intento = new Buscador.Intento(3);

        // Diagnostico: una foto por escena, no una sola en toda la partida. La
        // primera version volcaba el inventario una unica vez y se gasto en el
        // menu, justo donde el atlas no esta.
        private static string escenaVolcada;
        private static string escenaActual;
        private static int intentosJuego;
        private static int proximoFotograma;
        private static int barridos;

        public static string Carpeta
        {
            get { return RutasJuego.CarpetaCustom(NombreCarpeta); }
        }

        // ------------------------------------------------------------ escaneo
        public static void Instalar()
        {
            if (escaneado)
            {
                return;
            }
            escaneado = true;
            try
            {
                string carpeta = Carpeta;
                if (string.IsNullOrEmpty(carpeta))
                {
                    return;
                }
                string ignorada = Path.Combine(carpeta, CarpetaIgnorada);
                string[] todos = Directory.GetFiles(carpeta, "*.png",
                                                    SearchOption.AllDirectories);
                for (int i = 0; i < todos.Length; i++)
                {
                    if (todos[i].StartsWith(ignorada, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;      // ahi no hay nada que aplicar
                    }
                    string clave = Clave(Path.GetFileNameWithoutExtension(todos[i]));
                    if (string.IsNullOrEmpty(clave) || Ya(clave))
                    {
                        continue;
                    }
                    Objetivo o = new Objetivo();
                    o.clave = clave;
                    o.ruta = todos[i];
                    MedidasPng(todos[i], out o.ancho, out o.alto);
                    objetivos.Add(o);
                    MelonLogger.Msg("[Texturas] pendiente: " + clave + "  ("
                        + o.ancho.ToString() + "x" + o.alto.ToString() + ")");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Texturas] escaneo: " + ex.Message);
            }
            pendientes = objetivos.Count;
            hayTrabajo = pendientes > 0;
            if (!hayTrabajo)
            {
                MelonLogger.Msg("[Texturas] no hay imagenes en " + Carpeta);
            }
        }

        // Los volcadores de assets (UABEA y compania) guardan los PNG como
        //
        //     Logo_transparent-resources.assets-976.png
        //     Soft-unity default resources-10001.png
        //
        // o sea el nombre de la textura, el archivo del que salio y su id. Se
        // le quita esa cola para que valgan tal cual se exportan, sin obligar a
        // renombrar un monton de archivos a mano. Solo se corta si la cola
        // encaja del todo: id numerico, y antes un contenedor que termina en
        // ".assets" o es el de recursos propios de Unity.
        private static string Clave(string archivo)
        {
            if (string.IsNullOrEmpty(archivo))
            {
                return archivo;
            }
            int guionId = archivo.LastIndexOf('-');
            if (guionId <= 0 || guionId == archivo.Length - 1)
            {
                return archivo;
            }
            for (int i = guionId + 1; i < archivo.Length; i++)
            {
                if (archivo[i] < '0' || archivo[i] > '9')
                {
                    return archivo;      // la cola no es un id
                }
            }
            int guionContenedor = archivo.LastIndexOf('-', guionId - 1);
            if (guionContenedor <= 0)
            {
                return archivo;
            }
            string contenedor = archivo.Substring(guionContenedor + 1,
                                                  guionId - guionContenedor - 1);
            if (!contenedor.EndsWith(".assets", StringComparison.OrdinalIgnoreCase)
                && !contenedor.Equals("unity default resources",
                                      StringComparison.OrdinalIgnoreCase))
            {
                return archivo;
            }
            return archivo.Substring(0, guionContenedor);
        }

        private static bool Ya(string clave)
        {
            for (int i = 0; i < objetivos.Count; i++)
            {
                if (string.Equals(objetivos[i].clave, clave, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // Las medidas salen de la cabecera IHDR del PNG, que son los 8 bytes
        // siguientes a la firma. No hace falta decodificar la imagen entera
        // solo para saber cuanto mide.
        private static void MedidasPng(string ruta, out int ancho, out int alto)
        {
            ancho = 0;
            alto = 0;
            try
            {
                byte[] cab = new byte[24];
                using (FileStream fs = File.OpenRead(ruta))
                {
                    if (fs.Read(cab, 0, 24) < 24 || cab[1] != 0x50 || cab[2] != 0x4E)
                    {
                        return;      // no es un PNG
                    }
                }
                ancho = (cab[16] << 24) | (cab[17] << 16) | (cab[18] << 8) | cab[19];
                alto = (cab[20] << 24) | (cab[21] << 16) | (cab[22] << 8) | cab[23];
            }
            catch (Exception)
            {
            }
        }

        // ------------------------------------------------------------- ciclo -
        // Fuera de la cancion, espaciandose sola mientras no encuentre nada.
        public static void Tick()
        {
            if (!escaneado)
            {
                Instalar();
            }
            if (!hayTrabajo || barridos >= BarridosPorEscena || !intento.Toca())
            {
                return;
            }
            barridos++;
            if (Aplicar())
            {
                intento.Exito();
            }
            else
            {
                intento.Fallo();
            }
            if (barridos >= BarridosPorEscena)
            {
                AvisarDeLasQueFaltan();
            }
        }

        // Una linea, una sola vez, por cada imagen que no llego a cuadrar con
        // ninguna textura. Es lo unico que le sirve a quien la puso ahi: el
        // inventario entero solo se vuelca con diagnostico.flag.
        private static void AvisarDeLasQueFaltan()
        {
            for (int i = 0; i < objetivos.Count; i++)
            {
                Objetivo o = objetivos[i];
                if (o.resuelto || o.avisado)
                {
                    continue;
                }
                o.avisado = true;
                MelonLogger.Warning("[Texturas] " + o.clave
                    + ": ninguna textura del juego se llama asi");
            }
        }

        // Dentro de la cancion. Es el unico sitio donde puede estar cargado el
        // atlas de trastes, pero tambien donde el mod tiene prohibido gastar:
        // un punado de intentos al principio y se apaga solo.
        public static void TickEnJuego()
        {
            if (!hayTrabajo || intentosJuego >= IntentosEnJuego)
            {
                return;
            }
            if (Time.frameCount < proximoFotograma)
            {
                return;
            }
            intentosJuego++;
            proximoFotograma = Time.frameCount + FotogramasEntreIntentos;
            Aplicar();
            if (intentosJuego >= IntentosEnJuego)
            {
                AvisarDeLasQueFaltan();
            }
        }

        // Un barrido AQUI MISMO, en el fotograma en que carga la escena. Es lo
        // que hace que la textura ya este cambiada la primera vez que se
        // dibuja: esperar al Tick normal dejaba ver el logo original durante
        // un momento antes del cambiazo. Y el coste queda tapado por el tiron
        // de la propia carga, que es el mejor momento posible para gastar.
        public static void EscenaCambiada(string nombre, bool enJuego)
        {
            escenaActual = nombre;
            intentosJuego = 0;
            proximoFotograma = 0;
            barridos = 0;
            if (hayTrabajo)
            {
                Aplicar();
            }
        }

        // ---------------------------------------------------------- aplicar --
        // true si se cambio algo o si ya no queda nada pendiente.
        private static bool Aplicar()
        {
            try
            {
                if (pendientes <= 0)
                {
                    hayTrabajo = false;
                    return true;
                }
                string via;
                List<Texture2D> cargadas = Cargadas(out via);
                if (cargadas == null || cargadas.Count == 0)
                {
                    return false;
                }

                bool alguna = false;
                for (int i = 0; i < objetivos.Count; i++)
                {
                    Objetivo o = objetivos[i];
                    if (o.resuelto)
                    {
                        continue;
                    }
                    string como;
                    Texture2D destino = Localizar(cargadas, o, out como);
                    if (destino == null)
                    {
                        continue;
                    }
                    o.resuelto = true;      // a la que falla tampoco se insiste
                    pendientes--;
                    if (Pintar(destino, o.ruta))
                    {
                        alguna = true;
                        MelonLogger.Msg("[Texturas] " + o.clave + " cambiada (por "
                            + como + ")");
                    }
                }
                if (!alguna)
                {
                    Inventario(cargadas, via);
                }
                if (pendientes <= 0)
                {
                    hayTrabajo = false;
                }
                return alguna;
            }
            catch (Exception ex)
            {
                hayTrabajo = false;
                MelonLogger.Error("[Texturas] " + ex);
                return true;
            }
        }

        // Tres reglas, en orden, y NINGUNA adivina: si una deja mas de una
        // candidata se para y lo dice en el log.
        //
        //   1. el nombre entero de la textura
        //   2. un trozo de el, que es lo comodo de escribir
        //   3. las medidas exactas del PNG
        //
        // Lo de no adivinar importa: un nombre corto como "fret" cuadra con
        // media docena de texturas, y cambiar una al azar es peor que no hacer
        // nada porque el usuario no tiene forma de saber cual toco. Devolviendo
        // null se vuelve a mirar en la siguiente pasada, asi que si aparece una
        // coincidencia exacta mas tarde, funciona igual.
        private static Texture2D Localizar(List<Texture2D> cargadas, Objetivo o,
                                           out string como)
        {
            como = "nombre";
            Texture2D exacta = Unica(cargadas, o, true, 0, 0, o.clave, como);
            if (exacta != null)
            {
                return exacta;
            }
            como = "trozo del nombre";
            Texture2D parcial = Unica(cargadas, o, false, 0, 0, o.clave, como);
            if (parcial != null)
            {
                return parcial;
            }
            if (o.ancho <= 0 || o.alto <= 0)
            {
                return null;
            }
            como = "tamano " + o.ancho.ToString() + "x" + o.alto.ToString();
            return Unica(cargadas, o, false, o.ancho, o.alto, null, como);
        }

        // La unica que cumple, o null si no hay ninguna o hay varias. Con
        // texto == null se compara por medidas.
        private static Texture2D Unica(List<Texture2D> cargadas, Objetivo o,
                                       bool exacto, int ancho, int alto,
                                       string texto, string como)
        {
            Texture2D elegida = null;
            int cuantas = 0;
            for (int i = 0; i < cargadas.Count; i++)
            {
                Texture2D t = cargadas[i];
                bool cumple;
                if (texto == null)
                {
                    cumple = t.width == ancho && t.height == alto;
                }
                else
                {
                    string n = t.name;
                    if (string.IsNullOrEmpty(n))
                    {
                        continue;
                    }
                    cumple = exacto
                        ? string.Equals(n, texto, StringComparison.OrdinalIgnoreCase)
                        : n.IndexOf(texto, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (!cumple)
                {
                    continue;
                }
                cuantas++;
                if (elegida == null)
                {
                    elegida = t;
                }
                else if (cuantas <= 6)
                {
                    if (cuantas == 2)
                    {
                        MelonLogger.Warning("[Texturas] " + o.clave + ": por " + como
                            + " cuadran varias, no se toca ninguna. Candidatas:");
                        MelonLogger.Msg("[Texturas]   " + Describir(elegida));
                    }
                    MelonLogger.Msg("[Texturas]   " + Describir(t));
                }
            }
            if (cuantas > 1)
            {
                MelonLogger.Warning("[Texturas] " + o.clave + ": " + cuantas.ToString()
                    + " candidatas por " + como + "; usa un nombre mas concreto");
                return null;
            }
            return elegida;
        }

        // LoadImage reinicia la textura entera —tamano, formato y pixeles— pero
        // NO el objeto, que es justo lo que interesa: los sprites siguen
        // apuntando aqui. Los ajustes de muestreo si los pierde, asi que se
        // guardan antes y se vuelven a poner; el filtrado se nota a simple
        // vista en un atlas.
        private static bool Pintar(Texture2D destino, string ruta)
        {
            string antes = Describir(destino);
            long memAntes = Memoria(destino);
            FilterMode filtro = destino.filterMode;
            TextureWrapMode repeticion = destino.wrapMode;
            int aniso = destino.anisoLevel;
            float sesgo = destino.mipMapBias;

            byte[] datos;
            try
            {
                datos = File.ReadAllBytes(ruta);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Texturas] no se pudo leer " + ruta + ": " + ex.Message);
                return false;
            }

            // markNonReadable = true: en cuanto los pixeles estan en la tarjeta,
            // Unity suelta la copia que guarda en RAM. Nunca volvemos a leer
            // esta textura, asi que esa copia es memoria tirada — y el doble de
            // lo que ocupa la textura. Ademas deja el permiso de lectura como
            // estaba en el original, que tampoco lo daba.
            if (!UnityEngine.ImageConversion.LoadImage(destino, datos, true))
            {
                MelonLogger.Warning("[Texturas] " + antes
                    + ": el PNG no se pudo aplicar");
                return false;
            }

            // NADA de Apply() aqui. LoadImage ya sube los pixeles a la tarjeta;
            // Apply ademas necesita leer la copia de la CPU, y las texturas del
            // juego vienen sin permiso de lectura. Medido:
            //
            //   UnityException: Texture 'sactx-0-4096x4096-BC7-fiveFretAtlas...'
            //   is not readable, the texture memory can not be accessed
            //
            // El cambio ya se veia en pantalla y aun asi saltaba la excepcion,
            // que se llevaba por delante el resto de imagenes de la tanda.
            destino.filterMode = filtro;
            destino.wrapMode = repeticion;
            destino.anisoLevel = aniso;
            destino.mipMapBias = sesgo;

            MelonLogger.Msg("[Texturas]   " + antes + "  ->  " + Describir(destino)
                + Coste(memAntes, Memoria(destino)));
            return true;
        }

        // Lo que ocupa la textura en memoria. Importa porque el atlas viene en
        // BC7 comprimido y un PNG entra sin comprimir: el cambio no sale
        // gratis y conviene tenerlo a la vista.
        //
        // Se calcula a mano a proposito. Profiler.GetRuntimeMemorySizeLong
        // parecia lo suyo, pero MEDIDO en esta build devuelve 0 para todas las
        // texturas: el perfilador no esta en una compilacion de release.
        private static long Memoria(Texture2D t)
        {
            try
            {
                double porPixel;
                switch (t.format)
                {
                    case TextureFormat.DXT1:
                    case TextureFormat.BC4:
                        porPixel = 0.5;
                        break;
                    case TextureFormat.DXT5:
                    case TextureFormat.BC7:
                    case TextureFormat.BC5:
                    case TextureFormat.BC6H:
                    case TextureFormat.Alpha8:
                    case TextureFormat.R8:
                        porPixel = 1.0;
                        break;
                    case TextureFormat.RGB24:
                        porPixel = 3.0;
                        break;
                    case TextureFormat.RGBAHalf:
                        porPixel = 8.0;
                        break;
                    default:
                        porPixel = 4.0;      // RGBA32, ARGB32, BGRA32...
                        break;
                }
                double total = (double)t.width * (double)t.height * porPixel;
                if (t.mipmapCount > 1)
                {
                    total *= 4.0 / 3.0;      // la cadena de mips anade un tercio
                }
                return (long)total;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static string Coste(long antes, long despues)
        {
            if (antes < 0 || despues < 0)
            {
                return "";
            }
            const long mb = 1024 * 1024;
            long diferencia = despues - antes;
            return "   [memoria " + (antes / mb).ToString() + " MB -> "
                + (despues / mb).ToString() + " MB, "
                + (diferencia >= 0 ? "+" : "") + (diferencia / mb).ToString() + " MB]";
        }

        private static string Describir(Texture2D t)
        {
            string n = t.name;
            return (string.IsNullOrEmpty(n) ? "(sin nombre)" : n)
                + " " + t.width.ToString() + "x" + t.height.ToString()
                + " " + t.format.ToString() + " mips:" + t.mipmapCount.ToString();
        }

        // ------------------------------------------------------------ rastreo
        //
        // Unity recorta metodos en compilacion y aqui ya nos hemos topado con
        // varios. MEDIDO en esta build:
        //
        //   Resources.FindObjectsOfTypeAll<Texture2D>   Method unstripping failed
        //   Object.FindObjectsOfType(Type)              863 texturas en el menu
        //
        // La primera era la buena —es la unica que ve las texturas cargadas que
        // no esta usando nadie— y no esta. Asi que no queda mas remedio que
        // SUMAR lo que den las demas: la segunda no ve lo oculto, y recorrer la
        // escena no ve lo que no se esta dibujando, pero entre las dos sale
        // mas que con cualquiera por separado. La primera version se quedaba
        // con la primera vía que devolviera algo, y por eso el recorrido de la
        // escena no llego a correr nunca.
        private static List<Texture2D> Cargadas(out string via)
        {
            List<Texture2D> lista = new List<Texture2D>();
            HashSet<int> vistas = new HashSet<int>();

            int porTipo = PorTipo(lista, vistas);
            int porEscena = PorEscena(lista, vistas);
            int porSprites = PorSprites(lista, vistas);

            via = porTipo.ToString() + "+" + porEscena.ToString()
                + "+" + porSprites.ToString();
            return lista;
        }

        private static void Agregar(List<Texture2D> lista, HashSet<int> vistas, Texture t)
        {
            if (t == null)
            {
                return;
            }
            Texture2D t2 = t.TryCast<Texture2D>();
            if (t2 == null)
            {
                return;
            }
            if (vistas.Add(t2.GetInstanceID()))
            {
                lista.Add(t2);
            }
        }

        // Recibe el Type en vez de instanciarse, asi que no depende de ninguna
        // version generica. Es la que mas devuelve de las que funcionan.
        private static int PorTipo(List<Texture2D> lista, HashSet<int> vistas)
        {
            int antes = lista.Count;
            try
            {
                var todas = UnityEngine.Object.FindObjectsOfType(
                    Il2CppInterop.Runtime.Il2CppType.Of<Texture2D>());
                for (int i = 0; todas != null && i < todas.Length; i++)
                {
                    if (todas[i] != null)
                    {
                        Agregar(lista, vistas, todas[i].TryCast<Texture>());
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Texturas] FindObjectsOfType(Type): " + ex.Message);
            }
            return lista.Count - antes;
        }

        // Lo que se esta dibujando ahora mismo. El atlas de trastes ocupa media
        // pantalla durante la cancion, asi que por aqui tiene que salir aunque
        // no aparezca por ningun otro lado.
        private static int PorEscena(List<Texture2D> lista, HashSet<int> vistas)
        {
            int antes = lista.Count;
            try
            {
                var renders = UnityEngine.Object.FindObjectsOfType(
                    Il2CppInterop.Runtime.Il2CppType.Of<Renderer>());
                for (int i = 0; renders != null && i < renders.Length; i++)
                {
                    Renderer r = renders[i] == null ? null : renders[i].TryCast<Renderer>();
                    if (r == null)
                    {
                        continue;
                    }
                    var mats = r.sharedMaterials;
                    for (int j = 0; mats != null && j < mats.Length; j++)
                    {
                        if (mats[j] != null)
                        {
                            Agregar(lista, vistas, mats[j].mainTexture);
                        }
                    }
                    // Un SpriteRenderer no lleva la textura en el material: la
                    // lleva el sprite, y el material es el compartido de todos.
                    SpriteRenderer sr = r.TryCast<SpriteRenderer>();
                    if (sr != null && sr.sprite != null)
                    {
                        Agregar(lista, vistas, sr.sprite.texture);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Texturas] renderers: " + ex.Message);
            }
            try
            {
                var imagenes = UnityEngine.Object.FindObjectsOfType(
                    Il2CppInterop.Runtime.Il2CppType.Of<UnityEngine.UI.Image>());
                for (int i = 0; imagenes != null && i < imagenes.Length; i++)
                {
                    var img = imagenes[i] == null
                        ? null : imagenes[i].TryCast<UnityEngine.UI.Image>();
                    if (img != null && img.sprite != null)
                    {
                        Agregar(lista, vistas, img.sprite.texture);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Texturas] imagenes: " + ex.Message);
            }
            return lista.Count - antes;
        }

        // Los Sprite sueltos. Un atlas se reparte en cientos de sprites y todos
        // apuntan a la misma textura, asi que basta con que uno este cargado.
        private static int PorSprites(List<Texture2D> lista, HashSet<int> vistas)
        {
            int antes = lista.Count;
            try
            {
                var sprites = UnityEngine.Object.FindObjectsOfType(
                    Il2CppInterop.Runtime.Il2CppType.Of<Sprite>());
                for (int i = 0; sprites != null && i < sprites.Length; i++)
                {
                    Sprite s = sprites[i] == null ? null : sprites[i].TryCast<Sprite>();
                    if (s != null)
                    {
                        Agregar(lista, vistas, s.texture);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Texturas] sprites: " + ex.Message);
            }
            return lista.Count - antes;
        }

        // Una foto por escena de lo que hay, para no ir a ciegas en la
        // siguiente vuelta. Sale ordenado por tamano porque lo que buscamos es
        // de las mas grandes que puede haber.
        private static void Inventario(List<Texture2D> cargadas, string via)
        {
            if (!Diagnostico.Detallado || escenaVolcada == escenaActual)
            {
                return;
            }
            escenaVolcada = escenaActual;

            int conNombre = 0;
            List<Texture2D> orden = new List<Texture2D>();
            for (int i = 0; i < cargadas.Count; i++)
            {
                if (!string.IsNullOrEmpty(cargadas[i].name))
                {
                    conNombre++;
                }
                orden.Add(cargadas[i]);
            }
            orden.Sort(delegate (Texture2D a, Texture2D b)
            {
                return (b.width * b.height).CompareTo(a.width * a.height);
            });

            MelonLogger.Warning("[Texturas] ninguna coincidio en '" + escenaActual
                + "'. Texturas: " + cargadas.Count.ToString() + " (tipo+escena+sprites: "
                + via + "), con nombre: " + conNombre.ToString());
            for (int i = 0; i < orden.Count && i < 15; i++)
            {
                MelonLogger.Msg("[Texturas]   " + Describir(orden[i]));
            }
            for (int i = 0; i < orden.Count; i++)
            {
                string n = orden[i].name;
                if (string.IsNullOrEmpty(n))
                {
                    continue;
                }
                if (n.IndexOf("atlas", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("fret", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.StartsWith("sactx", StringComparison.OrdinalIgnoreCase))
                {
                    MelonLogger.Msg("[Texturas]   * " + Describir(orden[i]));
                }
            }
        }
    }
}
