using System;
using MelonLoader;

namespace CloneHeroMod
{
    // Lanza el "Scan Songs" del propio juego cuando una opcion del mod termina
    // de tocar la biblioteca (charts generados, dificultades anadidas,
    // restauradas o calculadas), para que no haya que hacerlo a mano.
    //
    // COMO LO HACE EL JUEGO. Su opcion Settings > General > Scan Songs, al
    // confirmar, hace exactamente dos llamadas sobre su componente SongScan
    // —y el menu principal, las mismas dos—: una de preparacion y la que
    // arranca el escaneo con true. Se buscan por su nombre real (Nativo): en
    // la 6142 eran Method_Public_Void_0 y Method_Public_Coroutine_Boolean_0,
    // pero ese numero cambia de una version del juego a otra.
    //
    // Las tareas del mod corren en hilos aparte y Unity solo deja tocar sus
    // objetos desde el principal, asi que aqui solo se apunta el pedido, y
    // Tick —que corre en los menus— lo lanza en el siguiente fotograma.
    public static class Reescaneo
    {
        private static volatile bool pedido;
        private static string quien;
        private static System.Reflection.MethodInfo preparar;
        private static System.Reflection.MethodInfo escanear;
        private static readonly Buscador.Intento intento = new Buscador.Intento(17);

        // Desde cualquier hilo.
        public static void Pedir(string motivo)
        {
            quien = motivo;
            pedido = true;
        }

        public static void Tick()
        {
            if (!pedido || Buscador.EnJuego || !intento.Toca())
            {
                return;
            }
            if (Ahora("tras: " + quien) == null)
            {
                intento.Fallo();      // se reintenta espaciado
                return;
            }
            pedido = false;
        }

        // Lanza el escaneo ya, desde el hilo principal. Devuelve el SongScan
        // que escanea (para vigilar su isScanning), o null si no se pudo.
        public static Il2Cpp.SongScan Ahora(string motivo)
        {
            try
            {
                Il2Cpp.SongScan scan = UnityEngine.Object.FindObjectOfType<Il2Cpp.SongScan>();
                if (scan == null)
                {
                    return null;
                }
                if (preparar == null || escanear == null)
                {
                    preparar = Nativo.Metodo(typeof(Il2Cpp.SongScan), Real.PrepararEscaneo);
                    escanear = Nativo.Metodo(typeof(Il2Cpp.SongScan), Real.Escanear, typeof(bool));
                    if (preparar == null || escanear == null)
                    {
                        MelonLogger.Warning("[Reescaneo] no se localizo el escaneo del juego;"
                            + " usa Settings > General > Scan Songs");
                        return null;
                    }
                }
                preparar.Invoke(scan, null);
                escanear.Invoke(scan, new object[] { true });
                MelonLogger.Msg("[Reescaneo] escaneando la biblioteca " + motivo);
                return scan;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Reescaneo] no se pudo lanzar el escaneo (" + ex.Message
                    + "); usa Settings > General > Scan Songs");
                return null;
            }
        }
    }
}
