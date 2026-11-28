using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MelonLoader;
using MelonLoader.Utils;

namespace CloneHeroMod
{
    // Una copia del juego y donde guarda sus cosas. La usa el importador para
    // la copia que corre y para la que elige el jugador.
    //
    // Donde guarda cada cosa (Helper.m0538 y m0539 del juego, iguales en la
    // 1.0 y en la 1.1):
    //
    //                      portable                instalada (.fullInstall)
    //   perfiles, ajustes  <juego>\PlayerData      Documents\Clone Hero
    //   puntuaciones, fav. <juego>\GameData        AppData\LocalLow\srylain Inc_\Clone Hero
    //
    // Portable es lo que hay salvo que exista <juego>\.fullInstall (lo deja el
    // instalador). Lo decide GameInit al arrancar.
    //
    // TODO EL TEXTO EN INGLES: los errores se ven en pantalla.
    public sealed class CopiaJuego
    {
        // Solo las que sabemos que se pueden mezclar: la mudanza de la 6142 a
        // la 6161 se hizo copiando estos mismos archivos, y el juego los leyo
        // sin quejarse. Las 1.0 y las 0.x guardan las puntuaciones en otro
        // formato.
        public static readonly string[] VersionesProbadas = { "1.1.0.6142", "1.1.0.6161" };

        public string Raiz;          // la carpeta del .exe
        public string Version;
        public bool Portable;
        public string Datos;         // PlayerData o Documents\Clone Hero
        public string DatosJuego;    // GameData o LocalLow

        public string Descripcion
        {
            get
            {
                return "Clone Hero " + (Version ?? "?")
                    + (Portable ? " (portable)" : " (installed)");
            }
        }

        // La que corre. Solo desde el hilo principal: persistentDataPath es de
        // Unity.
        public static CopiaJuego Actual()
        {
            CopiaJuego c = new CopiaJuego();
            c.Raiz = Normalizar(MelonEnvironment.GameRootDirectory);
            c.Portable = !File.Exists(Path.Combine(c.Raiz, ".fullInstall"));
            c.Datos = Normalizar(RutasJuego.CarpetaDatos);
            c.DatosJuego = Normalizar(c.Portable
                ? Path.Combine(c.Raiz, "GameData")
                : UnityEngine.Application.persistentDataPath);
            try { c.Version = UnityEngine.Application.version; } catch (Exception) { }
            return c;
        }

        // La que elige el jugador. null y un mensaje si no vale. Tambien desde
        // el hilo principal.
        public static CopiaJuego Abrir(string elegida, CopiaJuego actual, out string error)
        {
            error = null;
            string raiz = BuscarRaiz(elegida, out string carpetaDatos);
            if (raiz == null)
            {
                error = "That folder isn't a copy of Clone Hero. Choose the folder that has"
                      + " Clone Hero.exe in it.";
                return null;
            }

            CopiaJuego c = new CopiaJuego();
            c.Raiz = raiz;
            c.Version = LeerVersion(carpetaDatos);
            if (c.Version == null)
            {
                error = "Couldn't tell which version of Clone Hero that copy is.";
                return null;
            }
            if (!Probada(c.Version))
            {
                error = "That copy is Clone Hero " + c.Version + ". Importing only works from "
                      + string.Join(" and ", VersionesProbadas) + ".";
                return null;
            }
            if (Igual(c.Raiz, actual.Raiz))
            {
                error = "That's the copy you're playing right now. Choose the other one.";
                return null;
            }

            c.Portable = !File.Exists(Path.Combine(raiz, ".fullInstall"));
            c.Datos = Normalizar(c.Portable
                ? Path.Combine(raiz, "PlayerData")
                : RutasJuego.CarpetaDocumentos());
            c.DatosJuego = Normalizar(c.Portable
                ? Path.Combine(raiz, "GameData")
                : UnityEngine.Application.persistentDataPath);

            if (Igual(c.Datos, actual.Datos) && Igual(c.DatosJuego, actual.DatosJuego))
            {
                error = "Both copies keep their data in the same folder, so there's nothing"
                      + " to import.";
                return null;
            }
            if (!Directory.Exists(c.Datos) && !Directory.Exists(c.DatosJuego))
            {
                error = "That copy doesn't have any player data yet.";
                return null;
            }
            MelonLogger.Msg("[Importar] copia elegida: " + c.Raiz + "  " + c.Descripcion
                + "  datos=" + c.Datos + "  datosJuego=" + c.DatosJuego);
            return c;
        }

        public static bool Probada(string version)
        {
            return Array.IndexOf(VersionesProbadas, version) >= 0;
        }

        // Se acepta la carpeta del juego o una de sus subcarpetas (PlayerData,
        // Clone Hero_Data...): mucha gente elige la de dentro.
        private static string BuscarRaiz(string elegida, out string carpetaDatos)
        {
            carpetaDatos = null;
            if (string.IsNullOrWhiteSpace(elegida))
            {
                return null;
            }
            string dir;
            try { dir = Normalizar(Path.GetFullPath(elegida)); }
            catch (Exception) { return null; }

            for (int nivel = 0; nivel < 3 && !string.IsNullOrEmpty(dir); nivel++)
            {
                string datos = DatosDeExe(dir);
                if (datos != null)
                {
                    carpetaDatos = datos;
                    return dir;
                }
                try
                {
                    DirectoryInfo padre = Directory.GetParent(dir);
                    dir = padre == null ? null : Normalizar(padre.FullName);
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return null;
        }

        // Un juego de Unity es un <nombre>.exe junto a <nombre>_Data. El de
        // Clone Hero se llama asi, pero no se da por hecho.
        private static string DatosDeExe(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    return null;
                }
                string[] exes = Directory.GetFiles(dir, "*.exe");
                for (int i = 0; i < exes.Length; i++)
                {
                    string nombre = Path.GetFileNameWithoutExtension(exes[i]);
                    string datos = Path.Combine(dir, nombre + "_Data");
                    if (File.Exists(Path.Combine(datos, "globalgamemanagers")))
                    {
                        return datos;
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        // La version va en texto plano dentro de globalgamemanagers
        // ("1.1.0.6161"), y es la unica cadena con esa forma.
        private static readonly Regex FormaVersion =
            new Regex(@"(?<![0-9.])[0-9]+\.[0-9]+\.[0-9]+\.[0-9]{3,6}(?![0-9.])");

        private static string LeerVersion(string carpetaDatos)
        {
            try
            {
                byte[] b = File.ReadAllBytes(Path.Combine(carpetaDatos, "globalgamemanagers"));
                Match m = FormaVersion.Match(Encoding.Latin1.GetString(b));
                return m.Success ? m.Value : null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Importar] leer version: " + ex.Message);
                return null;
            }
        }

        public static string Normalizar(string ruta)
        {
            if (string.IsNullOrEmpty(ruta))
            {
                return "";
            }
            try { ruta = Path.GetFullPath(ruta); } catch (Exception) { }
            string t = ruta.TrimEnd('\\', '/');
            // "C:" a secas es la carpeta actual de C:, no la raiz.
            return t.Length == 2 && t[1] == ':' ? t + "\\" : t;
        }

        public static bool Igual(string a, string b)
        {
            return string.Equals(Normalizar(a), Normalizar(b), StringComparison.OrdinalIgnoreCase);
        }
    }
}
