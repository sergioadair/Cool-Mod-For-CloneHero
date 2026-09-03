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
                bool avisando = Aviso.Activo;
                if (!calculando && !generando && !avisando)
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
                    texto.text = generando ? TextoGenerando() : Aviso.Texto;
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
        }

        private static string TextoGenerando()
        {
            int t = GeneradorCharts.Total;
            int p = GeneradorCharts.Paso;
            string salto = "\n\n";
            string cuenta = t > 0
                ? salto + p.ToString() + " / " + t.ToString()
                : "";
            return "Generating Missing Difficulties" + cuenta + salto
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
            rtFondo.anchorMin = new Vector2(0.5f, 0.5f);
            rtFondo.anchorMax = new Vector2(0.5f, 0.5f);
            rtFondo.pivot = new Vector2(0.5f, 0.5f);
            rtFondo.anchoredPosition = Vector2.zero;
            rtFondo.sizeDelta = new Vector2(760f, 300f);

            GameObject textoGo = new GameObject("Text");
            textoGo.transform.SetParent(fondoGo.transform, false);
            texto = textoGo.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
            texto.font = plantilla.font;
            texto.fontSize = 32f;
            texto.color = Color.white;
            texto.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
            texto.text = "Calculating Difficulty";
            RectTransform rtTexto = textoGo.GetComponent<RectTransform>();
            rtTexto.anchorMin = Vector2.zero;
            rtTexto.anchorMax = Vector2.one;
            rtTexto.offsetMin = new Vector2(24f, 24f);
            rtTexto.offsetMax = new Vector2(-24f, -24f);

            MelonLogger.Msg("[Overlay] cartel de progreso creado");
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
