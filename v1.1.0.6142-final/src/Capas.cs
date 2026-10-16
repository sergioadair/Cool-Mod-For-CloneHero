using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace CloneHeroMod
{
    // Donde va en pantalla todo lo que el mod pinta durante la cancion: por
    // encima del juego, pero POR DEBAJO DEL MENU DE PAUSA Y DEL FUNDIDO A
    // NEGRO del final. Si quedaran por encima, taparian el menu o seguirian
    // viendose con la pantalla ya en negro.
    //
    // Antes cada cosa iba por su lado: las imagenes de during_song ya se
    // colocaban bajo la pausa, pero los carteles (Note Streak, SP Ready) y el
    // reloj iban en lienzos de orden 31000, por encima de todo. Al pausar se
    // veian brillantes sobre el oscurecido de la pausa —lo reporto un usuario
    // con una captura—. Ahora todos se registran aqui y se colocan juntos.
    //
    // El orden de capas del juego no esta en su codigo (el volcado ISIL no
    // fija sortingOrder de ningun Canvas): viene de sus escenas. Asi que se
    // averigua en cada cancion: se buscan el PauseMenu y el FadeBehaviour
    // —aunque esten ocultos—, se sube por su jerarquia hasta su Canvas raiz, y
    // lo nuestro se pone justo por debajo del mas bajo de los dos, en su mismo
    // modo de dibujo. Medido en el log: la pausa es "Menu Canvas", superpuesto
    // de orden 0, y el fundido "FadeCanvas", superpuesto de orden 100.
    //
    // Dentro de lo nuestro, dos niveles: Fondo (las imagenes de during_song)
    // y encima Carteles (Note Streak, SP Ready y el reloj).
    public static class Capas
    {
        public const int Fondo = 0;
        public const int Carteles = 1;

        private static readonly List<Canvas> lienzos = new List<Canvas>();
        private static readonly List<int> niveles = new List<int>();

        private static bool activo;
        private static bool resuelto;
        private static int intentos;
        private static int espera;

        // Lo averiguado: el lienzo bajo el que hay que quedarse.
        private static RenderMode modo;
        private static int techo;
        private static Camera camara;
        private static float plano;
        private static int capa;

        public static void EscenaCambiada(bool enJuego)
        {
            lienzos.Clear();      // los destruye Unity con la escena
            niveles.Clear();
            resuelto = false;
            intentos = 0;
            espera = 0;
            activo = enJuego;
        }

        // Cada lienzo nuevo del mod durante la cancion. Si ya se sabe donde va
        // la pausa, se coloca al momento; si no, en cuanto se sepa.
        public static void Registrar(Canvas lienzo, int nivel)
        {
            if (lienzo == null) return;
            lienzos.Add(lienzo);
            niveles.Add(nivel);
            if (resuelto) Aplicar(lienzo, nivel);
        }

        // Solo trabaja hasta dar con la pausa y el fundido: despues, nada.
        public static void Tick()
        {
            if (!activo || resuelto)
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
                resuelto = Resolver(intentos >= 20);
            }
            catch (Exception ex)
            {
                resuelto = true;
                activo = false;
                MelonLogger.Warning("[Capas] " + ex.Message);
            }
        }

        private static bool Resolver(bool ultimo)
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
                activo = false;
                MelonLogger.Warning("[Capas] no se encontraron ni la pausa ni el fundido;"
                    + " lo del mod queda donde estaba");
                return true;
            }

            Canvas bajo = techos[0];
            for (int i = 1; i < techos.Count; i++)
            {
                if (Clave(techos[i]) < Clave(bajo)) bajo = techos[i];
            }
            modo = bajo.renderMode;
            techo = bajo.sortingOrder;
            camara = bajo.worldCamera;
            plano = bajo.planeDistance;
            capa = bajo.sortingLayerID;
            for (int i = 0; i < lienzos.Count; i++)
            {
                Aplicar(lienzos[i], niveles[i]);
            }
            MelonLogger.Msg("[Capas] lo del mod va por debajo de " + bajo.gameObject.name
                + " (" + modo.ToString() + ", orden " + techo.ToString() + "): "
                + lienzos.Count.ToString() + " lienzo(s)");
            return true;
        }

        private static void Aplicar(Canvas lienzo, int nivel)
        {
            if (lienzo == null) return;
            try
            {
                if (modo == RenderMode.ScreenSpaceOverlay)
                {
                    lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
                }
                else
                {
                    // En el mismo plano y camara que ese lienzo, para que el
                    // orden de capas decida entre los dos.
                    lienzo.renderMode = modo;
                    lienzo.worldCamera = camara;
                    lienzo.planeDistance = plano;
                    lienzo.sortingLayerID = capa;
                }
                // Fondo dos por debajo, Carteles uno: siempre bajo el techo.
                lienzo.sortingOrder = techo - 2 + nivel;
            }
            catch (Exception)
            {
            }
        }

        // Para comparar dos lienzos: un overlay va siempre por encima de uno
        // de camara, y dentro de cada modo manda el orden.
        private static long Clave(Canvas c)
        {
            long m = c.renderMode == RenderMode.ScreenSpaceOverlay ? 1L : 0L;
            return (m << 32) + c.sortingOrder;
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
