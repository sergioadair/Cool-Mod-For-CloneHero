using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace CloneHeroMod
{
    // Panel con el perfil de dificultad de la cancion resaltada.
    //
    // POR QUE UN PANEL Y NO MAS TEXTO EN EL DE SIEMPRE: la etiqueta
    // "Difficulty: 41" esta alineada a la izquierda y pegada al borde inferior
    // del panel de detalles porque no queda mas sitio — ese panel ya lleva
    // caratula, artista, album, charter, genero, duracion, ano, puntuacion,
    // estrellas y modificadores. Para tres barras hace falta otra superficie.
    //
    // EL DISPARADOR ES DEL JUEGO. El mod no puede leer la entrada sin pelearse
    // con los mapeos de cada jugador (ya paso con el usuario de teclado que no
    // podia cambiar el slideshow). Pero el juego ya tiene un panel que se abre
    // manteniendo el azul —"(Hold) Show Scoring Info"— y su clase ScoringPanel
    // esta SIN OFUSCAR y expone un "public bool isActive". Asi que no hay que
    // inventar nada: se vigila ese bool y se acompana con lo nuestro.
    //
    // Y ES UN PANEL PROPIO, no lineas metidas en el suyo: el del juego esta
    // dimensionado para su contenido, y agrandar contenedores ajenos es
    // exactamente lo que descuadro los menus tres veces seguidas. Su
    // disparador si, su geometria no.
    public static class PanelPerfil
    {
        // Medidas ajustadas a ojo sobre una captura: el panel debe ocupar lo
        // menos posible sin apretar el texto. El desperdicio grande estaba a la
        // derecha de las barras, asi que el bloque de cifras se pega a ellas en
        // vez de al borde.
        private const float Ancho = 460f;
        private const float Alto = 232f;
        private const float AltoBarra = 14f;
        private const float AnchoBarra = 300f;
        private const float MargenX = 18f;
        private const float AnchoEtiqueta = 112f;
        private const float SaltoFila = 30f;

        private static Il2Cpp.ScoringPanel panelJuego;
        private static readonly Buscador.Intento intento = new Buscador.Intento(19);

        private static bool visibleAntes;
        private static string ultimaRuta;

        private static GameObject raiz;
        private static Il2CppTMPro.TextMeshProUGUI titulo;
        private static Il2CppTMPro.TextMeshProUGUI cifras;
        private static Il2CppTMPro.TextMeshProUGUI pie;
        private static Il2CppTMPro.TextMeshProUGUI instrumentos;
        private static Il2CppTMPro.TextMeshProUGUI subtitulo;
        private static Il2CppTMPro.TextMeshProUGUI[] etiquetas;
        private static RectTransform[] rellenos;
        private static bool falloCreacion;

        private static readonly string[] Nombres = { "Chords", "Technical", "Endurance" };
        private static readonly Color Dorado = new Color(1f, 0.82f, 0.29f, 1f);

        public static void EscenaCambiada()
        {
            panelJuego = null;
            raiz = null;      // lo destruye Unity con la escena
            titulo = null;
            visibleAntes = false;
            ultimaRuta = null;
            falloCreacion = false;
        }

        // Corre en cada fotograma DE MENU. Localizado el panel del juego, el
        // coste es leer un bool.
        public static void Tick()
        {
            try
            {
                if (panelJuego == null)
                {
                    if (!intento.Toca())
                    {
                        return;
                    }
                    panelJuego = UnityEngine.Object.FindObjectOfType<Il2Cpp.ScoringPanel>();
                    if (panelJuego == null)
                    {
                        intento.Fallo();
                        return;
                    }
                    intento.Exito();
                    MelonLogger.Msg("[Perfil] panel del juego localizado");
                    // El canvas se monta AHORA, no al abrirse por primera vez:
                    // crearlo incluye un FindObjectOfType que recorre todos los
                    // objetos cargados, y hacerlo en el instante de abrir el
                    // panel es justo cuando se nota. Mismo criterio que el
                    // cartel de racha.
                    Crear();
                }

                bool visible = panelJuego.isActive;
                if (visible == visibleAntes)
                {
                    // Abierto y quieto: puede haberse cambiado de cancion sin
                    // cerrarlo, asi que se comprueba cual es.
                    if (visible)
                    {
                        // forzar: el perfil del chart se calcula en otro hilo y
                        // puede llegar despues de abrirse el panel.
                        Rellenar(true);
                    }
                    return;
                }
                visibleAntes = visible;
                if (visible)
                {
                    Rellenar(true);
                }
                else if (raiz != null)
                {
                    raiz.SetActive(false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Perfil] " + ex.Message);
                panelJuego = null;
            }
        }

        private static void Rellenar(bool forzar)
        {
            string ruta = EtiquetaDificultad.RutaIniActual();
            if (!forzar && ruta == ultimaRuta)
            {
                return;      // misma cancion: nada que rehacer
            }
            ultimaRuta = ruta;

            Dificultad.Perfil p = Dificultad.PerfilDe(ruta);
            if (p == null)
            {
                if (raiz != null)
                {
                    raiz.SetActive(false);
                }
                return;      // sin calcular: no se ensena un panel vacio
            }
            if (raiz == null)
            {
                Crear();
                if (raiz == null)
                {
                    return;
                }
            }
            raiz.SetActive(true);

            // La nota grande es de la CANCION y sale del song.ini: es la idea
            // rapida de con que te vas a encontrar, y no depende de con que la
            // cojas. Todo lo demas describe UN chart concreto.
            titulo.text = "Difficulty " + p.global.ToString();

            // Y ese chart es, si se puede, el que el jugador tiene elegido. Lo
            // de antes describia siempre el chart mas dificil de la cancion, lo
            // cual enganaba: si el mas duro era la guitarra y tocabas bateria,
            // las barras hablaban de otra cosa.
            Dificultad.Perfil detalle = null;
            string cual = "Hardest chart";
            if (SeleccionJugador.Leer(out string pista, out int dificultad)
                && EtiquetaDificultad.ChartActual(out string chart, out bool esMidi))
            {
                Dificultad.Perfil c = PerfilChart.Pedir(chart, esMidi, pista, dificultad,
                                                        out bool listo);
                if (c != null)
                {
                    detalle = c;
                    cual = Dificultad.NombreDificultad(dificultad) + " "
                         + Dificultad.NombrePista(pista);
                }
                else if (listo)
                {
                    // La cancion no trae ese instrumento. El juego dice
                    // "No Part" en su panel; aqui se dice igual de claro, y se
                    // avisa de que lo que se ve debajo es de otro chart.
                    cual = "No " + Dificultad.NombrePista(pista)
                         + " - showing hardest";
                }
            }
            if (detalle == null)
            {
                detalle = p;      // lo del song.ini mientras tanto
            }
            subtitulo.text = cual;

            cifras.text = Miles(detalle.notas) + " notes\n"
                + detalle.npsMedio.ToString("0.0") + " avg NPS   "
                + detalle.npsMax.ToString("0.0") + " max";

            int[] valores = { detalle.acordes, detalle.tecnica, detalle.resistencia };
            for (int i = 0; i < rellenos.Length; i++)
            {
                float v = valores[i] / 100f;
                if (v < 0f) { v = 0f; }
                if (v > 1f) { v = 1f; }
                rellenos[i].sizeDelta = new Vector2(AnchoBarra * v, AltoBarra);
                etiquetas[i].text = Nombres[i];
            }
            pie.text = detalle.picoSegundo > 0
                ? "Hardest stretch at " + Reloj(detalle.picoSegundo)
                : "";
            instrumentos.text = PorInstrumento(p);
        }

        // La nota global mezcla todos los instrumentos; esto dice la de cada
        // uno, que es lo que de verdad importa segun con que se coja la
        // cancion. Solo salen los que la cancion trae.
        private static string PorInstrumento(Dificultad.Perfil p)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < p.porInstrumento.Length; i++)
            {
                if (p.porInstrumento[i] < 0)
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append("   ");
                }
                sb.Append(Dificultad.InstrumentosNombre[i]).Append(' ')
                  .Append(p.porInstrumento[i].ToString());
            }
            return sb.ToString();
        }

        private static string Miles(int n)
        {
            return n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string Reloj(int segundos)
        {
            return (segundos / 60).ToString() + ":" + (segundos % 60).ToString("00");
        }

        // ------------------------------------------------------------ montaje
        private static void Crear()
        {
            try
            {
                if (falloCreacion)
                {
                    return;
                }
                // Un TMP sin fuente sale en blanco: se toma de un texto que ya
                // exista en la escena.
                Il2CppTMPro.TextMeshProUGUI plantilla =
                    UnityEngine.Object.FindObjectOfType<Il2CppTMPro.TextMeshProUGUI>();
                if (plantilla == null || plantilla.font == null)
                {
                    return;      // se reintenta al abrir otra vez
                }

                raiz = new GameObject("DifficultyProfilePanel");
                Canvas lienzo = raiz.AddComponent<Canvas>();
                lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
                lienzo.sortingOrder = 31500;      // por encima del panel del juego
                CanvasScaler escala = raiz.AddComponent<CanvasScaler>();
                escala.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                escala.referenceResolution = new Vector2(1920f, 1080f);
                // Sin GraphicRaycaster: no se pulsa nada aqui.

                // Debajo del panel del juego, que esta centrado.
                GameObject fondoGo = new GameObject("Panel");
                fondoGo.transform.SetParent(raiz.transform, false);
                Image fondo = fondoGo.AddComponent<Image>();
                fondo.color = new Color(0.04f, 0.04f, 0.05f, 0.94f);
                fondo.raycastTarget = false;
                RectTransform rf = fondoGo.GetComponent<RectTransform>();
                // Casi pegado al panel del juego, que termina en y = -170.
                // Como este va anclado por su centro, el borde de arriba cae en
                // y + Alto/2: -268 + 95 = -173.
                Centrar(rf, new Vector2(0f, -289f), new Vector2(Ancho, Alto));

                titulo = Texto(fondoGo, plantilla, 30f, Il2CppTMPro.TextAlignmentOptions.TopLeft);
                Colocar(titulo, new Vector2(MargenX, -10f), new Vector2(200f, 40f));
                titulo.color = Dorado;
                titulo.fontStyle = Il2CppTMPro.FontStyles.Bold;

                cifras = Texto(fondoGo, plantilla, 19f, Il2CppTMPro.TextAlignmentOptions.TopRight);
                Colocar(cifras, new Vector2(Ancho - MargenX - 240f, -12f), new Vector2(240f, 46f));

                subtitulo = Texto(fondoGo, plantilla, 18f,
                                  Il2CppTMPro.TextAlignmentOptions.TopLeft);
                Colocar(subtitulo, new Vector2(MargenX, -46f), new Vector2(280f, 24f));
                subtitulo.color = new Color(0.72f, 0.72f, 0.76f, 1f);

                etiquetas = new Il2CppTMPro.TextMeshProUGUI[3];
                rellenos = new RectTransform[3];
                for (int i = 0; i < 3; i++)
                {
                    float y = -78f - i * SaltoFila;
                    etiquetas[i] = Texto(fondoGo, plantilla, 20f,
                                         Il2CppTMPro.TextAlignmentOptions.MidlineLeft);
                    Colocar(etiquetas[i], new Vector2(MargenX, y),
                            new Vector2(AnchoEtiqueta, 26f));

                    // Riel y relleno. Con imagenes y no con caracteres de
                    // bloque: la fuente del juego no tiene por que traerlos.
                    float xBarra = MargenX + AnchoEtiqueta + 6f;
                    RectTransform riel = Barra(fondoGo, new Color(1f, 1f, 1f, 0.13f));
                    Colocar(riel, new Vector2(xBarra, y - 6f),
                            new Vector2(AnchoBarra, AltoBarra));

                    rellenos[i] = Barra(fondoGo, Dorado);
                    Colocar(rellenos[i], new Vector2(xBarra, y - 6f),
                            new Vector2(AnchoBarra, AltoBarra));
                }

                instrumentos = Texto(fondoGo, plantilla, 18f,
                                     Il2CppTMPro.TextAlignmentOptions.TopLeft);
                Colocar(instrumentos, new Vector2(MargenX, -170f),
                        new Vector2(Ancho - MargenX * 2f, 26f));
                instrumentos.color = new Color(0.88f, 0.88f, 0.9f, 1f);

                pie = Texto(fondoGo, plantilla, 19f, Il2CppTMPro.TextAlignmentOptions.TopLeft);
                Colocar(pie, new Vector2(MargenX, -196f), new Vector2(Ancho - MargenX * 2f, 26f));
                pie.color = new Color(0.75f, 0.75f, 0.78f, 1f);

                MelonLogger.Msg("[Perfil] panel creado");
            }
            catch (Exception ex)
            {
                falloCreacion = true;
                raiz = null;
                MelonLogger.Error("[Perfil] al crear: " + ex);
            }
        }

        private static void Centrar(RectTransform rt, Vector2 pos, Vector2 tam)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = tam;
        }

        // Todo dentro del panel se ancla arriba-izquierda: asi las
        // coordenadas son las que se leen en el codigo, sin cuentas.
        private static void Colocar(Component c, Vector2 pos, Vector2 tam)
        {
            RectTransform rt = c.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = tam;
        }

        private static Il2CppTMPro.TextMeshProUGUI Texto(GameObject padre,
            Il2CppTMPro.TextMeshProUGUI plantilla, float tam,
            Il2CppTMPro.TextAlignmentOptions alineacion)
        {
            GameObject go = new GameObject("Text");
            go.transform.SetParent(padre.transform, false);
            var t = go.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
            t.font = plantilla.font;
            t.fontSize = tam;
            t.color = Color.white;
            t.alignment = alineacion;
            t.raycastTarget = false;
            t.text = "";
            return t;
        }

        private static RectTransform Barra(GameObject padre, Color color)
        {
            GameObject go = new GameObject("Bar");
            go.transform.SetParent(padre.transform, false);
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go.GetComponent<RectTransform>();
        }
    }
}
