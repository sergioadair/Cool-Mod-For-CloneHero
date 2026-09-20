using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace CloneHeroMod
{
    // Cartel de progreso mientras se calcula la dificultad, equivalente al de
    // "Scan Songs". Sin el, el juego parece congelado durante casi un minuto.
    //
    // Se construye a mano (Canvas + panel oscuro + texto) en vez de clonar algo
    // del juego, porque el cartel de escaneo esta enterrado en la jerarquia de
    // SongScan y depende de su estado.
    //
    // TODO EL TEXTO EN INGLES: se ve en pantalla.
    public static class OverlayProgreso
    {
        private static GameObject raiz;
        private static Il2CppTMPro.TextMeshProUGUI texto;
        private static RectTransform recuadro;
        private static bool falloCreacion;

        public static void Refrescar()
        {
            try
            {
                // Tres cosas distintas comparten el mismo cartel: el
                // calculo de dificultad, la generacion de charts y los avisos
                // sueltos. Se miran en ese orden porque el calculo es el unico
                // que puede tardar un minuto.
                bool calculando = CalculadorDificultad.Corriendo;
                bool generando = GeneradorCharts.Corriendo;
                bool enLote = GeneradorLote.Corriendo;
                bool desdeAudio = GeneradorAudio.Corriendo;
                bool avisando = Aviso.Activo;
                if (!calculando && !generando && !enLote && !desdeAudio && !avisando)
                {
                    Ocultar();
                    return;
                }
                if (raiz == null && !falloCreacion)
                {
                    Crear();
                }
                if (texto == null)
                {
                    return;
                }

                if (!calculando)
                {
                    texto.text = desdeAudio ? TextoAudio()
                        : enLote ? TextoLote()
                        : generando ? TextoGenerando() : Aviso.Texto;
                    return;
                }

                int total = CalculadorDificultad.Total;
                int hechas = CalculadorDificultad.Hechas;
                int pct = total > 0 ? hechas * 100 / total : 0;

                texto.text = "Calculating Difficulty\n\n"
                    + hechas.ToString() + " / " + total.ToString()
                    + "   (" + pct.ToString() + "%)\n\n"
                    + "Written: " + CalculadorDificultad.Escritas.ToString()
                    + "    Up to date: " + CalculadorDificultad.AlDia.ToString()
                    + "    Skipped: " + CalculadorDificultad.Saltadas.ToString()
                    + "    No data: " + CalculadorDificultad.Falladas.ToString()
                    + "\n\nPlease wait...";
            }
            catch (Exception ex)
            {
                falloCreacion = true;
                MelonLogger.Error("[Overlay] " + ex);
            }
            Ajustar();
        }

        // Aqui la cuenta importa mas que el porcentaje: son pocas carpetas
        // y cada una tarda un par de segundos, asi que se dice cual va.
        private static string TextoAudio()
        {
            string salto = "\n\n";
            int t = GeneradorAudio.Total;
            int h = GeneradorAudio.Hechas;
            return "Generating Charts From Audio" + salto
                + h.ToString() + " / " + t.ToString() + salto
                + Recortar(GeneradorAudio.Actual, 40) + salto
                + "Created: " + GeneradorAudio.Generadas.ToString()
                + "    Skipped: " + GeneradorAudio.Saltadas.ToString()
                + "    Failed: " + GeneradorAudio.Fallidas.ToString()
                + salto + "Please wait...";
        }

        // Un nombre de carpeta largo partia en varias lineas y volvia a
        // desbordar el cartel por mucho que este crezca.
        private static string Recortar(string t, int tope)
        {
            if (string.IsNullOrEmpty(t)) return "";
            return t.Length <= tope ? t : t.Substring(0, tope - 1) + "...";
        }

        private static string TextoLote()
        {
            string salto = "\n\n";
            int t = GeneradorLote.Total;
            int h = GeneradorLote.Hechas;
            int pct = t > 0 ? h * 100 / t : 0;
            return (GeneradorLote.Restaurando
                       ? "Restoring All Song Charts" : "Generating All Difficulties")
                + salto + h.ToString() + " / " + t.ToString()
                + "   (" + pct.ToString() + "%)" + salto
                + "Songs changed: " + GeneradorLote.Cambiadas.ToString()
                + "    Added: " + GeneradorLote.Dificultades.ToString()
                + "    Failed: " + GeneradorLote.Fallidas.ToString()
                + salto + "Please wait...";
        }

        private static string TextoGenerando()
        {
            int t = GeneradorCharts.Total;
            int p = GeneradorCharts.Paso;
            string salto = "\n\n";
            string cuenta = t > 0
                ? salto + p.ToString() + " / " + t.ToString()
                : "";
            return "Generating Song Difficulties" + cuenta + salto
                + (GeneradorCharts.Mensaje ?? "") + salto + "Please wait...";
        }

        private static void Crear()
        {
            // La fuente se toma de un texto que ya exista en la escena: crear un
            // TMP sin TMP_FontAsset sale en blanco.
            Il2CppTMPro.TextMeshProUGUI plantilla =
                UnityEngine.Object.FindObjectOfType<Il2CppTMPro.TextMeshProUGUI>();
            if (plantilla == null || plantilla.font == null)
            {
                return;      // se reintenta en el siguiente fotograma
            }

            raiz = new GameObject("DifficultyProgressOverlay");
            UnityEngine.Object.DontDestroyOnLoad(raiz);

            Canvas lienzo = raiz.AddComponent<Canvas>();
            lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
            lienzo.sortingOrder = 32000;      // por encima de todo lo del juego
            CanvasScaler escala = raiz.AddComponent<CanvasScaler>();
            escala.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            escala.referenceResolution = new Vector2(1920f, 1080f);
            raiz.AddComponent<GraphicRaycaster>();

            // Recuadro centrado, no pantalla completa: solo tapa lo que ocupa.
            GameObject fondoGo = new GameObject("Panel");
            fondoGo.transform.SetParent(raiz.transform, false);
            Image fondo = fondoGo.AddComponent<Image>();
            fondo.color = new Color(0f, 0f, 0f, 0.9f);
            RectTransform rtFondo = fondoGo.GetComponent<RectTransform>();
            recuadro = rtFondo;
            rtFondo.anchorMin = new Vector2(0.5f, 0.5f);
            rtFondo.anchorMax = new Vector2(0.5f, 0.5f);
            rtFondo.pivot = new Vector2(0.5f, 0.5f);
            rtFondo.anchoredPosition = Vector2.zero;
            rtFondo.sizeDelta = new Vector2(AnchoCartel, AltoMinimo);

            GameObject textoGo = new GameObject("Text");
            textoGo.transform.SetParent(fondoGo.transform, false);
            texto = textoGo.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
            texto.font = plantilla.font;
            texto.fontSize = Tipo;
            texto.color = Color.white;
            texto.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
            texto.text = "Calculating Difficulty";
            RectTransform rtTexto = textoGo.GetComponent<RectTransform>();
            rtTexto.anchorMin = Vector2.zero;
            rtTexto.anchorMax = Vector2.one;
            rtTexto.offsetMin = new Vector2(Margen, Margen);
            rtTexto.offsetMax = new Vector2(-Margen, -Margen);

            MelonLogger.Msg("[Overlay] cartel de progreso creado");
        }

        // El recuadro crece con el texto, CONTANDO LINEAS.
        //
        // El primer intento le preguntaba a TMP su alto preferido con
        // GetPreferredValues. No funciono: el cartel salio mas pequeño que
        // antes y el texto seguia saliendose, asi que la llamada devolvia algo
        // inservible —o fallaba en silencio— y se quedaba en el minimo.
        //
        // Contar lineas a mano es predecible: el texto lo componemos nosotros
        // y sabemos cuantos saltos lleva. Lo unico estimado es cuantas veces
        // parte una linea larga, y para eso basta un ancho medio de letra.
        public const float AnchoCartel = 760f;
        public const float Margen = 24f;
        public const float Tipo = 32f;           // el mismo fontSize del texto
        // Estos dos numeros se quedaron cortos dos veces seguidas: el cartel
        // crecia, pero no lo bastante, y el texto seguia saliendose. Asi que
        // se dejan claramente holgados. Que sobre un dedo de negro no lo ve
        // nadie; que falte, si.
        public const float AltoLinea = 1.70f;    // interlineado, con margen
        public const float AnchoLetra = 0.55f;   // ancho medio de letra, en tipos
        public const float Holgura = 2f;         // lineas de propina
        public const float AltoMinimo = 440f;
        public const float AltoMaximo = 860f;

        private static void Ajustar()
        {
            if (recuadro == null || texto == null)
            {
                return;
            }
            try
            {
                string t = texto.text;
                if (string.IsNullOrEmpty(t))
                {
                    return;
                }
                int porLinea = (int)((AnchoCartel - Margen * 2f) / (Tipo * AnchoLetra));
                if (porLinea < 8)
                {
                    porLinea = 8;
                }
                int lineas = 0;
                foreach (string linea in t.Split('\n'))
                {
                    lineas += 1 + linea.Length / porLinea;   // una vacia tambien ocupa
                }
                float alto = (lineas + Holgura) * Tipo * AltoLinea + Margen * 2f;
                if (alto < AltoMinimo) alto = AltoMinimo;
                if (alto > AltoMaximo) alto = AltoMaximo;
                if (Mathf.Abs(recuadro.sizeDelta.y - alto) > 1f)
                {
                    recuadro.sizeDelta = new Vector2(AnchoCartel, alto);
                    // una linea por cambio de tamano, no por fotograma: si
                    // vuelve a quedarse corto, el log dice con que cuentas
                    MelonLogger.Msg("[Overlay] cartel a " + alto.ToString("0")
                        + " px para " + lineas.ToString() + " lineas");
                }
            }
            catch (Exception)
            {
            }
        }

        private static void Ocultar()
        {
            if (raiz == null)
            {
                return;
            }
            UnityEngine.Object.Destroy(raiz);
            raiz = null;
            texto = null;
            MelonLogger.Msg("[Overlay] cartel retirado");
        }
    }
}
