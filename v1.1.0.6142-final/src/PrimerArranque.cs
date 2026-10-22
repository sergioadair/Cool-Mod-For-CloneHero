using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace CloneHeroMod
{
    // El paquete "todo en uno" trae los archivos del mod en UserData\CoolMod,
    // con la misma estructura que Custom (por ahora, Sounds\yourock.opus). La
    // primera vez que arranca, el mod los copia a la carpeta Custom del juego,
    // que ya sabe localizar sola (portable, instalado, Documentos en OneDrive).
    //
    // Solo una vez: despues deja una marca, para no volver a poner algo que el
    // jugador haya quitado a proposito. Lo que ya exista no se toca.
    public static class PrimerArranque
    {
        private const string Carpeta = "CoolMod";
        private const string Marca = "copiado.txt";

        public static void Copiar()
        {
            try
            {
                string origen = Path.Combine(MelonEnvironment.UserDataDirectory, Carpeta);
                if (!Directory.Exists(origen) || File.Exists(Path.Combine(origen, Marca)))
                {
                    return;
                }
                if (string.IsNullOrEmpty(RutasJuego.CarpetaDatos))
                {
                    return;      // sin carpeta del juego no se marca: se reintenta al arrancar otra vez
                }
                int copiados = 0;
                foreach (string dir in Directory.GetDirectories(origen))
                {
                    string destino = RutasJuego.CarpetaCustom(Path.GetFileName(dir));
                    if (string.IsNullOrEmpty(destino))
                    {
                        return;
                    }
                    foreach (string archivo in Directory.GetFiles(dir))
                    {
                        string final = Path.Combine(destino, Path.GetFileName(archivo));
                        if (!File.Exists(final))
                        {
                            File.Copy(archivo, final);
                            copiados++;
                        }
                    }
                }
                File.WriteAllText(Path.Combine(origen, Marca),
                    "Files copied to " + Path.Combine(RutasJuego.CarpetaDatos, "Custom") + Environment.NewLine);
                MelonLogger.Msg("[Instalacion] archivos del paquete copiados a Custom: " + copiados.ToString());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Instalacion] no se pudieron copiar los archivos del paquete: " + ex.Message);
            }
        }
    }
}
