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
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (UnityEngine.ImageConversion.LoadImage(tex, File.ReadAllBytes(ruta)))
                    {
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.filterMode = FilterMode.Bilinear;
                        // que no se la lleve la limpieza de Unity entre escenas
                        tex.hideFlags = HideFlags.HideAndDontSave;
                        t = tex;
                        MelonLogger.Msg("[Carteles] imagen propia: " + Path.GetFileName(ruta)
                            + " (" + tex.width.ToString() + "x" + tex.height.ToString() + ")");
                    }
                    else
                    {
                        MelonLogger.Warning("[Carteles] no se pudo leer " + ruta
                            + "; se usa el cartel de texto");
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
