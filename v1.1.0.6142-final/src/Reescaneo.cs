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
    // arranca el escaneo con true. En el interop son Method_Public_Void_0 y
    // Method_Public_Coroutine_Boolean_0: los unicos de su firma con llamadas
    // entrantes (cuatro y tres); los demas son senuelos del ofuscador, con
    // _PDM_.
    //
    // Las tareas del mod corren en hilos aparte y Unity solo deja tocar sus
    // objetos desde el principal, asi que aqui solo se apunta el pedido, y
    // Tick —que corre en los menus— lo lanza en el siguiente fotograma.
    public static class Reescaneo
    {
        private static volatile bool pedido;
        private static string quien;
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
            try
            {
                Il2Cpp.SongScan scan = UnityEngine.Object.FindObjectOfType<Il2Cpp.SongScan>();
                if (scan == null)
                {
                    intento.Fallo();      // se reintenta espaciado
                    return;
                }
                pedido = false;
                scan.Method_Public_Void_0();
                scan.Method_Public_Coroutine_Boolean_0(true);
                MelonLogger.Msg("[Reescaneo] escaneando la biblioteca tras: " + quien);
            }
            catch (Exception ex)
            {
                pedido = false;
                MelonLogger.Warning("[Reescaneo] no se pudo lanzar el escaneo (" + ex.Message
                    + "); usa Settings > General > Scan Songs");
            }
        }
    }
}
