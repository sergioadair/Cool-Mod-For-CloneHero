using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;

namespace CloneHeroMod
{
    // Ajustes del mod dentro del settings.ini del juego, en su propia seccion
    // [mods].
    //
    // Se leen y escriben directamente en vez de registrarlos en el sistema de
    // ajustes del juego, porque en esta version ese sistema esta ofuscado y es
    // ademas declarativo (settings_schema.json). Comprobado que el juego
    // CONSERVA las claves que no conoce: se anadio una de prueba, se arranco y
    // se cerro limpiamente, y seguia ahi.
    //
    // Aun asi hay una copia de respaldo en la carpeta de MelonLoader: si alguna
    // version futura del juego decidiera limpiar lo que no reconoce, el valor
    // del usuario no se pierde y se vuelve a escribir solo.
    public static class Ajustes
    {
        public const string Seccion = "mods";
        public const string ClaveReferenceNps = "difficulty_reference_nps";
        public const string ClaveSlideshow = "menu_bg_slideshow";
        public const string ClaveSlideshowSegundos = "menu_bg_slideshow_seconds";
        public const string ClaveSfxFin = "finished_song_sfx";
        public const string ClaveMostrarDificultad = "show_difficulty";
        public const string ClaveVolumenSfx = "finished_song_sfx_volume";

        public const float SlideshowPorDefecto = 0f;         // 0 = apagado
        public const float SegundosPorDefecto = 900f;        // 15 minutos
        public const float SegundosMin = 5f;
        public const float SegundosMax = 86400f;

        private static float sfxFin = 1f;
        private static float mostrarDificultad = 1f;
        private static float volumenSfx = 1f;
        private static float slideshow;
        private static float slideshowSegundos = SegundosPorDefecto;

        // Volumen del sonido de fin de cancion, 0..1. Ajustable a mano en
        // settings.ini porque el escalador del juego no basta por si solo.
        public static float VolumenSfx
        {
            get
            {
                if (!cargado) { Cargar(); }
                return volumenSfx;
            }
        }

        public static bool SfxFinActivo
        {
            get
            {
                if (!cargado) { Cargar(); }
                return sfxFin >= 0.5f;
            }
        }

        public static void GuardarSfxFin(bool activo)
        {
            try
            {
                sfxFin = activo ? 1f : 0f;
                EscribirClave(RutaSettings(), Seccion, ClaveSfxFin, sfxFin);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Ajustes] guardar sfx: " + ex.Message);
            }
        }

        // Muestra u oculta la etiqueta "Difficulty: X" del panel de informacion
        // de la cancion. Se conmuta desde Settings > Video.
        public static bool MostrarDificultad
        {
            get
            {
                if (!cargado) { Cargar(); }
                return mostrarDificultad >= 0.5f;
            }
        }

