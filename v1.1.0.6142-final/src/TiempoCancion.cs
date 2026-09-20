using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace CloneHeroMod
{
    // Reloj de la cancion durante la partida: "1:25 / 2:30".
    //
    // Existe para que el "Hardest stretch at 4:44" del panel de dificultad
    // sirva de algo: saber donde esta el tramo duro no vale de nada si mientras
    // tocas no sabes por donde vas.
    //
    // DE DONDE SALEN LOS NUMEROS. BassAudioManager lleva la reproduccion:
    // audioLength es la duracion y Method_Public_Double_0() la posicion actual.
    // Ese nombre lo inventa Il2CppInterop, asi que se comprobo en el volcado
    // ISIL antes de fiarse — y hace exactamente lo que hace BASS para esto:
    //
    //     018 Compare [rbx+40], 0        <- sin canal, devuelve 0
    //     034 Call <tipo>.<metodo>        <- ChannelGetPosition, en bytes
    //     037 Compare rax, -1             <- el LogWarning es la rama de error
    //     045 Call <tipo>.<metodo>        <- Bytes2Seconds
    //
    // RENDIMIENTO, que es lo que manda aqui. Esto corre en cada fotograma de la
    // cancion, que es justo donde el mod tiene prohibido gastar:
    //
    //   - si la opcion esta apagada, la primera linea sale y ya;
    //   - el texto SOLO se rehace cuando cambia el segundo entero. El resto de
    //     fotogramas son una llamada al reloj y una comparacion de enteros, sin
    //     construir ni una cadena;
    //   - los minutos y segundos se formatean a mano en vez de con string.Format
    //     o interpolacion, que reservan de mas;
    //   - el lienzo no lleva GraphicRaycaster: el reloj no se pulsa y asi no
    //     entra en el reparto de eventos de entrada.
    //
    // DONDE SE PONE. Arriba a la derecha. Empezo arriba a la izquierda,
    // buscando el contador de FPS del juego para colgarse debajo y no pisarlo;
    // moverlo a la derecha quita ese problema de raiz y ademas ese rincon esta
    // libre durante la cancion — la etiqueta de version del juego, que es lo
    // unico que vive ahi, no se muestra mientras tocas.
    public static class TiempoCancion
    {
        public const float GrosorBorde = 0.2f;
        public const float MargenDerecha = -18f;     // desde el borde derecho
        public const float MargenArriba = -14f;      // desde el borde superior

        private static bool activo;
        private static GameObject raiz;
        private static Il2CppTMPro.TextMeshProUGUI texto;
        private static RectTransform rt;
        private static bool preparado;
        private static int ultimoSegundo = -1;
        private static int ultimoTotal = -1;

        private static Color color = Color.white;
        private static Il2CppTMPro.TMP_FontAsset fuente;
        private static bool estiloResuelto;
        private static readonly Buscador.Intento intentoEstilo = new Buscador.Intento(11);

        // Al entrar y salir de la cancion. Mismo enganche que el cartel de
        // racha: el objeto se destruye con la escena.
        public static void EscenaCambiada(string nombre, bool enJuego)
        {
            activo = enJuego && Ajustes.MostrarTiempo;
            raiz = null;
            texto = null;
            rt = null;
            preparado = false;
            ultimoSegundo = -1;
            ultimoTotal = -1;
        }

        public static void Tick()
        {
            if (!activo)
            {
                return;      // apagado: ni una instruccion mas
            }
            try
            {
                if (!preparado)
                {
                    Preparar();
                    return;
                }
                if (texto == null)
                {
                    return;
                }

                Il2Cpp.BassAudioManager audio = Il2Cpp.BassAudioManager.instance;
                if (audio == null)
                {
                    return;
                }
                int segundo = (int)audio.Method_Public_Double_0();
                int total = (int)audio.audioLength;
                if (segundo == ultimoSegundo && total == ultimoTotal)
                {
                    return;      // el caso normal: dos comparaciones y fuera
                }
                ultimoSegundo = segundo;
                ultimoTotal = total;
                texto.text = Reloj(segundo) + " / " + Reloj(total);
            }
            catch (Exception ex)
            {
                activo = false;      // no se insiste durante la cancion
                MelonLogger.Warning("[Tiempo] " + ex.Message);
            }
        }

        // "m:ss", a mano. Con string.Format o interpolacion esto reservaria de
        // mas una vez por segundo, y aqui estamos en mitad de la cancion.
        private static string Reloj(int segundos)
        {
            if (segundos < 0)
            {
                segundos = 0;
            }
            int m = segundos / 60;
            int s = segundos - m * 60;
            return m.ToString() + (s < 10 ? ":0" : ":") + s.ToString();
        }

        private static void Preparar()
        {
            try
            {
                preparado = true;      // un solo intento por cancion

                // Un TMP sin TMP_FontAsset sale en blanco: se toma la fuente de
                // un texto que ya exista en la escena.
                Il2CppTMPro.TextMeshProUGUI plantilla =
                    UnityEngine.Object.FindObjectOfType<Il2CppTMPro.TextMeshProUGUI>();
                if (plantilla == null || plantilla.font == null)
                {
                    preparado = false;      // la escena aun carga; se reintenta
                    return;
                }
                ResolverEstilo();

                raiz = new GameObject("SongTimeOverlay");
                Canvas lienzo = raiz.AddComponent<Canvas>();
                lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
                lienzo.sortingOrder = 31000;
                CanvasScaler escala = raiz.AddComponent<CanvasScaler>();
                escala.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                escala.referenceResolution = new Vector2(1920f, 1080f);

                GameObject go = new GameObject("Text");
                go.transform.SetParent(raiz.transform, false);
                texto = go.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                texto.font = fuente != null ? fuente : plantilla.font;
                texto.fontSize = Ajustes.TiempoTamano;
                texto.fontStyle = Il2CppTMPro.FontStyles.Bold;
                texto.color = color;
                texto.alignment = Il2CppTMPro.TextAlignmentOptions.TopRight;
                texto.raycastTarget = false;
                texto.text = "";

                rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(420f, 60f);
                rt.anchoredPosition = new Vector2(MargenDerecha, MargenArriba);

                PonerBorde();
                MelonLogger.Msg("[Tiempo] reloj preparado");
            }
            catch (Exception ex)
            {
                activo = false;
                MelonLogger.Error("[Tiempo] al preparar: " + ex);
            }
        }

        // Mismo borde que el cartel de racha: sobre el material instanciado del
        // propio texto, no sobre el compartido.
        private static void PonerBorde()
        {
            try
            {
                Material m = texto.fontMaterial;
                if (m == null)
                {
                    return;
                }
                m.SetColor(Shader.PropertyToID("_OutlineColor"), Color.black);
                m.SetFloat(Shader.PropertyToID("_OutlineWidth"), GrosorBorde);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Tiempo] sin borde: " + ex.Message);
            }
        }

        // Color y fuente del .ini. Se resuelve fuera de la cancion (lo llama
        // Diagnostico en el menu), asi que aqui no cuesta nada.
        public static void ResolverEstilo()
        {
            if (estiloResuelto || !intentoEstilo.Toca())
            {
                return;
            }
            try
            {
                string hex = Ajustes.TiempoColor;
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
                        MelonLogger.Warning("[Tiempo] color '" + Ajustes.TiempoColor
                            + "' no se entiende; se usa el de siempre. Formato: RRGGBB");
                    }
                }

                string nombre = Ajustes.TiempoFuente;
                if (string.IsNullOrEmpty(nombre))
                {
                    estiloResuelto = true;      // la del juego
                    return;
                }
                // La misma busqueda que usa el cartel de racha: las fuentes son
                // las del juego, no hay forma de cargar una de Windows.
                fuente = RachaNotas.BuscarFuentePublica(nombre);
                estiloResuelto = fuente != null;
            }
            catch (Exception ex)
            {
                estiloResuelto = true;
                MelonLogger.Warning("[Tiempo] estilo: " + ex.Message);
            }
        }
    }
}
