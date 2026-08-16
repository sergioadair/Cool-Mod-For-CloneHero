using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MelonLoader;
using MelonLoader.Utils;

namespace CloneHeroMod
{
    // Carpeta de datos del jugador. No esta en el mismo sitio segun como se
    // instale el juego, y ademas Documentos puede estar redirigido a OneDrive:
    //
    //   portable  ->  <carpeta del juego>\PlayerData
    //   normal    ->  Documents\Clone Hero
    //   normal    ->  OneDrive\Documents\Clone Hero   (o \Documentos\, en es)
    //
    // Dos intentos, en este orden:
    //
    //   1. Preguntarle al juego. Su clase de rutas tiene varios "static
    //      string()" y los metodos NO conservan el nombre ofuscado, asi que se
    //      llaman todos y se valida el resultado: el bueno es el que apunta a
    //      una carpeta que de verdad contiene settings.ini, Songs o Custom.
    //   2. Si eso falla, una lista de candidatos con la misma validacion.
    //
    // Antes esto estaba fijo a PlayerData y luego a GetFolderPath(MyDocuments),
    // que con Documentos redirigido a OneDrive devuelve la ruta equivocada: el
    // mod no encontraba fondos ni sonidos y no avisaba de nada.
    public static class RutasJuego
    {
        private const string TipoRutas = "ʵˁʼʾʽʶʵʹʴʸʻ";

        private static string carpetaDatos;

        public static string CarpetaDatos
        {
            get
            {
                if (carpetaDatos == null)
                {
                    carpetaDatos = Resolver();
                }
                return carpetaDatos;
            }
        }

        private static string Resolver()
        {
            List<string> probados = new List<string>();

            // 0. Salida manual: un archivo de texto con la ruta, por si la
            // deteccion falla en alguna configuracion que no previmos.
            try
            {
                string forzada = Path.Combine(MelonEnvironment.MelonLoaderDirectory,
                                              "clone-hero-data-folder.txt");
                if (File.Exists(forzada))
                {
                    string r = File.ReadAllText(forzada).Trim();
                    probados.Add(r);
                    if (EsCarpetaDelJuego(r))
                    {
                        MelonLogger.Msg("[Rutas] forzada por archivo: " + r);
                        return r;
                    }
                    MelonLogger.Warning("[Rutas] la ruta de clone-hero-data-folder.txt "
                        + "no parece valida: " + r);
                }
            }
            catch (Exception)
            {
            }

            // 1. Portable: manda si existe, igual que hace el juego.
            try
            {
                string raiz = Directory.GetParent(MelonEnvironment.MelonLoaderDirectory).FullName;
                string portable = Path.Combine(raiz, "PlayerData");
                probados.Add(portable);
                if (EsCarpetaDelJuego(portable))
                {
                    MelonLogger.Msg("[Rutas] portable: " + portable);
                    return portable;
                }
            }
            catch (Exception)
            {
            }

            // 2. Preguntarle al juego.
            string delJuego = PreguntarAlJuego(probados);
            if (delJuego != null)
            {
                MelonLogger.Msg("[Rutas] segun el juego: " + delJuego);
                return delJuego;
            }

            // 3. Candidatos conocidos, incluidas las variantes de OneDrive.
            foreach (string c in Candidatos())
            {
                probados.Add(c);
                if (EsCarpetaDelJuego(c))
                {
                    MelonLogger.Msg("[Rutas] encontrada: " + c);
                    return c;
                }
            }

            MelonLogger.Error("[Rutas] NO se encontro la carpeta de datos. Probados:");
            for (int i = 0; i < probados.Count; i++)
            {
                MelonLogger.Error("[Rutas]   " + probados[i]);
            }
            MelonLogger.Error("[Rutas] los fondos y sonidos propios no funcionaran. "
                + "Se puede indicar la ruta a mano creando el archivo "
                + "MelonLoader\\clone-hero-data-folder.txt con la carpeta dentro.");
            return "";
        }

        // Una carpeta de datos de Clone Hero tiene al menos una de estas cosas.
        private static bool EsCarpetaDelJuego(string ruta)
        {
            try
            {
                if (string.IsNullOrEmpty(ruta) || !Directory.Exists(ruta))
                {
                    return false;
                }
                return File.Exists(Path.Combine(ruta, "settings.ini"))
                    || Directory.Exists(Path.Combine(ruta, "Songs"))
                    || Directory.Exists(Path.Combine(ruta, "Custom"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Se invocan todos los "public static string()" de la clase de rutas y
        // se valida lo que devuelven. Son getters de rutas, sin efectos.
        private static string PreguntarAlJuego(List<string> probados)
        {
            try
            {
                Type t = Ofuscado.Tipo(TipoRutas);
                if (t == null)
                {
                    return null;
                }
                MethodInfo[] ms = t.GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].ReturnType != typeof(string)
                        || ms[i].GetParameters().Length != 0
                        || ms[i].IsSpecialName)
                    {
                        continue;
                    }
                    string r;
                    try { r = ms[i].Invoke(null, null) as string; }
                    catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(r))
                    {
                        continue;
                    }
                    probados.Add(r);
                    if (EsCarpetaDelJuego(r))
                    {
                        return r;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Rutas] preguntar al juego: " + ex.Message);
            }
            return null;
        }

        private static IEnumerable<string> Candidatos()
        {
            string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // El que da .NET. Con Documentos redirigido a OneDrive no siempre
            // devuelve la ruta buena, por eso no se usa en solitario.
            string docs = null;
            try { docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
            catch (Exception) { }
            if (!string.IsNullOrEmpty(docs))
            {
                yield return Path.Combine(docs, "Clone Hero");
            }

            if (string.IsNullOrEmpty(perfil))
            {
                yield break;
            }

            // Variantes con y sin OneDrive, en ingles y en espanol. OneDrive
            // renombra la carpeta segun el idioma de Windows.
            string[] bases =
            {
                Path.Combine(perfil, "Documents"),
                Path.Combine(perfil, "Documentos"),
                Path.Combine(perfil, "OneDrive", "Documents"),
                Path.Combine(perfil, "OneDrive", "Documentos")
            };
            for (int i = 0; i < bases.Length; i++)
            {
                yield return Path.Combine(bases[i], "Clone Hero");
            }

            // Cualquier OneDrive con otro nombre (OneDrive - Empresa, etc).
            string[] carpetas = null;
            try { carpetas = Directory.GetDirectories(perfil, "OneDrive*"); }
            catch (Exception) { }
            if (carpetas == null)
            {
                yield break;
            }
            for (int i = 0; i < carpetas.Length; i++)
            {
                yield return Path.Combine(carpetas[i], "Documents", "Clone Hero");
                yield return Path.Combine(carpetas[i], "Documentos", "Clone Hero");
            }
        }

        // Subcarpeta de Custom, creandola si no existe.
        public static string CarpetaCustom(string nombre)
        {
            string raiz = CarpetaDatos;
            if (string.IsNullOrEmpty(raiz))
            {
                return "";
            }
            string c = Path.Combine(raiz, "Custom", nombre);
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
            string raiz = CarpetaDatos;
            return string.IsNullOrEmpty(raiz) ? "" : Path.Combine(raiz, "settings.ini");
        }
    }
}
