using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace CloneHeroMod
{
    // "SP Ready!" en pantalla en el mismo instante en que la barra
    // de Star Power se enciende: el brillo electrico y el rayo.
    //
    // DE DONDE SALE EL MOMENTO. No se calcula nada: se engancha el metodo con
    // que el juego rellena la barra, SPBar(relleno, activo). En el volcado
    // ISIL se ve que es ahi donde decide el brillo:
    //
    //     _uElectricityEffect = (activo || relleno >= Umbral) ? 1 : 0
    //
    // y con la misma condicion enciende el objeto del rayo. Activo es el Star
    // Power ya desplegado; sin el, el brillo significa "listo para usar". El
    // cartel sale en el flanco: cuando la barra pasa de no lista a lista.
    //
    // PRIMER INTENTO, DESCARTADO: enganchar el sonido star_available. Existe
    // en la tabla de sonidos del juego y hay un metodo que solo hace sonarlo,
    // pero nadie lo llama: en esta version no suena nunca. El que suena es
    // sp_awarded, y ese va con cada frase de Star Power ganada, este o no
    // lista la barra.
    //
    // El SPBar tiene seis metodos (float, bool) casi iguales: el ofuscador
    // mete senuelos. El real es el unico con llamadas entrantes —cinco, desde
    // BasePlayer y la propia barra—, y Il2CppInterop lo llama
    // Method_Public_Void_Single_Boolean_0; los senuelos llevan _PDM_.
    //
    // Durante la cancion el parche solo compara dos numeros y, si toca,
    // levanta una bandera; el Tick sale en la primera linea si no hay nada.
    public static class EstrellaLista
    {
        public const string Texto = "SP Ready!";
        public const string Metodo = "Method_Public_Void_Single_Boolean_0";

        // A partir de aqui la barra brilla. Es la mitad, como en todos los
        // juegos de la saga: con media barra ya se puede desplegar.
        private const float Umbral = 0.5f;

        private const float Duracion = 1.8f;
        private const float GrosorBorde = 0.2f;

        // Por debajo del cartel de racha (250), para que no se pisen si un
        // hito y el Star Power coinciden.
        private const float AlturaBase = 150f;
        private const float Deriva = 55f;

        private static bool activo;
        private static bool pendiente;
        private static bool parcheado;

        // Por barra, porque en multijugador hay una por jugador. Se indexa por
        // el puntero nativo: el envoltorio gestionado cambia de una llamada a
        // otra.
        private static readonly System.Collections.Generic.Dictionary<IntPtr, bool> listas =
            new System.Collections.Generic.Dictionary<IntPtr, bool>();

        private static GameObject raiz;
        private static Il2CppTMPro.TextMeshProUGUI texto;
        // Lo que se anima: el texto o, si hay imagen propia, la imagen sola
        // (ver CartelImagen). El texto se crea igual, vacio, para que el resto
        // del codigo no tenga dos caminos.
        private static GameObject cartel;
        private static RectTransform rtCartel;
        private static RawImage imagen;
        private static Texture2D texturaImagen;
        private static bool imagenBuscada;
        private static bool animando;
        private static float t;
        private static int intentos;
        private static int esperaBusqueda;

        private static Material material;
        private static int idColorBorde;

        private static Color color = new Color(0.2f, 0.71f, 1f, 1f);
        private static Il2CppTMPro.TMP_FontAsset fuente;
        private static bool estiloResuelto;
        private static readonly Buscador.Intento intentoEstilo = new Buscador.Intento(23);

        // ------------------------------------------------------------ parche -
        public static void InstalarParche(HarmonyLib.Harmony harmony)
        {
            if (parcheado)
            {
                return;
            }
            parcheado = true;
            try
            {
                MethodInfo m = typeof(Il2Cpp.SPBar).GetMethod(Metodo,
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new Type[] { typeof(float), typeof(bool) }, null);
                if (m == null)
                {
                    MelonLogger.Warning("[Estrella] no esta " + Metodo + " en SPBar;"
                        + " el cartel de Star Power queda apagado");
                    return;
                }
                harmony.Patch(m, null, new HarmonyMethod(
                    typeof(EstrellaLista).GetMethod("PostRelleno",
                        BindingFlags.NonPublic | BindingFlags.Static)));
                MelonLogger.Msg("[Estrella] enganchado a la barra de Star Power");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Estrella] " + ex);
            }
        }

        // Corre dentro del juego, en mitad de su logica de puntuacion: aqui no
        // se toca nada de Unity, solo se deja el aviso para el siguiente Tick.
        private static void PostRelleno(Il2Cpp.SPBar __instance, float __0, bool __1)
        {
            if (!activo || __instance == null)
            {
                return;
            }
            try
            {
                bool lista = !__1 && __0 >= Umbral;
                IntPtr clave = __instance.Pointer;
                bool antes;
                listas.TryGetValue(clave, out antes);
                if (lista == antes)
                {
                    return;
                }
                listas[clave] = lista;
                if (lista)
                {
                    pendiente = true;
                    rellenoAlDisparar = __0;
                }
            }
            catch (Exception)
            {
            }
        }

        private static float rellenoAlDisparar;

        // ------------------------------------------------------------ escena -
        public static void EscenaCambiada(string nombre, bool enJuego)
        {
            pendiente = false;
            listas.Clear();
            animando = false;
            raiz = null;      // lo destruye Unity al descargar la escena
            texto = null;
            cartel = null;
            rtCartel = null;
            imagen = null;
            material = null;
            intentos = 0;
            esperaBusqueda = 0;

            activo = enJuego && Ajustes.MostrarEstrella;
            if (activo)
            {
                MelonLogger.Msg("[Estrella] activa en " + nombre);
            }
        }

        // ----------------------------------------------------------- por frame
        public static void Tick()
        {
            if (!activo)
            {
                return;
            }
            try
            {
                if (raiz == null)
                {
                    // El cartel se monta al empezar la cancion, no al primer
                    // aviso: crear un canvas en ese instante se notaria como un
                    // tiron justo cuando el jugador va a activar el Star Power.
                    Localizar();
                }
                if (pendiente)
                {
                    pendiente = false;
                    Disparar();
                }
                if (animando)
                {
                    Animar();
                }
            }
            catch (Exception ex)
            {
                activo = false;      // nunca a costa de la cancion
                MelonLogger.Error("[Estrella] desactivada por error: " + ex);
            }
        }

        // FindObjectOfType recorre los objetos cargados: espaciado, y con
        // limite.
        private static void Localizar()
        {
            if (intentos > 60)
            {
                return;
            }
            if (esperaBusqueda > 0)
            {
                esperaBusqueda--;
                return;
            }
            esperaBusqueda = 30;
            intentos++;
            Preparar();
        }

        // ------------------------------------------------------------ cartel -
        private static void Disparar()
        {
            if (texto == null)
            {
                Preparar();
                if (texto == null)
                {
                    return;
                }
            }
            cartel.SetActive(true);
            animando = true;
            t = 0f;
            MelonLogger.Msg("[Estrella] Star Power listo (barra al "
                + (rellenoAlDisparar * 100f).ToString("0") + "%)");
        }

        // La misma animacion que el cartel de racha: entra pasandose de
        // tamano, se asienta, aguanta y se desvanece mientras sube.
        private static void Animar()
        {
            t += Time.deltaTime;
            if (t >= Duracion)
            {
                animando = false;
                if (cartel != null)
                {
                    cartel.SetActive(false);
                }
                return;
            }

            float escala;
            if (t < 0.16f)
            {
                escala = Mathf.Lerp(0.5f, 1.18f, t / 0.16f);
            }
            else if (t < 0.30f)
            {
                escala = Mathf.Lerp(1.18f, 1f, (t - 0.16f) / 0.14f);
            }
            else
            {
                escala = 1f;
            }

            float alfa;
            if (t < 0.12f)
            {
                alfa = t / 0.12f;
            }
            else if (t > Duracion - 0.55f)
            {
                alfa = (Duracion - t) / 0.55f;
            }
            else
            {
                alfa = 1f;
            }

            float avance = t / Duracion;
            rtCartel.localScale = new Vector3(escala, escala, 1f);
            rtCartel.anchoredPosition = new Vector2(0f, AlturaBase + Deriva * avance * avance);
            texto.color = new Color(color.r, color.g, color.b, alfa);
            if (imagen != null)
            {
                imagen.color = new Color(1f, 1f, 1f, alfa);
            }
            if (material != null)
            {
                material.SetColor(idColorBorde, new Color(0f, 0f, 0f, alfa));
            }
        }

        // Borde negro sobre una COPIA propia del material (fontMaterial); el
        // compartido lo usan todos los textos del juego con esa fuente.
        private static void PonerBorde()
        {
            try
            {
                idColorBorde = Shader.PropertyToID("_OutlineColor");
                material = texto.fontMaterial;
                if (material == null)
                {
                    return;
                }
                material.SetColor(idColorBorde, Color.black);
                material.SetFloat(Shader.PropertyToID("_OutlineWidth"), GrosorBorde);
            }
            catch (Exception ex)
            {
                material = null;
                MelonLogger.Warning("[Estrella] sin borde: " + ex.Message);
            }
        }

        // Color y fuente del .ini. Se resuelve fuera de la cancion (lo llama
        // Diagnostico en el menu), asi que aqui no cuesta nada.
        public static void ResolverEstilo()
        {
            if (!imagenBuscada)
            {
                imagenBuscada = true;
                texturaImagen = CartelImagen.Cargar(CartelImagen.Estrella);
            }
            if (estiloResuelto || !intentoEstilo.Toca())
            {
                return;
            }
            try
            {
                string hex = Ajustes.EstrellaColor;
                if (!string.IsNullOrEmpty(hex))
                {
                    if (hex[0] != '#')
                    {
                        hex = "#" + hex;
                    }
                    Color c;
                    if (ColorUtility.TryParseHtmlString(hex, out c))
                    {
                        color = c;
                    }
                    else
                    {
                        MelonLogger.Warning("[Estrella] color '" + Ajustes.EstrellaColor
                            + "' no se entiende; se usa el de siempre. Formato: RRGGBB");
                    }
                }

                string nombre = Ajustes.EstrellaFuente;
                if (string.IsNullOrEmpty(nombre))
                {
                    estiloResuelto = true;      // la del juego
                    return;
                }
                fuente = RachaNotas.BuscarFuentePublica(nombre);
                estiloResuelto = fuente != null;
            }
            catch (Exception ex)
            {
                estiloResuelto = true;
                MelonLogger.Warning("[Estrella] estilo: " + ex.Message);
            }
        }

        private static void Preparar()
        {
            try
            {
                if (raiz != null)
                {
                    return;
                }
                // Un TMP sin TM_FontAsset sale en blanco: se toma la fuente de
                // un texto que ya exista en la escena.
                Il2CppTMPro.TextMeshProUGUI plantilla =
                    UnityEngine.Object.FindObjectOfType<Il2CppTMPro.TextMeshProUGUI>();
                if (plantilla == null || plantilla.font == null)
                {
                    return;
                }

                raiz = new GameObject("StarPowerReadyOverlay");
                Canvas lienzo = raiz.AddComponent<Canvas>();
                lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
                lienzo.sortingOrder = 31000;
                CanvasScaler escala = raiz.AddComponent<CanvasScaler>();
                escala.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                escala.referenceResolution = new Vector2(1920f, 1080f);

                cartel = new GameObject("Cartel");
                cartel.transform.SetParent(raiz.transform, false);
                rtCartel = cartel.AddComponent<RectTransform>();
                rtCartel.anchorMin = new Vector2(0.5f, 0.5f);
                rtCartel.anchorMax = new Vector2(0.5f, 0.5f);
                rtCartel.pivot = new Vector2(0.5f, 0.5f);
                rtCartel.sizeDelta = new Vector2(1200f, 140f);
                rtCartel.anchoredPosition = new Vector2(0f, AlturaBase);

                if (texturaImagen != null)
                {
                    imagen = CartelImagen.Poner(cartel.transform, texturaImagen,
                        Ajustes.EstrellaTamano * CartelImagen.AltoPorTamano);
                }

                GameObject go = new GameObject("Text");
                go.transform.SetParent(cartel.transform, false);
                texto = go.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                texto.font = fuente != null ? fuente : plantilla.font;
                texto.fontSize = Ajustes.EstrellaTamano;
                texto.fontStyle = Il2CppTMPro.FontStyles.Bold;
                texto.color = color;
                texto.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
                texto.raycastTarget = false;
                texto.text = imagen != null ? "" : Texto;

                RectTransform rtTexto = go.GetComponent<RectTransform>();
                rtTexto.anchorMin = new Vector2(0.5f, 0.5f);
                rtTexto.anchorMax = new Vector2(0.5f, 0.5f);
                rtTexto.pivot = new Vector2(0.5f, 0.5f);
                rtTexto.sizeDelta = new Vector2(1200f, 140f);
                rtTexto.anchoredPosition = Vector2.zero;
                cartel.SetActive(false);

                PonerBorde();
                MelonLogger.Msg("[Estrella] cartel preparado");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Estrella] al preparar el cartel: " + ex);
                activo = false;
            }
        }
    }
}
