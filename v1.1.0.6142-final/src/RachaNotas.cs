using System;
using System.IO;
using System.Reflection;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace CloneHeroMod
{
    // "50 Note Streak!" en pantalla al llegar a 50 notas seguidas sin fallar,
    // luego a 100, y de ahi cada 100.
    //
    // ES LA UNICA PARTE DEL MOD QUE CORRE DURANTE LA CANCION, asi que el
    // presupuesto por fotograma es lo primero:
    //
    //   - Con la opcion apagada, Tick() sale en la primera linea. Un bool.
    //   - Encendida y sin animacion en curso, el coste es leer UN entero.
    //   - Solo mientras el cartel se mueve (~1,8 s) se tocan color y transform.
    //
    // De donde sale el numero: el contador que el juego dibuja bajo la
    // puntuacion es un SpriteFont, y ScoreManager lo expone como comboFont.
    // SpriteFont guarda en un int privado el valor que esta dibujando, asi que
    // no hay que reimplementar nada del motor de puntuacion: se lee lo que ya
    // se ve. Los dos nombres (ScoreManager, comboFont) estan sin ofuscar.
    public static class RachaNotas
    {
        public const string Sufijo = " Note Streak!";

        private const float Duracion = 1.8f;

        // Grosor del borde negro, 0..1 en unidades de TextMeshPro. Si se sube
        // mucho, el borde se come la letra: el margen lo marca el relleno con
        // que se genero el atlas de la fuente.
        private const float GrosorBorde = 0.2f;
        private const float AlturaBase = 250f;   // por encima de la autopista
        private const float Deriva = 55f;

        // Activa solo dentro de la cancion y solo si la opcion esta puesta. Se
        // resuelve al cargar la escena, no en cada fotograma.
        private static bool activo;

        private static Il2Cpp.SpriteFont contador;
        // Se lee con un delegado, no con PropertyInfo.GetValue: esto corre en
        // cada fotograma de la cancion, y asi no hay ni despacho por reflexion
        // ni boxeo del entero. Queda en una llamada directa al getter.
        private static Func<Il2Cpp.SpriteFont, int> leerValor;
        private static int intentos;
        private static int esperaBusqueda;

        private static int ultimaRacha;
        private static int ultimoHito;

        private static GameObject raiz;
        private static Il2CppTMPro.TextMeshProUGUI texto;
        private static RectTransform rtTexto;
        private static bool animando;
        private static float t;

        // Material propio del cartel y el identificador del color de borde. El
        // color se resuelve una vez: hacerlo por nombre en cada fotograma seria
        // una busqueda por hash de mas.
        private static Material material;
        private static int idColorBorde;
        private static bool registrar;

        // Aspecto configurable desde settings.ini. Se resuelve una vez por
        // sesion, no por cancion: son ajustes que no cambian mientras se juega
        // y localizar una fuente cuesta recorrer objetos.
        private static Color color = new Color(1f, 0.82f, 0.29f, 1f);
        private static Il2CppTMPro.TMP_FontAsset fuente;
        private static bool estiloResuelto;
        private static int intentosEstilo;
        private static readonly Buscador.Intento intentoEstilo = new Buscador.Intento(23);

        // ------------------------------------------------------------ escena -
        public static void EscenaCambiada(string nombre, bool enJuego)
        {
            contador = null;
            leerValor = null;
            intentos = 0;
            esperaBusqueda = 0;
            ultimaRacha = 0;
            ultimoHito = 0;
            animando = false;
            raiz = null;      // lo destruye Unity al descargar la escena
            texto = null;
            rtTexto = null;
            material = null;

            activo = enJuego && Ajustes.MostrarRacha;
            if (activo)
            {
                registrar = File.Exists(Path.Combine(
                    MelonEnvironment.MelonLoaderDirectory, "diagnostico.flag"));
                MelonLogger.Msg("[Racha] activa en " + nombre);
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
                if (animando)
                {
                    Animar();
                }
                if (contador == null)
                {
                    Localizar();
                    return;
                }

                int racha = leerValor(contador);
                if (racha == ultimaRacha)
                {
                    return;      // lo normal entre nota y nota
                }
                if (racha < ultimaRacha)
                {
                    ultimoHito = 0;      // se rompio la racha: se empieza de nuevo
                }
                ultimaRacha = racha;

                int hito = ProximoHito(ultimoHito);
                if (racha >= hito)
                {
                    ultimoHito = hito;
                    Disparar(hito);
                }
                else if (registrar && racha % 25 == 0)
                {
                    MelonLogger.Msg("[Racha] contador=" + racha.ToString());
                }
            }
            catch (Exception ex)
            {
                activo = false;      // nunca a costa de la cancion
                MelonLogger.Error("[Racha] desactivada por error: " + ex);
            }
        }

        // Primero 50, luego 100, y de ahi de cien en cien.
        private static int ProximoHito(int anterior)
        {
            if (anterior < 50) { return 50; }
            if (anterior < 100) { return 100; }
            return anterior + 100;
        }

        // ---------------------------------------------------------- busqueda -
        // FindObjectOfType recorre todos los objetos cargados, asi que se hace
        // espaciado y se deja de intentar: si al medio minuto no aparecio, es
        // que en esta escena no hay marcador.
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

            Il2Cpp.ScoreManager marcador = UnityEngine.Object.FindObjectOfType<Il2Cpp.ScoreManager>();
            if (marcador == null || marcador.comboFont == null)
            {
                return;
            }
            PropertyInfo p = ValorDe(marcador.comboFont);
            if (p == null)
            {
                intentos = 999;      // sin esto no hay nada que hacer
                MelonLogger.Warning("[Racha] SpriteFont no expone el valor dibujado");
                return;
            }
            leerValor = Atajo(p);
            if (leerValor == null)
            {
                intentos = 999;
                return;
            }
            contador = marcador.comboFont;
            ultimaRacha = leerValor(contador);
            MelonLogger.Msg("[Racha] contador localizado (" + p.Name
                            + "), valor inicial " + ultimaRacha.ToString());

            // El cartel se monta AHORA, no en el primer hito: crear un canvas a
            // mitad de cancion se notaria como un tiron justo al llegar a 50.
            Preparar();
        }

        // SpriteFont solo declara dos enteros: maxValue, que es configuracion y
        // conserva su nombre porque no estaba ofuscado, y el valor que dibuja,
        // que si lo estaba y sale renombrado. Por descarte.
        private static PropertyInfo ValorDe(Il2Cpp.SpriteFont fuente)
        {
            PropertyInfo[] props = fuente.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.DeclaredOnly);
            PropertyInfo elegida = null;
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].PropertyType != typeof(int)
                    || props[i].GetIndexParameters().Length != 0
                    || props[i].Name == "maxValue")
                {
                    continue;
                }
                if (elegida != null)
                {
                    MelonLogger.Warning("[Racha] hay mas de un entero candidato: "
                        + elegida.Name + " y " + props[i].Name);
                    return null;
                }
                elegida = props[i];
            }
            return elegida;
        }

        // Enlaza el getter una sola vez. Si el runtime no dejara crear el
        // delegado, se cae a la reflexion de siempre antes que quedarse sin
        // funcion.
        private static Func<Il2Cpp.SpriteFont, int> Atajo(PropertyInfo p)
        {
            try
            {
                MethodInfo getter = p.GetGetMethod(true);
                if (getter != null)
                {
                    return (Func<Il2Cpp.SpriteFont, int>)Delegate.CreateDelegate(
                        typeof(Func<Il2Cpp.SpriteFont, int>), getter);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Racha] sin atajo al getter: " + ex.Message);
            }
            try
            {
                return delegate (Il2Cpp.SpriteFont f) { return (int)p.GetValue(f); };
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------ cartel -
        private static void Disparar(int hito)
        {
            if (texto == null)
            {
                Preparar();
                if (texto == null)
                {
                    return;
                }
            }
            texto.text = hito.ToString() + Sufijo;
            texto.gameObject.SetActive(true);
            animando = true;
            t = 0f;
            MelonLogger.Msg("[Racha] " + hito.ToString() + " notas seguidas");
        }

        private static void Animar()
        {
            t += Time.deltaTime;
            if (t >= Duracion)
            {
                animando = false;
                if (texto != null)
                {
                    texto.gameObject.SetActive(false);
                }
                return;
            }

            // Entra de golpe pasandose de tamano, se asienta, aguanta y se va
            // desvaneciendo mientras sube.
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
            rtTexto.localScale = new Vector3(escala, escala, 1f);
            rtTexto.anchoredPosition = new Vector2(0f, AlturaBase + Deriva * avance * avance);
            texto.color = new Color(color.r, color.g, color.b, alfa);

            // El borde se desvanece aparte: en el shader de TextMeshPro el
            // color de vertice tine la CARA de la letra, no el contorno, asi
            // que sin esto el borde se quedaria opaco mientras el texto se va.
            if (material != null)
            {
                material.SetColor(idColorBorde, new Color(0f, 0f, 0f, alfa));
            }
        }

        // Borde negro, para que el dorado se lea tambien sobre fondos claros.
        //
        // Se toca fontMaterial, que es una COPIA propia del cartel;
        // fontSharedMaterial lo usan todos los textos del juego con esa fuente
        // y les pondria borde a todos.
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
                MelonLogger.Msg("[Racha] borde puesto sobre " + material.name);
            }
            catch (Exception ex)
            {
                material = null;
                MelonLogger.Warning("[Racha] sin borde: " + ex.Message);
            }
        }

        // Color, tamano y fuente. Se llama desde los menus, antes de que
        // empiece ninguna cancion: buscar una fuente recorre objetos cargados y
        // eso no puede pasar durante el gameplay.
        public static void ResolverEstilo()
        {
            if (estiloResuelto || !intentoEstilo.Toca())
            {
                return;
            }
            try
            {
                string hex = Ajustes.RachaColor;
                if (!string.IsNullOrEmpty(hex))
                {
                    if (hex[0] != '#')
                    {
                        hex = "#" + hex;
                    }
                    if (ColorUtility.TryParseHtmlString(hex, out Color c))
                    {
                        color = c;
                    }
                    else
                    {
                        MelonLogger.Warning("[Racha] color '" + Ajustes.RachaColor
                            + "' no se entiende; se usa el de siempre. Formato: RRGGBB");
                    }
                }
                string nombre = Ajustes.RachaFuente;
                if (string.IsNullOrEmpty(nombre))
                {
                    estiloResuelto = true;      // la del juego, nada que buscar
                    return;
                }

                // Se reintenta mientras no haya textos que mirar. Al arrancar,
                // el juego todavia esta cargando y la busqueda no encuentra
                // ninguno: rendirse en el primer intento dejaba la lista de
                // fuentes vacia y al jugador sin saber que escribir.
                intentosEstilo++;
                bool ultimo = intentosEstilo >= 12;
                fuente = BuscarFuente(nombre, ultimo, out bool huboTextos);
                if (fuente != null || huboTextos || ultimo)
                {
                    estiloResuelto = true;
                }
                else
                {
                    intentoEstilo.Fallo();
                }
            }
            catch (Exception ex)
            {
                estiloResuelto = true;
                MelonLogger.Warning("[Racha] estilo: " + ex.Message);
            }
        }

        // Solo entre las fuentes que el juego ya tiene cargadas.
        //
        // SE INTENTO usar fuentes instaladas en Windows, con
        // Font.CreateDynamicFontFromOSFont, y NO SE PUEDE: el juego responde
        // "Method unstripping failed". Unity recorta en compilacion el codigo
        // que el juego no usa, y como Clone Hero nunca carga fuentes del
        // sistema, ese metodo no existe en el binario. No es algo que se pueda
        // arreglar desde fuera, asi que no merece la pena reintentarlo.
        //
        // El intento se conserva porque en otra version del juego podria estar
        // disponible, pero con su propio try: si revienta, lo importante es
        // seguir hasta la lista de fuentes disponibles, que es lo unico que le
        // sirve a quien configura esto.
        private static Il2CppTMPro.TMP_FontAsset BuscarFuente(string nombre, bool avisar,
                                                              out bool huboTextos)
        {
            huboTextos = false;
            try
            {
                // Las fuentes se sacan de los textos que hay en pantalla, no
                // con Resources.FindObjectsOfTypeAll<TMP_FontAsset>: ese
                // generico tambien esta recortado del juego y revienta con
                // "Method unstripping failed". FindObjectsOfType sobre los TMP
                // si funciona — es lo que ya usamos para clonar la fuente del
                // cartel.
                var textos = UnityEngine.Object.FindObjectsOfType<Il2CppTMPro.TextMeshProUGUI>();
                huboTextos = textos.Length > 0;
                System.Text.StringBuilder disponibles = new System.Text.StringBuilder();
                for (int i = 0; i < textos.Length; i++)
                {
                    if (textos[i] == null || textos[i].font == null)
                    {
                        continue;
                    }
                    string n = textos[i].font.name;
                    if (n.IndexOf(nombre, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        MelonLogger.Msg("[Racha] fuente del juego: " + n);
                        return textos[i].font;
                    }
                    // Sin repetir: el mismo asset lo comparten muchos textos.
                    if (disponibles.ToString().IndexOf(n, StringComparison.Ordinal) < 0)
                    {
                        disponibles.Append(n).Append(", ");
                    }
                }

                try
                {
                    Font sistema = Font.CreateDynamicFontFromOSFont(nombre, 64);
                    if (sistema != null)
                    {
                        var creada = Il2CppTMPro.TMP_FontAsset.CreateFontAsset(sistema);
                        if (creada != null)
                        {
                            MelonLogger.Msg("[Racha] fuente del sistema: " + nombre);
                            return creada;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (avisar || huboTextos)
                    {
                        MelonLogger.Msg("[Racha] las fuentes de Windows no estan"
                            + " disponibles en este juego (" + ex.Message + ")");
                    }
                }

                if (avisar || huboTextos)
                {
                    MelonLogger.Warning("[Racha] no hay ninguna fuente que se llame '"
                        + nombre + "'. Las del juego son: " + disponibles.ToString());
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Racha] fuente '" + nombre + "': " + ex.Message);
            }
            return null;
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
                    return;      // se reintenta en el siguiente hito
                }

                raiz = new GameObject("NoteStreakOverlay");
                Canvas lienzo = raiz.AddComponent<Canvas>();
                lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
                lienzo.sortingOrder = 31000;
                CanvasScaler escala = raiz.AddComponent<CanvasScaler>();
                escala.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                escala.referenceResolution = new Vector2(1920f, 1080f);
                // Sin GraphicRaycaster: el cartel no se pulsa, y asi no entra en
                // el reparto de eventos de entrada.

                GameObject go = new GameObject("Text");
                go.transform.SetParent(raiz.transform, false);
                texto = go.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                texto.font = fuente != null ? fuente : plantilla.font;
                texto.fontSize = Ajustes.RachaTamano;
                texto.fontStyle = Il2CppTMPro.FontStyles.Bold;
                texto.color = color;
                texto.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
                texto.raycastTarget = false;
                texto.text = "";

                rtTexto = go.GetComponent<RectTransform>();
                rtTexto.anchorMin = new Vector2(0.5f, 0.5f);
                rtTexto.anchorMax = new Vector2(0.5f, 0.5f);
                rtTexto.pivot = new Vector2(0.5f, 0.5f);
                rtTexto.sizeDelta = new Vector2(1200f, 140f);
                rtTexto.anchoredPosition = new Vector2(0f, AlturaBase);
                go.SetActive(false);

                PonerBorde();
                MelonLogger.Msg("[Racha] cartel preparado");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Racha] al preparar el cartel: " + ex);
                activo = false;
            }
        }
    }
}
