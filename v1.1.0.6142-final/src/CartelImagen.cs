using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace CloneHeroMod
{
    // Imagenes propias para los carteles de la cancion: si en Custom/Textures
    // hay un cool_note_streak.png (o .jpg) o un cool_star_power.png, el cartel
    // se pinta con esa imagen en vez de con el texto, con la misma animacion.
    // Si no hay, todo sigue como siempre.
    //
    // Viven en la misma carpeta que las texturas del juego porque es donde
    // alguien que personaliza graficos ya esta mirando. TexturasPersonalizadas
    // las salta: no reemplazan ninguna textura del juego.
    //
    // Se cargan desde los menus (ResolverEstilo de cada cartel), nunca durante
    // la cancion: leer y decodificar un PNG ahi seria un tiron justo al
    // arrancar.
    public static class CartelImagen
    {
        public const string Racha = "cool_note_streak";
        public const string Estrella = "cool_star_power";

        // La imagen y la animacion LLENAN LA PANTALLA, sea cual sea su
        // resolucion y la del jugador, como hace el juego con los fondos. Antes
        // se veian a su tamano tal cual, y un usuario lo reporto: para que un
        // cartel cubriera una pantalla 4K habia que dibujarlo a 3840x2160, y
        // asi cada fotograma ocupa unos 33 MB en memoria una vez cargado
        // —61 fotogramas, unos 2 GB—, aunque el PNG pese poco. Estirando, unos
        // fotogramas de 800x450 sirven en cualquier pantalla. El tamano del
        // .ini es solo el de la letra (el numero del Note Streak).

        private static readonly string[] Extensiones = { ".png", ".jpg", ".jpeg" };
        private static readonly Dictionary<string, Texture2D> cargadas =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        public static bool EsDeCartel(string nombreSinExtension)
        {
            return string.Equals(nombreSinExtension, Racha, StringComparison.OrdinalIgnoreCase)
                || string.Equals(nombreSinExtension, Estrella, StringComparison.OrdinalIgnoreCase);
        }

        // Las carpetas de animacion (Textures/cool_note_streak/...) tampoco son
        // texturas del juego. Sus fotogramas suelen llamarse 1.png, 2.png...,
        // y sin esto el reemplazo de texturas intentaria emparejarlos con
        // alguna del juego por tamano.
        public static bool EsCarpetaDeCartel(string ruta)
        {
            string carpeta = Path.GetFileName(Path.GetDirectoryName(ruta) ?? "");
            return EsDeCartel(carpeta)
                || string.Equals(carpeta, ImagenesCancion.NombreCarpeta,
                                 StringComparison.OrdinalIgnoreCase);
        }

        // La imagen, o null si no hay.
        //
        // SOLO SE RELEE SI CAMBIO ALGO. Se llama cada vez que se vuelve al
        // menu principal, para que los cambios hechos con el juego abierto se
        // vean sin reiniciar; pero releer siempre hacia que salir de una
        // cancion tardara muchisimo con animaciones grandes. Ahora se compara
        // una firma —nombres, tamanos y fechas de los archivos— y si es la
        // misma se devuelve lo que ya habia.
        public static Texture2D Cargar(string nombre)
        {
            string ruta = null;
            try
            {
                string carpeta = RutasJuego.CarpetaCustom(TexturasPersonalizadas.NombreCarpeta);
                if (!string.IsNullOrEmpty(carpeta) && Directory.Exists(carpeta))
                {
                    for (int i = 0; i < Extensiones.Length && ruta == null; i++)
                    {
                        string c = Path.Combine(carpeta, nombre + Extensiones[i]);
                        if (File.Exists(c)) ruta = c;
                    }
                }
            }
            catch (Exception)
            {
            }
            string firma = ruta == null ? "" : Firma(new List<string> { ruta });
            Texture2D t;
            string vista;
            if (cargadas.TryGetValue(nombre, out t) && firmas.TryGetValue(nombre, out vista)
                && vista == firma)
            {
                return t;
            }
            if (t != null) UnityEngine.Object.Destroy(t);
            t = null;
            try
            {
                if (ruta != null)
                {
                    t = Leer(ruta);
                    if (t != null)
                    {
                        MelonLogger.Msg("[Carteles] imagen propia: " + Path.GetFileName(ruta)
                            + " (" + t.width.ToString() + "x" + t.height.ToString() + ")");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Carteles] " + nombre + ": " + ex.Message);
            }
            cargadas[nombre] = t;
            firmas[nombre] = firma;
            return t;
        }

        // ANIMACION: una carpeta con el mismo nombre que la imagen, llena de
        // fotogramas. Tiene prioridad sobre la imagen suelta; vacia, como si
        // no estuviera. Devuelve null si no hay fotogramas.
        //
        // El orden es el NATURAL, comparando los numeros como numeros: en
        // orden alfabetico estricto, 1.png, 2.png ... 12.png saldrian como
        // 1, 10, 11, 12, 2... Asi vale igual con 1.png que con frame_001.png.
        public static Texture2D[] CargarAnimacion(string nombre)
        {
            string carpeta = null;
            try
            {
                string raiz = RutasJuego.CarpetaCustom(TexturasPersonalizadas.NombreCarpeta);
                carpeta = string.IsNullOrEmpty(raiz) ? null : Path.Combine(raiz, nombre);
            }
            catch (Exception)
            {
            }
            string firma = FirmaCarpeta(carpeta);
            Texture2D[] t;
            string vista;
            if (animaciones.TryGetValue(nombre, out t) && firmas.TryGetValue("/" + nombre, out vista)
                && vista == firma)
            {
                return t;
            }
            Destruir(t);
            t = null;
            try
            {
                if (carpeta != null && Directory.Exists(carpeta))
                {
                    t = LeerCarpeta(carpeta, "[Carteles] animacion propia: " + nombre);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Carteles] animacion " + nombre + ": " + ex.Message);
            }
            animaciones[nombre] = t;
            firmas["/" + nombre] = firma;
            return t;
        }

        private static readonly Dictionary<string, string> firmas =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Nombres, tamanos y fechas de las imagenes de una carpeta: si no
        // cambia, el contenido tampoco. "" si no hay carpeta.
        public static string FirmaCarpeta(string carpeta)
        {
            try
            {
                if (string.IsNullOrEmpty(carpeta) || !Directory.Exists(carpeta)) return "";
                List<string> archivos = new List<string>();
                foreach (string f in Directory.GetFiles(carpeta))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(Extensiones, ext) >= 0) archivos.Add(f);
                }
                archivos.Sort(StringComparer.OrdinalIgnoreCase);
                return Firma(archivos);
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static string Firma(List<string> archivos)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < archivos.Count; i++)
            {
                FileInfo fi = new FileInfo(archivos[i]);
                sb.Append(fi.Name).Append('|').Append(fi.Length).Append('|')
                  .Append(fi.LastWriteTimeUtc.Ticks).Append(';');
            }
            return sb.ToString();
        }

        public static void Destruir(Texture2D[] a)
        {
            if (a == null) return;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != null) UnityEngine.Object.Destroy(a[i]);
            }
        }

        private static readonly Dictionary<string, Texture2D[]> animaciones =
            new Dictionary<string, Texture2D[]>(StringComparer.OrdinalIgnoreCase);

        // Todas las imagenes de una carpeta, en orden natural, o null si no
        // hay ninguna. Deja en el log cuantas y en que orden, precedido de
        // "etiqueta". La usan las animaciones de los carteles y las imagenes
        // de durante la cancion (ImagenesCancion).
        public static Texture2D[] LeerCarpeta(string carpeta, string etiqueta)
        {
            List<string> archivos = new List<string>();
            foreach (string f in Directory.GetFiles(carpeta))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (Array.IndexOf(Extensiones, ext) >= 0) archivos.Add(f);
            }
            archivos.Sort(OrdenNatural);
            List<Texture2D> leidas = new List<Texture2D>();
            List<string> orden = new List<string>();
            for (int i = 0; i < archivos.Count; i++)
            {
                Texture2D f = Leer(archivos[i]);
                if (f == null) continue;
                leidas.Add(f);
                orden.Add(Path.GetFileName(archivos[i]));
            }
            if (leidas.Count == 0)
            {
                return null;
            }
            MelonLogger.Msg(etiqueta + ", " + leidas.Count.ToString() + " imagen(es) ("
                + leidas[0].width.ToString() + "x" + leidas[0].height.ToString()
                + "), en este orden: " + string.Join(", ", orden.ToArray()));
            return leidas.ToArray();
        }

        private static Texture2D Leer(string ruta)
        {
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            // markNonReadable: en cuanto los pixeles suben a la tarjeta, Unity
            // suelta la copia que guarda en RAM. Nunca se vuelven a leer, y
            // esa copia es tanto como la textura entera: la mitad de lo que
            // ocupaba cada fotograma.
            if (!UnityEngine.ImageConversion.LoadImage(tex, File.ReadAllBytes(ruta), true))
            {
                MelonLogger.Warning("[Carteles] no se pudo leer " + ruta);
                return null;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            // que no se la lleve la limpieza de Unity entre escenas
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        // Trozos de digitos se comparan por su valor; el resto, como texto.
        private static int OrdenNatural(string a, string b)
        {
            a = Path.GetFileNameWithoutExtension(a);
            b = Path.GetFileNameWithoutExtension(b);
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int i0 = i, j0 = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a.Substring(i0, i - i0).TrimStart('0');
                    string nb = b.Substring(j0, j - j0).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                    int c = string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                    if (c != 0) return c;
                    i++;
                    j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }

        // Un RawImage hijo de "padre": centrado y al tamano de la imagen, o
        // estirado a todo "padre" si pantallaCompleta.
        public static RawImage Poner(Transform padre, Texture2D tex, bool pantallaCompleta = false)
        {
            GameObject go = new GameObject("Imagen");
            go.transform.SetParent(padre, false);
            RawImage img = go.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            if (pantallaCompleta)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.sizeDelta = Vector2.zero;
            }
            else
            {
                rt.sizeDelta = new Vector2(tex.width, tex.height);
            }
            rt.anchoredPosition = Vector2.zero;
            return img;
        }
    }
}
