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

        // El tamano del .ini es el de la letra. Para una imagen se toma como
        // su alto en pixeles por este factor: con el 72 de serie, 144 px, que
        // es mas o menos lo que ocupa el texto. El ancho sale de su proporcion.
        public const float AltoPorTamano = 2f;

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
            return EsDeCartel(carpeta);
        }

        // La imagen, o null si no hay. Se mira una sola vez por sesion.
        public static Texture2D Cargar(string nombre)
        {
            Texture2D t;
            if (cargadas.TryGetValue(nombre, out t))
            {
                return t;
            }
            t = null;
            try
            {
                string carpeta = RutasJuego.CarpetaCustom(TexturasPersonalizadas.NombreCarpeta);
                string ruta = null;
                if (!string.IsNullOrEmpty(carpeta) && Directory.Exists(carpeta))
                {
                    for (int i = 0; i < Extensiones.Length && ruta == null; i++)
                    {
                        string c = Path.Combine(carpeta, nombre + Extensiones[i]);
                        if (File.Exists(c)) ruta = c;
                    }
                }
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
            Texture2D[] t;
            if (animaciones.TryGetValue(nombre, out t))
            {
                return t;
            }
            t = null;
            try
            {
                string raiz = RutasJuego.CarpetaCustom(TexturasPersonalizadas.NombreCarpeta);
                string carpeta = string.IsNullOrEmpty(raiz) ? null : Path.Combine(raiz, nombre);
                if (carpeta != null && Directory.Exists(carpeta))
                {
                    List<string> archivos = new List<string>();
                    foreach (string f in Directory.GetFiles(carpeta))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (Array.IndexOf(Extensiones, ext) >= 0) archivos.Add(f);
                    }
                    archivos.Sort(OrdenNatural);
                    List<Texture2D> fotogramas = new List<Texture2D>();
                    List<string> orden = new List<string>();
                    for (int i = 0; i < archivos.Count; i++)
                    {
                        Texture2D f = Leer(archivos[i]);
                        if (f == null) continue;
                        fotogramas.Add(f);
                        orden.Add(Path.GetFileName(archivos[i]));
                    }
                    if (fotogramas.Count > 0)
                    {
                        t = fotogramas.ToArray();
                        MelonLogger.Msg("[Carteles] animacion propia: " + nombre + ", "
                            + t.Length.ToString() + " fotogramas ("
                            + t[0].width.ToString() + "x" + t[0].height.ToString() + "), en este orden: "
                            + string.Join(", ", orden.ToArray()));
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Carteles] animacion " + nombre + ": " + ex.Message);
            }
            animaciones[nombre] = t;
            return t;
        }

        private static readonly Dictionary<string, Texture2D[]> animaciones =
            new Dictionary<string, Texture2D[]>(StringComparer.OrdinalIgnoreCase);

        private static Texture2D Leer(string ruta)
        {
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!UnityEngine.ImageConversion.LoadImage(tex, File.ReadAllBytes(ruta)))
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

        // Un RawImage hijo de "padre", centrado, con el alto pedido y el ancho
        // que le toque por proporcion.
        public static RawImage Poner(Transform padre, Texture2D tex, float alto)
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
            float ancho = tex.height > 0 ? alto * tex.width / tex.height : alto;
            rt.sizeDelta = new Vector2(ancho, alto);
            rt.anchoredPosition = Vector2.zero;
            return img;
        }
    }
}
