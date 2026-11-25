using System;
using System.Diagnostics;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace CloneHeroMod
{
    // Deja la instalacion lista para GHLarp sin que el jugador toque nada.
    //
    // GHLarp tiene que llamarse version.dll, que es justo el nombre con el que
    // viene MelonLoader: si se copia encima, MelonLoader desaparece y con el
    // el mod. Ademas los dos enganchan las mismas funciones de escena y el
    // juego se cae (ver CoolModCompat).
    //
    // Por eso, en cada arranque:
    //   1. El plugin CoolModCompat, que va DENTRO de este .dll como recurso,
    //      se copia a Plugins si falta o si ha cambiado. Asi basta con el boton
    //      Update Cool Mod para tenerlo al dia. Sin GHLarp no hace nada.
    //   2. Si MelonLoader esta como version.dll y no hay winmm.dll, se
    //      renombra a winmm.dll: es otro nombre que MelonLoader admite y que el
    //      juego carga igual. Desde ese momento GHLarp se instala solo copiando
    //      sus archivos.
    //
    // Los dos cambios surten efecto en el siguiente arranque. Windows deja
    // renombrar un .dll que esta en uso, que es el caso de los dos.
    public static class PreparacionGHLarp
    {
        private const string Recurso = "CoolModCompat.dll";

        public static void Preparar()
        {
            InstalarPlugin();
            RenombrarCargador();
        }

        private static void InstalarPlugin()
        {
            try
            {
                byte[] datos;
                using (Stream s = typeof(PreparacionGHLarp).Assembly.GetManifestResourceStream(Recurso))
                {
                    if (s == null)
                    {
                        MelonLogger.Warning("[GHLarp] el plugin de compatibilidad no va dentro del mod");
                        return;
                    }
                    using (MemoryStream m = new MemoryStream())
                    {
                        s.CopyTo(m);
                        datos = m.ToArray();
                    }
                }
                string carpeta = MelonEnvironment.PluginsDirectory;
                Directory.CreateDirectory(carpeta);
                string destino = Path.Combine(carpeta, Recurso);
                if (File.Exists(destino) && Iguales(File.ReadAllBytes(destino), datos))
                {
                    return;
                }
                bool habia = File.Exists(destino);
                if (habia)
                {
                    // Puede estar cargado: se aparta en vez de sobrescribirlo.
                    string apartado = destino + "." + DateTime.Now.Ticks.ToString() + ".old";
                    File.Move(destino, apartado);
                }
                File.WriteAllBytes(destino, datos);
                MelonLogger.Msg("[GHLarp] plugin de compatibilidad " + (habia ? "actualizado" : "instalado")
                                + " en Plugins (activo desde el proximo arranque)");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[GHLarp] no se pudo instalar el plugin: " + ex.Message);
            }
        }

        private static void RenombrarCargador()
        {
            try
            {
                string raiz = MelonEnvironment.GameRootDirectory;
                string version = Path.Combine(raiz, "version.dll");
                string winmm = Path.Combine(raiz, "winmm.dll");
                if (!File.Exists(version) || File.Exists(winmm) || !EsMelonLoader(version))
                {
                    return;
                }
                File.Move(version, winmm);
                MelonLogger.Msg("[GHLarp] MelonLoader ahora se carga como winmm.dll: GHLarp ya puede"
                                + " usar version.dll");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[GHLarp] no se pudo renombrar MelonLoader: " + ex.Message);
            }
        }

        private static bool EsMelonLoader(string ruta)
        {
            string producto = FileVersionInfo.GetVersionInfo(ruta).ProductName;
            return producto != null && producto.StartsWith("MelonLoader", StringComparison.OrdinalIgnoreCase);
        }

        // Los .old que deja InstalarPlugin al cambiar de version.
        public static void LimpiarRestos()
        {
            try
            {
                string[] restos = Directory.GetFiles(MelonEnvironment.PluginsDirectory, Recurso + ".*.old");
                for (int i = 0; i < restos.Length; i++)
                {
                    try { File.Delete(restos[i]); } catch (Exception) { }
                }
            }
            catch (Exception)
            {
            }
        }

        private static bool Iguales(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
