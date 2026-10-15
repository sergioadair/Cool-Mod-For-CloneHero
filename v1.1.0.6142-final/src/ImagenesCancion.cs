using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace CloneHeroMod
{
    // Imagenes fijas durante la cancion: todo lo que haya en
    // Custom/Textures/during_song se pinta estirado a toda la pantalla, sea
    // cual sea su resolucion y la del jugador, mientras se toca, y se va
    // cuando la cancion acaba.
    //
    // ENCIMA DE TODO, MENOS DE DOS COSAS: el menu de pausa y el fundido a
    // negro del final. Si quedaran por encima, taparian el menu o seguirian
    // viendose con la pantalla ya en negro.
    //
    // El orden de capas del juego no esta en su codigo (el volcado ISIL no
    // fija sortingOrder de ningun Canvas): viene de sus escenas. Asi que se
    // averigua al empezar cada cancion: se buscan el PauseMenu y el
    // FadeBehaviour —aunque esten ocultos—, se sube por su jerarquia hasta su
    // Canvas raiz, y el nuestro se pone justo por debajo del mas bajo de los
    // dos, en su mismo modo de dibujo. Lo encontrado se deja en el log.
    //
    // DESAPARECER AL ACABAR sale solo: el lienzo pertenece a la escena de la
    // cancion, y al acabar el juego carga otra (EndOfSong). Lo mismo al
    // salir al menu.
    //
    // ORDEN ENTRE ELLAS: el natural de los nombres (1, 2 ... 10), y cada una
    // queda por encima de las anteriores, como capas numeradas de abajo
    // arriba.
    public static class ImagenesCancion
    {
        public const string NombreCarpeta = "during_song";

        // Mientras no se sepa donde van la pausa y el fundido: por debajo de
        // los carteles del mod (31000) y por encima de casi cualquier cosa.
        private const int OrdenProvisional = 30000;

        private static Texture2D[] imagenes;
        private static bool cargadas;

        private static bool activo;
        private static GameObject raiz;
        private static Canvas lienzo;
        private static bool colocado;
        private static int intentos;
        private static int espera;

        // ------------------------------------------------------------ carga -
        // Desde los menus, nunca durante la cancion.
        public static void Cargar()
        {
            if (cargadas)
            {
                return;
            }
            cargadas = true;
            try
            {
                string carpeta = Carpeta();
                firmaCargada = CartelImagen.FirmaCarpeta(carpeta);
                if (carpeta == null || !Directory.Exists(carpeta))
                {
                    return;
                }
                imagenes = CartelImagen.LeerCarpeta(carpeta, "[DuranteCancion] " + NombreCarpeta);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[DuranteCancion] " + ex.Message);
            }
        }

        private static string firmaCargada;

        private static string Carpeta()
        {
            string raizTexturas = RutasJuego.CarpetaCustom(TexturasPersonalizadas.NombreCarpeta);
            return string.IsNullOrEmpty(raizTexturas) ? null : Path.Combine(raizTexturas, NombreCarpeta);
        }

        // Las texturas viejas se destruyen: llevan HideAndDontSave, asi que
        // Unity no las soltaria nunca por su cuenta.
        private static void Descartar()
        {
            if (imagenes != null)
            {
                for (int i = 0; i < imagenes.Length; i++)
                {
                    if (imagenes[i] != null) UnityEngine.Object.Destroy(imagenes[i]);
                }
            }
            imagenes = null;
            cargadas = false;
        }

        // ------------------------------------------------------------ escena -
        public static void EscenaCambiada(string nombre, bool enJuego)
        {
            raiz = null;      // lo destruye Unity con la escena
            lienzo = null;
            colocado = false;
            intentos = 0;
            espera = 0;

            // RECARGA AL VOLVER AL MENU PRINCIPAL, SI CAMBIO ALGO. Antes las
            // imagenes se leian una vez por sesion, y cambiar la carpeta con el
            // juego abierto no tenia efecto hasta reiniciarlo. Luego se
            // releian siempre, y con imagenes grandes salir de una cancion
            // tardaba. Ahora se compara la firma de la carpeta (nombres,
            // tamanos y fechas) y solo se relee si es distinta.
            if (nombre == Buscador.EscenaMenu)
            {
                string firma = CartelImagen.FirmaCarpeta(Carpeta());
                if (firma != firmaCargada)
                {
                    Descartar();
                    Cargar();
                }
            }
            if (enJuego && !cargadas)
            {
                Cargar();
            }
            activo = enJuego && imagenes != null && imagenes.Length > 0;
            if (activo)
            {
                Montar();
            }
        }

        // Solo trabaja hasta dar con la pausa y el fundido: despues, nada.
        public static void Tick()
        {
            if (!activo || colocado || lienzo == null)
            {
                return;
            }
            if (espera > 0)
            {
                espera--;
                return;
            }
            espera = 30;
            intentos++;
            try
            {
                colocado = Colocar(intentos >= 20);
            }
            catch (Exception ex)
            {
                colocado = true;
                MelonLogger.Warning("[DuranteCancion] orden de capas: " + ex.Message);
            }
        }

        // ------------------------------------------------------------ montar -
        private static void Montar()
        {
            try
            {
                raiz = new GameObject("DuringSongImages");
                lienzo = raiz.AddComponent<Canvas>();
                lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
                lienzo.sortingOrder = OrdenProvisional;
                CanvasScaler escala = raiz.AddComponent<CanvasScaler>();
                escala.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                escala.referenceResolution = new Vector2(1920f, 1080f);
                for (int i = 0; i < imagenes.Length; i++)
                {
                    // a toda la pantalla, sea cual sea su resolucion
                    RawImage img = CartelImagen.Poner(raiz.transform, imagenes[i], true);
                    img.gameObject.name = "Imagen" + (i + 1).ToString();
                }
                MelonLogger.Msg("[DuranteCancion] " + imagenes.Length.ToString()
                    + " imagen(es) en pantalla");
            }
            catch (Exception ex)
            {
                activo = false;
                MelonLogger.Error("[DuranteCancion] al montar: " + ex);
            }
        }

        // Justo por debajo de la pausa y del fundido. true cuando ya no hay
        // nada mas que hacer: colocado, o rendido.
        private static bool Colocar(bool ultimo)
        {
            List<Canvas> techos = new List<Canvas>();
            Canvas pausa = LienzoDe(Il2CppInterop.Runtime.Il2CppType.Of<Il2Cpp.PauseMenu>());
            Canvas fundido = LienzoDe(Il2CppInterop.Runtime.Il2CppType.Of<Il2Cpp.FadeBehaviour>());
            if (pausa != null) techos.Add(pausa);
            if (fundido != null && fundido != pausa) techos.Add(fundido);
            if (techos.Count < 2 && !ultimo)
            {
                return false;      // se reintenta: puede que aun no existan
            }
            if (techos.Count == 0)
            {
                MelonLogger.Warning("[DuranteCancion] no se encontraron ni la pausa ni el"
                    + " fundido; las imagenes quedan en el orden " + OrdenProvisional.ToString());
                return true;
            }

            Canvas bajo = techos[0];
            for (int i = 1; i < techos.Count; i++)
            {
                if (Clave(techos[i]) < Clave(bajo)) bajo = techos[i];
            }
            for (int i = 0; i < techos.Count; i++)
            {
                MelonLogger.Msg("[DuranteCancion] " + (techos[i] == pausa ? "pausa" : "fundido")
                    + ": " + techos[i].gameObject.name + "  modo " + techos[i].renderMode.ToString()
                    + "  orden " + techos[i].sortingOrder.ToString()
                    + "  capa " + techos[i].sortingLayerName);
            }

            if (bajo.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            else
            {
                // En el mismo plano y camara que ese lienzo, para que el orden
                // de capas decida entre los dos.
                lienzo.renderMode = bajo.renderMode;
                lienzo.worldCamera = bajo.worldCamera;
                lienzo.planeDistance = bajo.planeDistance;
                lienzo.sortingLayerID = bajo.sortingLayerID;
            }
            lienzo.sortingOrder = bajo.sortingOrder - 1;
            MelonLogger.Msg("[DuranteCancion] colocadas por debajo de "
                + bajo.gameObject.name + ": modo " + lienzo.renderMode.ToString()
                + ", orden " + lienzo.sortingOrder.ToString());
            return true;
        }

        // Para comparar dos lienzos: un overlay va siempre por encima de uno
        // de camara, y dentro de cada modo manda el orden.
        private static long Clave(Canvas c)
        {
            long modo = c.renderMode == RenderMode.ScreenSpaceOverlay ? 1L : 0L;
            return (modo << 32) + c.sortingOrder;
        }

        // El Canvas raiz del primer objeto de ese tipo, este activo o no.
        private static Canvas LienzoDe(Il2CppSystem.Type tipo)
        {
            UnityEngine.Object[] todos = Buscar(tipo);
            for (int i = 0; todos != null && i < todos.Length; i++)
            {
                Component c = todos[i] == null ? null : todos[i].TryCast<Component>();
                if (c == null) continue;
                Canvas raizCanvas = null;
                Transform t = c.transform;
                while (t != null)
                {
                    Component k = t.GetComponent(Il2CppInterop.Runtime.Il2CppType.Of<Canvas>());
                    if (k != null) raizCanvas = k.TryCast<Canvas>();
                    t = t.parent;
                }
                if (raizCanvas != null) return raizCanvas;
            }
            return null;
        }

        private static bool sinInactivos;

        private static UnityEngine.Object[] Buscar(Il2CppSystem.Type tipo)
        {
            if (!sinInactivos)
            {
                try
                {
                    return UnityEngine.Object.FindObjectsOfType(tipo, true);
                }
                catch (Exception)
                {
                    sinInactivos = true;      // recortado del juego: solo activos
                }
            }
            return UnityEngine.Object.FindObjectsOfType(tipo);
        }
    }
}