        public static void GuardarMostrarDificultad(bool activo)
        {
            try
            {
                mostrarDificultad = activo ? 1f : 0f;
                EscribirClave(RutaSettings(), Seccion, ClaveMostrarDificultad, mostrarDificultad);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Ajustes] guardar show difficulty: " + ex.Message);
            }
        }

        public static bool SlideshowActivo
        {
            get
            {
                if (!cargado) { Cargar(); }
                return slideshow >= 0.5f;
            }
        }

        public static float SlideshowSegundos
        {
            get
            {
                if (!cargado) { Cargar(); }
                return slideshowSegundos;
            }
        }

        public const float ReferenceNpsPorDefecto = 14f;
        public const float ReferenceNpsMin = 1f;
        public const float ReferenceNpsMax = 100f;

        private static float referenceNps = ReferenceNpsPorDefecto;
        private static bool cargado;

        public static float ReferenceNps
        {
            get
            {
                if (!cargado)
                {
                    Cargar();
                }
                return referenceNps;
            }
        }

        private static string RutaSettings()
        {
            // La carpeta de datos cambia segun como este instalado el juego
            // (PlayerData si es portable, Documents\Clone Hero si no).
            return RutasJuego.RutaSettings();
        }

        private static string RutaRespaldo()
        {
            return Path.Combine(MelonEnvironment.MelonLoaderDirectory, "mod-settings.txt");
        }

        public static void Cargar()
        {
            cargado = true;
            referenceNps = ReferenceNpsPorDefecto;
            try
            {
                string ruta = RutaSettings();
                float? leido = LeerClave(ruta, Seccion, ClaveReferenceNps);

                if (leido == null)
                {
                    // No esta en settings.ini: se recupera del respaldo si lo
                    // hay, y se vuelve a dejar escrita para que sea visible.
                    float valor = LeerRespaldo() ?? ReferenceNpsPorDefecto;
                    referenceNps = Limitar(valor);
                    EscribirClave(ruta, Seccion, ClaveReferenceNps, referenceNps);
                    MelonLogger.Msg("[Ajustes] " + ClaveReferenceNps + " no estaba; escrito con "
                                    + Texto(referenceNps));
                }
                else
                {
                    referenceNps = Limitar(leido.Value);
                    if (Math.Abs(referenceNps - leido.Value) > 0.0001f)
                    {
                        MelonLogger.Warning("[Ajustes] " + ClaveReferenceNps + " fuera de rango ("
                            + Texto(leido.Value) + "); se usa " + Texto(referenceNps));
                    }
                }
                slideshow = LeerOEscribir(ruta, ClaveSlideshow, SlideshowPorDefecto, 0f, 1f);
                slideshowSegundos = LeerOEscribir(ruta, ClaveSlideshowSegundos,
                    SegundosPorDefecto, SegundosMin, SegundosMax);

                sfxFin = LeerOEscribir(ruta, ClaveSfxFin, 1f, 0f, 1f);
                volumenSfx = LeerOEscribir(ruta, ClaveVolumenSfx, 1f, 0f, 1f);
                mostrarDificultad = LeerOEscribir(ruta, ClaveMostrarDificultad, 1f, 0f, 1f);

                GuardarRespaldo(referenceNps);
                MelonLogger.Msg("[Ajustes] ReferenceNps=" + Texto(referenceNps)
                    + "  slideshow=" + (slideshow >= 0.5f ? "si" : "no")
                    + "  segundos=" + Texto(slideshowSegundos)
                    + "  sfxFin=" + (sfxFin >= 0.5f ? "si" : "no")
                    + "  mostrarDificultad=" + (mostrarDificultad >= 0.5f ? "si" : "no"));
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Ajustes] " + ex);
            }
        }

        // Lee una clave de [mods]; si no esta, la escribe con el valor por
        // defecto para que el usuario la encuentre. Devuelve el valor acotado.
        private static float LeerOEscribir(string ruta, string clave, float porDefecto,
                                           float min, float max)
        {
            float? leido = LeerClave(ruta, Seccion, clave);
            float v = leido ?? porDefecto;
            if (v < min) { v = min; }
            if (v > max) { v = max; }
            if (leido == null || Math.Abs(v - leido.Value) > 0.0001f)
            {
                EscribirClave(ruta, Seccion, clave, v);
            }
            return v;
        }

        // El fondo elegido se guarda APARTE, en [mods]. El ajuste del juego
        // (menu_background) tiene maximo 13 en su settings_schema.json, asi que
        // al arrancar recorta cualquier valor nuestro y vuelve a uno de serie:
        // por eso no se conservaba entre sesiones.
        public const string ClaveFondo = "menu_background_custom";

        public static int LeerFondoGuardado()
        {
            try
            {
                float? v = LeerClave(RutaSettings(), Seccion, ClaveFondo);
                return v == null ? -1 : (int)v.Value;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public static void GuardarFondo(int valor)
        {
            try
            {
                EscribirClave(RutaSettings(), Seccion, ClaveFondo, valor);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Ajustes] guardar fondo: " + ex.Message);
            }
        }

        // Conmuta el slideshow y lo persiste en settings.ini.
        public static void GuardarSlideshow(bool activo)
        {
            try
            {
                slideshow = activo ? 1f : 0f;
                EscribirClave(RutaSettings(), Seccion, ClaveSlideshow, slideshow);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Ajustes] guardar slideshow: " + ex.Message);
            }
        }

        private static float Limitar(float v)
        {
            if (v < ReferenceNpsMin) { return ReferenceNpsMin; }
            if (v > ReferenceNpsMax) { return ReferenceNpsMax; }
            return v;
        }

        private static string Texto(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // Acepta punto y coma como separador decimal: el juego escribe algunos
        // valores con la cultura local (hud_position_x = 0,08).
        private static bool Parsear(string s, out float v)
        {
            v = 0f;
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            s = s.Trim().Replace(',', '.');
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        // ------------------------------------------------------------- INI --
        private static float? LeerClave(string ruta, string seccion, string clave)
        {
            if (!File.Exists(ruta))
            {
                return null;
            }
            string[] lineas = File.ReadAllLines(ruta);
            string actual = null;
            for (int i = 0; i < lineas.Length; i++)
            {
                string l = lineas[i].Trim();
                if (l.Length == 0 || l[0] == ';' || l[0] == '#')
                {
                    continue;
                }
                if (l[0] == '[' && l[l.Length - 1] == ']')
                {
                    actual = l.Substring(1, l.Length - 2).Trim();
                    continue;
                }
                if (!string.Equals(actual, seccion, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                int eq = l.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                if (!string.Equals(l.Substring(0, eq).Trim(), clave, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (Parsear(l.Substring(eq + 1), out float v))
                {
                    return v;
                }
                return null;
            }
            return null;
        }

        // Inserta o actualiza la clave conservando el resto del archivo intacto.
        private static void EscribirClave(string ruta, string seccion, string clave, float valor)
        {
            List<string> lineas = File.Exists(ruta)
                ? new List<string>(File.ReadAllLines(ruta))
                : new List<string>();

            string nueva = clave + " = " + Texto(valor);
            int inicioSeccion = -1;
            int finSeccion = -1;
            string actual = null;

            for (int i = 0; i < lineas.Count; i++)
            {
                string l = lineas[i].Trim();
                if (l.Length >= 2 && l[0] == '[' && l[l.Length - 1] == ']')
                {
                    string nombre = l.Substring(1, l.Length - 2).Trim();
                    if (string.Equals(nombre, seccion, StringComparison.OrdinalIgnoreCase))
                    {
                        inicioSeccion = i;
                    }
                    else if (inicioSeccion >= 0 && finSeccion < 0)
                    {
                        finSeccion = i;
                    }
                    actual = nombre;
                    continue;
                }
                if (inicioSeccion >= 0 && finSeccion < 0
                    && string.Equals(actual, seccion, StringComparison.OrdinalIgnoreCase))
                {
                    int eq = l.IndexOf('=');
                    if (eq > 0 && string.Equals(l.Substring(0, eq).Trim(), clave,
                                                StringComparison.OrdinalIgnoreCase))
                    {
                        lineas[i] = nueva;          // ya existia: se actualiza
                        Volcar(ruta, lineas);
                        return;
                    }
                }
            }

            if (inicioSeccion < 0)
            {
                if (lineas.Count > 0 && lineas[lineas.Count - 1].Trim().Length != 0)
                {
                    lineas.Add("");
                }
                lineas.Add("[" + seccion + "]");
                lineas.Add(nueva);
            }
            else
            {
                int donde = (finSeccion >= 0) ? finSeccion : lineas.Count;
                lineas.Insert(donde, nueva);
            }
            Volcar(ruta, lineas);
        }

        private static void Volcar(string ruta, List<string> lineas)
        {
            File.WriteAllText(ruta, string.Join(Environment.NewLine, lineas)
                                    + Environment.NewLine, new UTF8Encoding(false));
        }

        // ---------------------------------------------------------- respaldo -
        private static float? LeerRespaldo()
        {
            try
            {
                string r = RutaRespaldo();
                if (!File.Exists(r))
                {
                    return null;
                }
                string[] lineas = File.ReadAllLines(r);
                for (int i = 0; i < lineas.Length; i++)
                {
                    int eq = lineas[i].IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    if (string.Equals(lineas[i].Substring(0, eq).Trim(), ClaveReferenceNps,
                                      StringComparison.OrdinalIgnoreCase)
                        && Parsear(lineas[i].Substring(eq + 1), out float v))
                    {
                        return v;
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private static void GuardarRespaldo(float valor)
        {
            try
            {
                File.WriteAllText(RutaRespaldo(),
                    ClaveReferenceNps + " = " + Texto(valor) + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
        }
    }
}
