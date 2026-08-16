using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace CloneHeroMod
{
    // Carpeta de datos del jugador, que NO esta en el mismo sitio en las dos
    // formas de instalar el juego:
    //
    //   portable  ->  <carpeta del juego>\PlayerData
    //   normal    ->  Documents\Clone Hero
    //
    // El juego decide con esa misma regla: si existe PlayerData junto al
    // ejecutable la usa, y si no se va a Documentos. Se replica aqui en vez de
    // llamar a su resolutor porque los metodos no conservan el nombre ofuscado
    // y habria que adivinar cual de los varios "static string()" de esa clase
    // es el correcto.
    //
    // Antes esto estaba fijo a PlayerData, asi que en una instalacion normal el
    // mod no encontraba ni los fondos ni los sonidos.
    public static class RutasJuego
    {
        private static string carpetaDatos;

        public static string CarpetaDatos
        {
            get
            {
                if (carpetaDatos != null)
                {
                    return carpetaDatos;
                }
                try
                {
                    string raizJuego = Directory.GetParent(
                        MelonEnvironment.MelonLoaderDirectory).FullName;
                    string portable = Path.Combine(raizJuego, "PlayerData");
                    if (Directory.Exists(portable))
                    {
                        carpetaDatos = portable;
                        MelonLogger.Msg("[Rutas] instalacion portable: " + carpetaDatos);
                        return carpetaDatos;
                    }

                    string documentos = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "Clone Hero");
                    if (Directory.Exists(documentos))
                    {
                        carpetaDatos = documentos;
                        MelonLogger.Msg("[Rutas] instalacion normal: " + carpetaDatos);
                        return carpetaDatos;
                    }

                    // Ninguna existe todavia: se usa la de Documentos, que es la
                    // que crea el juego en una instalacion normal.
                    carpetaDatos = documentos;
                    MelonLogger.Warning("[Rutas] no se encontro carpeta de datos; se usara "
                                        + carpetaDatos);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error("[Rutas] " + ex);
                    carpetaDatos = "";
                }
                return carpetaDatos;
            }
        }

        // Subcarpeta de Custom, creandola si no existe.
        public static string CarpetaCustom(string nombre)
        {
            string c = Path.Combine(CarpetaDatos, "Custom", nombre);
            try
            {
                Directory.CreateDirectory(c);
            }
            catch (Exception)
            {
            }
            return c;
        }

        public static string RutaSettings()
        {
            return Path.Combine(CarpetaDatos, "settings.ini");
        }
    }
}
