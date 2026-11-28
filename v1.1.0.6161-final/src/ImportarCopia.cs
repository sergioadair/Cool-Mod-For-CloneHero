using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace CloneHeroMod
{
    // "Import From Another Copy" (Settings > General): trae perfiles, ajustes,
    // puntuaciones, favoritos, la carpeta Custom y los volumenes del mod desde
    // otra copia del juego.
    //
    // COMO SE USA, TODO DENTRO DEL JUEGO:
    //   - La fila abre un panel propio. "Browse..." abre la misma ventana de
    //     Windows que usa el juego para anadir carpetas de canciones
    //     (NativeFilePicker.dll, ShowOpenFolderDialog).
    //   - Se valida que sea una copia de Clone Hero de una version probada
    //     (CopiaJuego) y se marcan las casillas de lo que tiene.
    //   - Import: copia en segundo plano, con progreso, y reinicia el juego.
    //
    // POR QUE SE REINICIA, Y MATANDO EL PROCESO: el juego tiene perfiles,
    // ajustes y puntuaciones en memoria y los vuelve a escribir al salir. Si
    // se cerrara normal, pisaria lo recien importado. Se lanza la copia nueva
    // a los dos segundos (cmd + explorer, para que no herede el entorno de
    // MelonLoader) y este proceso se termina sin pasar por el guardado.
    //
    // REEMPLAZAR vs MEZCLAR:
    //   - Perfiles, ajustes, puntuaciones, favoritos y volumenes se
    //     REEMPLAZAN. Lo de aqui va antes a PlayerData\Import Backups\<fecha>.
    //     Primero se copia todo a temporales junto al destino y solo si eso
    //     sale bien se cambian, para no dejar media importacion.
    //   - Custom y Setlists se MEZCLAN: solo se anade lo que falta, nunca se
    //     sobrescribe nada. Por eso no necesitan respaldo.
    //
    // EL PANEL LEE LOS BOTONES DE REWIRED, los mismos de los menus (BaseMenu.
    // DoInput: 0 verde, 1 rojo, 2 amarillo, 3 azul, 5/14 rasgueo). Mientras
    // esta abierto, OverlayProgreso corta la entrada de los menus del juego.
    //
    // TODO EL TEXTO EN INGLES: se ve en pantalla.
    public static partial class ImportarCopia
    {
        public const string Nombre = "Import From Another Copy";

        // ------------------------------------------------- que se importa
        private enum Base { Datos, DatosJuego, Raiz }

        private sealed class Elemento
        {
            public Base Donde;
            public string Rel;
            public bool Carpeta;      // se mezcla en vez de reemplazarse
            public bool Depende;      // va con el primer archivo de su grupo

            public Elemento(Base donde, string rel, bool carpeta = false, bool depende = false)
            {
                Donde = donde;
                Rel = rel;
                Carpeta = carpeta;
                Depende = depende;
            }
        }

        private sealed class Categoria
        {
            public string Nombre;
            public string Nota;
            public bool Grande;
            public Elemento[] Elementos;
            public bool Disponible;
            public bool Marcada;
            // Lo que falta por copiar en sus carpetas, medido en otro hilo.
            public volatile bool Medida;
            public long BytesNuevos;
        }

        private static Categoria Cat(string nombre, string nota, bool grande, params Elemento[] e)
        {
            Categoria c = new Categoria();
            c.Nombre = nombre;
            c.Nota = nota;
            c.Grande = grande;
            c.Elementos = e;
            return c;
        }

        // Los "Depende" solo se tocan si el primero del grupo esta en la otra
        // copia; y entonces se reemplazan o, si alli no existen, se retiran
        // aqui (al respaldo). Asi no queda un scoresext.bin de esta copia
        // mezclado con el scoredata.bin de la otra.
        private static readonly Categoria[] categorias =
        {
            Cat("Profiles", null, false,
                new Elemento(Base.Datos, "profiles.ini")),
            Cat("Settings", "video, audio, gameplay and song folders", false,
                new Elemento(Base.Datos, "settings.ini"),
                new Elemento(Base.Datos, "settings.ini.bak", depende: true)),
            Cat("Scores and Stats", null, false,
                new Elemento(Base.DatosJuego, "scoredata.bin"),
                new Elemento(Base.DatosJuego, "scoredata_backup.bin", depende: true),
                new Elemento(Base.DatosJuego, "scoresext.bin", depende: true),
                new Elemento(Base.DatosJuego, "scoresext_backup.bin", depende: true),
                new Elemento(Base.DatosJuego, "user.bin", depende: true),
                new Elemento(Base.Datos, "scorestats.json", depende: true)),
            Cat("Favorites and Setlists", null, false,
                new Elemento(Base.DatosJuego, "favorites.bin"),
                new Elemento(Base.DatosJuego, "favorites_backup.bin", depende: true),
                new Elemento(Base.Datos, "Setlists", carpeta: true)),
            Cat("Custom Folder", "textures, highways, backgrounds, colors, sounds."
                + " Can take a while: it's usually big", true,
                new Elemento(Base.Datos, "Custom", carpeta: true)),
            Cat("Cool Mod Song Volumes", null, false,
                new Elemento(Base.Raiz, Path.Combine("UserData", "CoolMod", "song_volume.tsv"))),
        };

        private static string Ruta(CopiaJuego c, Elemento e)
        {
            string b = e.Donde == Base.Datos ? c.Datos
                     : e.Donde == Base.DatosJuego ? c.DatosJuego : c.Raiz;
            return string.IsNullOrEmpty(b) ? null : Path.Combine(b, e.Rel);
        }

        private static string NombreBase(Base b)
        {
            return b == Base.Datos ? "PlayerData" : b == Base.DatosJuego ? "GameData" : "Game Folder";
        }

        // ------------------------------------------------------------ estado
        private enum Estado { Cerrado, Eligiendo, Respaldos, Importando, Listo }

        private static Estado estado;
        private static int cursor;
        private static bool confirmando;
        private static CopiaJuego actual;
        private static CopiaJuego origen;
        private static string mensaje;
        private static bool mensajeError;
        private static int frameAbierto;
        private static int frameCerrado = -100;
        private static bool pedirDialogo;
        private static float reiniciarEn;
        private static int medicion;

        // Del hilo de copia.
        private static volatile string fase;
        private static volatile string archivoActual;
        private static long bytesHechos;
        private static long bytesTotal;
        private static volatile bool terminado;
        private static volatile string errorHilo;
        private static volatile bool sinCambios;
        private static volatile string resumen;
        private static volatile string carpetaRespaldo;
        private static bool restaurando;      // el trabajo en marcha es una restauracion

        private static int Filas { get { return categorias.Length + 4; } }
        private static int FilaImportar { get { return categorias.Length + 1; } }
        private static int FilaRestaurar { get { return categorias.Length + 2; } }
        private static int FilaCancelar { get { return categorias.Length + 3; } }

        // Los menus del juego no atienden mientras el panel esta abierto, ni
        // un par de fotogramas despues de cerrarlo: el rojo que lo cierra
        // seguiria "recien pulsado" y sacaria al jugador de Settings.
        public static bool BloqueaMenus
        {
            get { return estado != Estado.Cerrado || Time.frameCount <= frameCerrado + 2; }
        }

        public static bool Importando
        {
            get { return estado == Estado.Importando || estado == Estado.Listo; }
        }

        // Desde la fila de Settings > General.
        public static void Abrir()
        {
            if (estado != Estado.Cerrado)
            {
                return;
            }
            try
            {
                actual = CopiaJuego.Actual();
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Importar] copia actual: " + ex);
                Aviso.Mostrar(Nombre, "Couldn't find this copy's data folder.");
                return;
            }
            MelonLogger.Msg("[Importar] panel abierto. Esta copia: " + actual.Raiz + "  "
                + actual.Descripcion + "  datos=" + actual.Datos + "  datosJuego=" + actual.DatosJuego);
            estado = Estado.Eligiendo;
            cursor = 0;
            confirmando = false;
            mensajeError = false;
            mensaje = null;
            frameAbierto = Time.frameCount;
            if (origen != null)
            {
                MirarOrigen();
            }
        }

        public static void EscenaCambiada()
        {
            if (estado == Estado.Eligiendo || estado == Estado.Respaldos)
            {
                Cerrar();
            }
        }

        private static void Cerrar()
        {
            estado = Estado.Cerrado;
            frameCerrado = Time.frameCount;
            confirmando = false;
            DevolverCursor();
            Destruir();
        }

        public static void Tick()
        {
            if (estado == Estado.Cerrado)
            {
                return;
            }
            try
            {
                if (raiz == null)
                {
                    Crear();
                    if (raiz == null)
                    {
                        if (falloCreacion)
                        {
                            Cerrar();
                        }
                        return;
                    }
                }
                if (pedirDialogo)
                {
                    pedirDialogo = false;
                    Explorar();
                }
                switch (estado)
                {
                    case Estado.Eligiendo:
                        Entrada();
                        break;
                    case Estado.Respaldos:
                        EntradaRespaldos();
                        break;
                    case Estado.Importando:
                        if (terminado)
                        {
                            Terminar();
                        }
                        break;
                    case Estado.Listo:
                        if (Time.unscaledTime >= reiniciarEn)
                        {
                            Reiniciar();
                        }
                        break;
                }
                Pintar();
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Importar] Tick: " + ex);
                if (estado == Estado.Eligiendo || estado == Estado.Respaldos)
                {
                    Cerrar();
                }
            }
        }

        // ----------------------------------------------------------- entrada
        private static void Entrada()
        {
            if (Raton(false))
            {
                return;
            }
            if (!LeerBotones(out bool arriba, out bool abajo, out bool verde, out bool rojo))
            {
                return;
            }
            if (arriba) Mover(-1);
            else if (abajo) Mover(1);
            else if (verde) Activar();
            else if (rojo) Atras();
        }

        private static void Mover(int d)
        {
            cursor = (cursor + d + Filas) % Filas;
            confirmando = false;
        }

        private static void Atras()
        {
            if (confirmando)
            {
                confirmando = false;
            }
            else
            {
                Cerrar();
            }
        }

        // ------------------------------------------------------------- raton
        // Como "Manage Song Paths": mientras el panel esta abierto se ve el
        // cursor, y al cerrar vuelve a como estaba (el juego lo esconde segun
        // su ajuste). Pasar por encima de una fila la resalta, el clic
        // izquierdo la elige, el derecho es el rojo y la rueda mueve.
        //
        // Las filas no son botones de Unity: van todas en un solo texto. Cada
        // una lleva un <link> (no cambia como se ve) y se apunta en
        // filasMostradas en el orden en que aparece. Bajo el raton se busca la
        // LINEA (cualquier punto de su ancho vale, no solo encima de las
        // letras) y el link que la cruza.
        private static bool cursorAntes;
        private static bool cursorGuardado;
        private static Vector3 ultimoRaton;
        private static List<int> filasLink = new List<int>();
        private static List<int> filasMostradas = new List<int>();

        private static void MostrarCursor()
        {
            if (!cursorGuardado)
            {
                cursorAntes = Cursor.visible;
                cursorGuardado = true;
                ultimoRaton = Input.mousePosition;
            }
            if (!Cursor.visible)
            {
                Cursor.visible = true;
            }
        }

        private static void DevolverCursor()
        {
            if (cursorGuardado)
            {
                cursorGuardado = false;
                try { Cursor.visible = cursorAntes; } catch (Exception) { }
            }
        }

        // true si el raton hizo algo este fotograma.
        private static bool Raton(bool lista)
        {
            try
            {
                MostrarCursor();
                if (Time.frameCount <= frameAbierto + 1)
                {
                    return false;
                }
                Vector3 m = Input.mousePosition;
                bool movio = (m - ultimoRaton).sqrMagnitude > 0.25f;
                ultimoRaton = m;
                float rueda = Input.mouseScrollDelta.y;
                if (rueda > 0.01f || rueda < -0.01f)
                {
                    int d = rueda > 0f ? -1 : 1;
                    if (lista) MoverR(d); else Mover(d);
                    return true;
                }
                if (Input.GetMouseButtonDown(1))
                {
                    if (lista) AtrasR(); else Atras();
                    return true;
                }
                bool clic = Input.GetMouseButtonDown(0);
                if (!movio && !clic)
                {
                    return false;
                }
                int fila = FilaBajoRaton(m);
                if (fila < 0)
                {
                    return clic;
                }
                if (lista)
                {
                    if (fila != cursorR)
                    {
                        cursorR = fila;
                        confirmandoR = false;
                        mensaje = null;
                    }
                    if (clic) ActivarR();
                }
                else
                {
                    if (fila != cursor)
                    {
                        cursor = fila;
                        confirmando = false;
                    }
                    if (clic) Activar();
                }
                return clic;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int FilaBajoRaton(Vector3 m)
        {
            if (texto == null || filasMostradas.Count == 0)
            {
                return -1;
            }
            int linea = Il2CppTMPro.TMP_TextUtilities.FindIntersectingLine(texto, m, null);
            Il2CppTMPro.TMP_TextInfo ti = texto.textInfo;
            if (linea < 0 || ti == null || linea >= ti.lineCount)
            {
                return -1;
            }
            Il2CppTMPro.TMP_LineInfo li = ti.lineInfo[linea];
            int n = Math.Min(ti.linkCount, filasMostradas.Count);
            for (int k = 0; k < n; k++)
            {
                Il2CppTMPro.TMP_LinkInfo l = ti.linkInfo[k];
                int a = l.linkTextfirstCharacterIndex;
                int b = a + l.linkTextLength - 1;
                if (b >= li.firstCharacterIndex && a <= li.lastCharacterIndex)
                {
                    return filasMostradas[k];
                }
            }
            return -1;
        }

        // Envuelve el texto de una fila (o de una linea que va con ella).
        private static string Enlace(int fila, string t)
        {
            filasLink.Add(fila);
            return "<link=\"" + filasLink.Count.ToString() + "\">" + t + "</link>";
        }

        private static bool LeerBotones(out bool arriba, out bool abajo, out bool verde, out bool rojo)
        {
            arriba = abajo = verde = rojo = false;
            // El verde que abrio el panel (o cambio de pantalla) sigue pulsado
            // este fotograma.
            if (Time.frameCount <= frameAbierto + 1)
            {
                return false;
            }
            arriba = Boton(5, true) || Boton(2, true) || Input.GetKeyDown(KeyCode.UpArrow);
            abajo = Boton(14, true) || Boton(3, true) || Input.GetKeyDown(KeyCode.DownArrow);
            verde = Boton(0, false) || Input.GetKeyDown(KeyCode.Return)
                 || Input.GetKeyDown(KeyCode.KeypadEnter);
            rojo = Boton(1, false) || Input.GetKeyDown(KeyCode.Escape)
                || Input.GetKeyDown(KeyCode.Backspace);
            return arriba || abajo || verde || rojo;
        }

        // Cualquier jugador vale: el panel no es de nadie.
        private static bool Boton(int accion, bool repetir)
        {
            try
            {
                Il2CppRewired.ReInput.PlayerHelper jugadores = Il2CppRewired.ReInput.players;
                if (jugadores == null)
                {
                    return false;
                }
                int n = jugadores.playerCount;
                for (int i = 0; i < n; i++)
                {
                    Il2CppRewired.Player p = jugadores.GetPlayer(i);
                    if (p != null && (repetir ? p.GetButtonRepeating(accion) : p.GetButtonDown(accion)))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        private static void Activar()
        {
            mensaje = null;
            mensajeError = false;
            if (cursor == 0)
            {
                // En el siguiente fotograma, para que se pinte el aviso antes
                // de que la ventana de Windows congele el juego.
                pedirDialogo = true;
                mensaje = "Choose the folder in the window that just opened.";
                return;
            }
            if (cursor == FilaCancelar)
            {
                Cerrar();
                return;
            }
            if (cursor == FilaImportar)
            {
                Pedirimportar();
                return;
            }
            if (cursor == FilaRestaurar)
            {
                AbrirRespaldos();
                return;
            }
            Categoria c = categorias[cursor - 1];
            if (origen == null)
            {
                Decir("Choose a copy first.", true);
                return;
            }
            if (!c.Disponible)
            {
                Decir("That copy doesn't have any.", true);
                return;
            }
            c.Marcada = !c.Marcada;
            confirmando = false;
        }

        private static void Decir(string t, bool error)
        {
            mensaje = t;
            mensajeError = error;
        }

        private static void Pedirimportar()
        {
            if (origen == null)
            {
                Decir("Choose a copy first.", true);
                return;
            }
            bool alguna = false;
            for (int i = 0; i < categorias.Length; i++)
            {
                alguna |= categorias[i].Disponible && categorias[i].Marcada;
            }
            if (!alguna)
            {
                Decir("Check at least one thing to import.", true);
                return;
            }
            if (!confirmando)
            {
                confirmando = true;
                return;
            }
            confirmando = false;
            Lanzar();
        }

        // ------------------------------------------------------------ copia
        private static void Explorar()
        {
            string elegida = DialogoCarpeta("Choose the Clone Hero folder to import from",
                                            out bool fallo);
            if (fallo)
            {
                Decir("Couldn't open the folder window.", true);
                return;
            }
            if (elegida == null)
            {
                mensaje = null;      // cancelado: se queda lo que habia
                return;
            }
            MelonLogger.Msg("[Importar] carpeta elegida: " + elegida);
            CopiaJuego c = CopiaJuego.Abrir(elegida, actual, out string error);
            if (c == null)
            {
                MelonLogger.Msg("[Importar] no vale: " + error);
                origen = null;
                for (int i = 0; i < categorias.Length; i++)
                {
                    categorias[i].Disponible = false;
                    categorias[i].Marcada = false;
                }
                Decir(error, true);
                return;
            }
            origen = c;
            MirarOrigen();
            mensaje = null;
        }

        // Que tiene la otra copia, y cuanto ocupa lo que falta de sus carpetas.
        private static void MirarOrigen(bool conservarMarcas = false)
        {
            for (int i = 0; i < categorias.Length; i++)
            {
                Categoria c = categorias[i];
                c.Disponible = false;
                c.Medida = false;
                c.BytesNuevos = 0;
                for (int j = 0; j < c.Elementos.Length; j++)
                {
                    Elemento e = c.Elementos[j];
                    string src = Ruta(origen, e);
                    if (e.Depende || src == null || CopiaJuego.Igual(src, Ruta(actual, e)))
                    {
                        continue;
                    }
                    if (e.Carpeta ? HayArchivos(src) : File.Exists(src))
                    {
                        c.Disponible = true;
                    }
                }
                // Custom se deja sin marcar: puede ser muy grande y que lo
                // copie tiene que ser una decision.
                c.Marcada = conservarMarcas ? c.Marcada && c.Disponible : c.Disponible && !c.Grande;
            }

            int gen = ++medicion;
            CopiaJuego o = origen;
            CopiaJuego a = actual;
            Thread t = new Thread(() => Medir(gen, o, a));
            t.IsBackground = true;
            t.Start();
        }

        private static bool HayArchivos(string carpeta)
        {
            try
            {
                if (!Directory.Exists(carpeta))
                {
                    return false;
                }
                using (IEnumerator<string> e = Directory.EnumerateFiles(carpeta, "*", Opciones).GetEnumerator())
                {
                    return e.MoveNext();
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static readonly EnumerationOptions Opciones = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System
        };

        private static void Medir(int gen, CopiaJuego o, CopiaJuego a)
        {
            for (int i = 0; i < categorias.Length; i++)
            {
                Categoria c = categorias[i];
                long bytes = 0;
                for (int j = 0; j < c.Elementos.Length; j++)
                {
                    Elemento e = c.Elementos[j];
                    if (!e.Carpeta)
                    {
                        continue;
                    }
                    foreach (Pendiente p in Faltantes(Ruta(o, e), Ruta(a, e)))
                    {
                        bytes += p.Bytes;
                        if (gen != medicion)
                        {
                            return;      // ya se eligio otra copia
                        }
                    }
                }
                if (gen != medicion)
                {
                    return;
                }
                c.BytesNuevos = bytes;
                c.Medida = true;
            }
        }

        private struct Pendiente
        {
            public string Origen;
            public string Destino;
            public string Rel;
            public long Bytes;
            public Base Donde;        // para el manifiesto: de que carpeta base
            public string RelBase;    // y su ruta dentro de ella
        }

        // Los archivos de una carpeta de la otra copia que aqui no existen.
        private static IEnumerable<Pendiente> Faltantes(string src, string dst)
        {
            if (src == null || dst == null || !Directory.Exists(src) || CopiaJuego.Igual(src, dst))
            {
                yield break;
            }
            foreach (string f in Directory.EnumerateFiles(src, "*", Opciones))
            {
                if (f.EndsWith(Parcial, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string rel = Path.GetRelativePath(src, f);
                string d = Path.Combine(dst, rel);
                if (File.Exists(d))
                {
                    continue;
                }
                long b = 0;
                try { b = new FileInfo(f).Length; } catch (Exception) { }
                Pendiente p = default(Pendiente);
                p.Origen = f;
                p.Destino = d;
                p.Rel = rel;
                p.Bytes = b;
                yield return p;
            }
        }

        private const string Temporal = ".coolmod-import";
        private const string Parcial = ".coolmod-part";

        private sealed class Reemplazo
        {
            public Elemento E;
            public string Origen;     // null: se retira
            public string Destino;
            public string Temp;
            public string Respaldo;
            public bool Movido;
            public bool Puesto;
        }

        private static void Lanzar()
        {
            List<Categoria> elegidas = new List<Categoria>();
            for (int i = 0; i < categorias.Length; i++)
            {
                if (categorias[i].Disponible && categorias[i].Marcada)
                {
                    elegidas.Add(categorias[i]);
                }
            }
            string nombres = "";
            for (int i = 0; i < elegidas.Count; i++)
            {
                nombres += (i > 0 ? ", " : "") + elegidas[i].Nombre;
            }
            MelonLogger.Msg("[Importar] desde " + origen.Raiz + ": " + nombres);

            carpetaRespaldo = Path.Combine(CarpetaRespaldos(actual),
                DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
            restaurando = false;

            fase = "Checking what to copy...";
            archivoActual = null;
            Interlocked.Exchange(ref bytesHechos, 0);
            Interlocked.Exchange(ref bytesTotal, 0);
            terminado = false;
            errorHilo = null;
            sinCambios = false;
            resumen = null;
            estado = Estado.Importando;

            CopiaJuego o = origen;
            CopiaJuego a = actual;
            string respaldo = carpetaRespaldo;
            Thread t = new Thread(() => Trabajo(elegidas, o, a, respaldo));
            t.IsBackground = true;
            t.Start();
        }

        private static string CarpetaRespaldos(CopiaJuego a)
        {
            string b = string.IsNullOrEmpty(a.Datos)
                ? Path.Combine(MelonEnvironment.UserDataDirectory, "CoolMod")
                : a.Datos;
            return Path.Combine(b, "Import Backups");
        }

        private static void Trabajo(List<Categoria> elegidas, CopiaJuego o, CopiaJuego a, string respaldo)
        {
            List<Reemplazo> reemplazos = new List<Reemplazo>();
            List<string> agregados = new List<string>();      // lineas del manifiesto
            string queSeImporta = "";
            for (int i = 0; i < elegidas.Count; i++)
            {
                queSeImporta += (i > 0 ? ", " : "") + elegidas[i].Nombre;
            }
            try
            {
                // 1. Plan.
                List<Pendiente> nuevos = new List<Pendiente>();
                List<string> deCarpeta = new List<string>();
                long total = 0;
                for (int i = 0; i < elegidas.Count; i++)
                {
                    Categoria c = elegidas[i];
                    Elemento primero = c.Elementos[0];
                    bool hayPrimero = !primero.Carpeta && File.Exists(Ruta(o, primero));
                    for (int j = 0; j < c.Elementos.Length; j++)
                    {
                        Elemento e = c.Elementos[j];
                        string src = Ruta(o, e);
                        string dst = Ruta(a, e);
                        if (src == null || dst == null || CopiaJuego.Igual(src, dst))
                        {
                            continue;
                        }
                        if (e.Carpeta)
                        {
                            foreach (Pendiente f in Faltantes(src, dst))
                            {
                                Pendiente p = f;
                                p.Donde = e.Donde;
                                p.RelBase = Path.Combine(e.Rel, f.Rel);
                                nuevos.Add(p);
                                deCarpeta.Add(c.Nombre);
                                total += p.Bytes;
                            }
                            continue;
                        }
                        bool alli = File.Exists(src);
                        if (e.Depende && !hayPrimero)
                        {
                            continue;
                        }
                        if (!alli && (!e.Depende || !File.Exists(dst)))
                        {
                            continue;
                        }
                        Reemplazo r = new Reemplazo();
                        r.E = e;
                        r.Origen = alli ? src : null;
                        r.Destino = dst;
                        r.Respaldo = Path.Combine(respaldo, NombreBase(e.Donde), e.Rel);
                        reemplazos.Add(r);
                        if (alli)
                        {
                            total += new FileInfo(src).Length;
                        }
                    }
                }
                Interlocked.Exchange(ref bytesTotal, total);
                MelonLogger.Msg("[Importar] plan: " + reemplazos.Count.ToString() + " a reemplazar, "
                    + nuevos.Count.ToString() + " nuevos (" + Tam(total) + ")");

                if (reemplazos.Count == 0 && nuevos.Count == 0)
                {
                    sinCambios = true;
                    return;
                }

                // 2. Espacio. Con margen: el juego tambien necesita escribir.
                long libre = Libre(a.Datos);
                if (libre >= 0 && total + (256L << 20) > libre)
                {
                    throw new ErrorImportar("Not enough free space: the import needs " + Tam(total)
                        + " and there's " + Tam(libre) + " free.");
                }

                // 3. Lo que se reemplaza, a temporales junto a su destino. Si
                // algo falla aqui no se ha tocado nada todavia.
                fase = "Copying profiles, settings and scores...";
                for (int i = 0; i < reemplazos.Count; i++)
                {
                    Reemplazo r = reemplazos[i];
                    if (r.Origen == null)
                    {
                        continue;
                    }
                    archivoActual = Path.GetFileName(r.Destino);
                    Directory.CreateDirectory(Path.GetDirectoryName(r.Destino));
                    r.Temp = r.Destino + Temporal;
                    Copiar(r.Origen, r.Temp);
                }

                // 4. Las carpetas que se mezclan. Un archivo suelto que no se
                // pueda leer no para todo; quedarse sin disco si.
                int copiados = 0;
                int fallidos = 0;
                for (int i = 0; i < nuevos.Count; i++)
                {
                    Pendiente p = nuevos[i];
                    fase = "Copying " + deCarpeta[i] + "...  " + (i + 1).ToString("N0")
                         + " of " + nuevos.Count.ToString("N0") + " files";
                    archivoActual = p.Rel;
                    string parte = p.Destino + Parcial;
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(p.Destino));
                        Copiar(p.Origen, parte);
                        File.Move(parte, p.Destino);
                        agregados.Add("added	" + NombreBase(p.Donde) + "	" + p.RelBase);
                        copiados++;
                    }
                    catch (Exception ex)
                    {
                        Borrar(parte);
                        if (SinEspacio(ex))
                        {
                            throw new ErrorImportar("The disk got full after copying "
                                + copiados.ToString("N0") + " files. Nothing else was changed.");
                        }
                        fallidos++;
                        MelonLogger.Warning("[Importar] no se pudo copiar " + p.Origen + ": " + ex.Message);
                    }
                }

                // 5. El cambiazo: lo de aqui al respaldo, lo nuevo a su sitio.
                fase = "Saving...";
                archivoActual = null;
                int puestos = 0;
                try
                {
                    for (int i = 0; i < reemplazos.Count; i++)
                    {
                        Reemplazo r = reemplazos[i];
                        if (File.Exists(r.Destino))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(r.Respaldo));
                            File.Move(r.Destino, r.Respaldo, true);
                            r.Movido = true;
                        }
                        if (r.Temp != null)
                        {
                            File.Move(r.Temp, r.Destino);
                            r.Puesto = true;
                            puestos++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error("[Importar] al colocar los archivos: " + ex);
                    Deshacer(reemplazos);
                    throw new ErrorImportar("Couldn't replace this copy's files (" + ex.Message
                        + "). They were left as they were.");
                }

                int retirados = 0;
                List<string> lineas = new List<string>();
                for (int i = 0; i < reemplazos.Count; i++)
                {
                    Reemplazo r = reemplazos[i];
                    string donde = NombreBase(r.E.Donde) + "	" + r.E.Rel;
                    if (r.Origen == null && r.Movido)
                    {
                        retirados++;
                        lineas.Add("removed	" + donde);
                    }
                    else if (r.Puesto)
                    {
                        lineas.Add((r.Movido ? "replaced	" : "created	") + donde);
                    }
                }
                lineas.AddRange(agregados);
                EscribirManifiesto(respaldo, o, queSeImporta, lineas);
                StringBuilder sb = new StringBuilder();
                if (puestos > 0)
                {
                    sb.Append("Replaced " + puestos.ToString() + (puestos == 1 ? " file" : " files"));
                }
                if (copiados > 0)
                {
                    sb.Append(sb.Length > 0 ? " and added " : "Added ");
                    sb.Append(copiados.ToString("N0") + (copiados == 1 ? " new file" : " new files"));
                }
                if (sb.Length == 0)
                {
                    sb.Append("Updated the files");
                }
                sb.Append('.');
                if (fallidos > 0)
                {
                    sb.Append(" " + fallidos.ToString("N0")
                        + (fallidos == 1 ? " file" : " files") + " couldn't be read and were skipped.");
                }
                resumen = sb.ToString();
                MelonLogger.Msg("[Importar] hecho: " + puestos.ToString() + " reemplazados, "
                    + retirados.ToString() + " retirados, " + copiados.ToString() + " nuevos, "
                    + fallidos.ToString() + " fallidos. Respaldo: " + respaldo);
            }
            catch (ErrorImportar ex)
            {
                Limpiar(reemplazos);
                errorHilo = ex.Message;
                MelonLogger.Warning("[Importar] " + ex.Message);
                // Lo que ya se anadio a Custom se queda: que se pueda quitar
                // restaurando.
                EscribirManifiesto(respaldo, o, queSeImporta, agregados);
            }
            catch (Exception ex)
            {
                Limpiar(reemplazos);
                errorHilo = "The import stopped: " + ex.Message + " Nothing was replaced.";
                MelonLogger.Error("[Importar] " + ex);
                EscribirManifiesto(respaldo, o, queSeImporta, agregados);
            }
            finally
            {
                terminado = true;
            }
        }

        // import.txt en la carpeta del respaldo. Lo que hace falta para
        // deshacer una importacion y que el respaldo no tiene: los archivos que
        // la importacion CREO (antes no habia nada que respaldar) y los que
        // anadio a Custom y Setlists. Restaurar los borra.
        private const string Manifiesto = "import.txt";

        private static void EscribirManifiesto(string respaldo, CopiaJuego o, string que, List<string> lineas)
        {
            if (lineas == null || lineas.Count == 0)
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(respaldo);
                List<string> todo = new List<string>();
                todo.Add("# Cool Mod: what this import changed. Used by Restore a Backup.");
                todo.Add("from	" + o.Raiz);
                todo.Add("version	" + o.Version);
                todo.Add("what	" + que);
                todo.AddRange(lineas);
                File.WriteAllLines(Path.Combine(respaldo, Manifiesto), todo, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Importar] manifiesto: " + ex.Message);
            }
        }

        private sealed class ErrorImportar : Exception
        {
            public ErrorImportar(string m) : base(m) { }
        }

        private static void Limpiar(List<Reemplazo> reemplazos)
        {
            for (int i = 0; i < reemplazos.Count; i++)
            {
                if (reemplazos[i].Temp != null && !reemplazos[i].Puesto)
                {
                    Borrar(reemplazos[i].Temp);
                }
            }
        }

        private static void Deshacer(List<Reemplazo> reemplazos)
        {
            for (int i = reemplazos.Count - 1; i >= 0; i--)
            {
                Reemplazo r = reemplazos[i];
                try
                {
                    if (r.Puesto)
                    {
                        File.Delete(r.Destino);
                        r.Puesto = false;
                    }
                    if (r.Movido)
                    {
                        File.Move(r.Respaldo, r.Destino, true);
                        r.Movido = false;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error("[Importar] no se pudo deshacer " + r.Destino + ": " + ex.Message);
                }
            }
        }

        private static void Copiar(string src, string dst)
        {
            byte[] buf = new byte[1 << 20];
            using (FileStream i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (FileStream o = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int n;
                while ((n = i.Read(buf, 0, buf.Length)) > 0)
                {
                    o.Write(buf, 0, n);
                    Interlocked.Add(ref bytesHechos, n);
                }
            }
            try { File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src)); } catch (Exception) { }
        }

        private static void Borrar(string f)
        {
            try
            {
                if (f != null && File.Exists(f))
                {
                    File.Delete(f);
                }
            }
            catch (Exception)
            {
            }
        }

        private static bool SinEspacio(Exception ex)
        {
            int h = ex.HResult & 0xFFFF;
            return ex is IOException && (h == 0x70 || h == 0x27);
        }

        private static long Libre(string carpeta)
        {
            try
            {
                return new DriveInfo(Path.GetPathRoot(carpeta)).AvailableFreeSpace;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static void Terminar()
        {
            if (restaurando)
            {
                TerminarRestauracion();
                return;
            }
            if (errorHilo != null)
            {
                estado = Estado.Eligiendo;
                Decir(errorHilo, true);
                if (origen != null)
                {
                    MirarOrigen(true);      // lo que falta puede haber cambiado
                }
                return;
            }
            if (sinCambios)
            {
                estado = Estado.Eligiendo;
                Decir("Nothing to import: this copy already has all of it.", false);
                return;
            }
            estado = Estado.Listo;
            reiniciarEn = Time.unscaledTime + 3f;
        }

        // Ver la cabecera: matar, no cerrar, o el juego pisa lo importado.
        private static void Reiniciar()
        {
            estado = Estado.Cerrado;
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                MelonLogger.Msg("[Importar] reiniciando " + exe);
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                    "/c ping -n 3 127.0.0.1 >nul & explorer.exe \"" + exe + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Importar] no se pudo relanzar: " + ex);
            }
            Process.GetCurrentProcess().Kill();
        }

        // ---------------------------------------------- ventana de Windows
        // La del propio juego (T0268 en la v1.0): devuelve la ruta en memoria
        // de CoTaskMemAlloc, que es lo que libera el marshalling del juego.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr FnCarpeta(IntPtr ventana, uint opciones,
            [MarshalAs(UnmanagedType.LPWStr)] string titulo);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string ruta);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr modulo, string nombre);

        private static FnCarpeta dialogo;

        private static string DialogoCarpeta(string titulo, out bool fallo)
        {
            fallo = false;
            try
            {
                if (dialogo == null)
                {
                    string dll = Path.Combine(Application.dataPath, "Plugins", "x86_64",
                                              "NativeFilePicker.dll");
                    IntPtr h = LoadLibraryW(dll);
                    if (h == IntPtr.Zero)
                    {
                        MelonLogger.Warning("[Importar] no se pudo cargar " + dll);
                        fallo = true;
                        return null;
                    }
                    IntPtr f = GetProcAddress(h, "ShowOpenFolderDialog");
                    if (f == IntPtr.Zero)
                    {
                        MelonLogger.Warning("[Importar] NativeFilePicker sin ShowOpenFolderDialog");
                        fallo = true;
                        return null;
                    }
                    dialogo = Marshal.GetDelegateForFunctionPointer<FnCarpeta>(f);
                }
                IntPtr r = dialogo(IntPtr.Zero, 0u, titulo);
                if (r == IntPtr.Zero)
                {
                    return null;
                }
                string s = Marshal.PtrToStringUni(r);
                Marshal.FreeCoTaskMem(r);
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Importar] ventana de carpetas: " + ex.Message);
                fallo = true;
                return null;
            }
        }

        // ------------------------------------------------------------ panel
        private const string Oro = "#FFD24A";
        private const string Gris = "#8C8C8C";
        private const string Rojo = "#FF7070";
        private const string Verde = "#8BE28B";

        private static GameObject raiz;
        private static Il2CppTMPro.TextMeshProUGUI texto;
        private static bool falloCreacion;
        private static string ultimoTexto;

        private static void Crear()
        {
            Il2CppTMPro.TextMeshProUGUI plantilla =
                UnityEngine.Object.FindObjectOfType<Il2CppTMPro.TextMeshProUGUI>();
            if (plantilla == null || plantilla.font == null)
            {
                return;      // se reintenta en el siguiente fotograma
            }
            try
            {
                raiz = new GameObject("CoolModImportPanel");
                UnityEngine.Object.DontDestroyOnLoad(raiz);
                Canvas lienzo = raiz.AddComponent<Canvas>();
                lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
                lienzo.sortingOrder = 31950;      // los avisos, encima
                CanvasScaler escala = raiz.AddComponent<CanvasScaler>();
                escala.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                escala.referenceResolution = new Vector2(1920f, 1080f);
                raiz.AddComponent<GraphicRaycaster>();

                // Velo a pantalla completa: oscurece el menu y se come los
                // clics del raton para que no lleguen a lo de debajo.
                GameObject veloGo = new GameObject("Veil");
                veloGo.transform.SetParent(raiz.transform, false);
                Image velo = veloGo.AddComponent<Image>();
                velo.color = new Color(0f, 0f, 0f, 0.6f);
                RectTransform rv = veloGo.GetComponent<RectTransform>();
                rv.anchorMin = Vector2.zero;
                rv.anchorMax = Vector2.one;
                rv.offsetMin = Vector2.zero;
                rv.offsetMax = Vector2.zero;

                GameObject panelGo = new GameObject("Panel");
                panelGo.transform.SetParent(raiz.transform, false);
                Image panel = panelGo.AddComponent<Image>();
                panel.color = new Color(0.04f, 0.04f, 0.06f, 0.96f);
                RectTransform rp = panelGo.GetComponent<RectTransform>();
                rp.anchorMin = new Vector2(0.5f, 0.5f);
                rp.anchorMax = new Vector2(0.5f, 0.5f);
                rp.pivot = new Vector2(0.5f, 0.5f);
                rp.anchoredPosition = Vector2.zero;
                rp.sizeDelta = new Vector2(1240f, 900f);

                GameObject textoGo = new GameObject("Text");
                textoGo.transform.SetParent(panelGo.transform, false);
                texto = textoGo.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                texto.font = plantilla.font;
                texto.fontSize = 28f;
                texto.color = Color.white;
                texto.richText = true;
                texto.enableWordWrapping = true;
                texto.alignment = Il2CppTMPro.TextAlignmentOptions.TopLeft;
                texto.raycastTarget = false;
                RectTransform rt = textoGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(48f, 36f);
                rt.offsetMax = new Vector2(-48f, -36f);
                ultimoTexto = null;
            }
            catch (Exception ex)
            {
                falloCreacion = true;
                MelonLogger.Error("[Importar] panel: " + ex);
                Destruir();
            }
        }

        private static void Destruir()
        {
            try
            {
                if (raiz != null)
                {
                    UnityEngine.Object.Destroy(raiz);
                }
            }
            catch (Exception)
            {
            }
            raiz = null;
            texto = null;
            ultimoTexto = null;
        }

        private static void Pintar()
        {
            if (texto == null)
            {
                return;
            }
            filasLink = new List<int>();
            string t = estado == Estado.Eligiendo ? TextoEleccion()
                     : estado == Estado.Respaldos ? TextoRespaldos()
                     : estado == Estado.Importando ? TextoProgreso() : TextoListo();
            if (t != ultimoTexto)
            {
                ultimoTexto = t;
                texto.text = t;
                // Que textInfo (lineas y links) corresponda ya a este texto:
                // el raton lo consulta en el siguiente fotograma.
                texto.ForceMeshUpdate(false, false);
                filasMostradas = filasLink;
            }
        }

        private static string Tinta(string color, string t)
        {
            return "<color=" + color + ">" + t + "</color>";
        }

        private static string Literal(string t)
        {
            return "<noparse>" + (t ?? "") + "</noparse>";
        }

        private static string Fila(int i, string t)
        {
            return Enlace(i, i == cursor ? Tinta(Oro, "> " + t) : "   " + t);
        }

        private static string TextoEleccion()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<size=40><b>Import From Another Copy</b></size>\n\n");

            sb.Append(Fila(0, "Browse...")).Append('\n');
            if (origen == null)
            {
                sb.Append(Enlace(0, "      " + Tinta(Gris, "No copy chosen yet."))).Append('\n');
            }
            else
            {
                sb.Append(Enlace(0, "      " + Literal(origen.Raiz))).Append('\n');
                sb.Append(Enlace(0, "      " + Tinta(Verde, origen.Descripcion))).Append('\n');
            }
            if (!string.IsNullOrEmpty(mensaje))
            {
                sb.Append("      ").Append(Tinta(mensajeError ? Rojo : Oro, Literal(mensaje))).Append('\n');
            }

            sb.Append("\nWhat to import:\n");
            for (int i = 0; i < categorias.Length; i++)
            {
                Categoria c = categorias[i];
                string linea;
                if (origen != null && !c.Disponible)
                {
                    linea = Tinta(Gris, "[  ] " + c.Nombre + "  -  not in that copy");
                }
                else
                {
                    linea = (c.Marcada ? "[X] " : "[  ] ") + c.Nombre;
                    string nota = c.Nota;
                    if (c.Grande && origen != null)
                    {
                        nota += c.Medida
                            ? (c.BytesNuevos == 0 ? " (nothing new)" : " (" + Tam(c.BytesNuevos) + " new)")
                            : " (measuring...)";
                    }
                    if (!string.IsNullOrEmpty(nota))
                    {
                        linea += "  " + Tinta(Gris, "<size=80%>" + nota + "</size>");
                    }
                }
                sb.Append(Fila(i + 1, linea)).Append('\n');
            }

            sb.Append('\n');
            sb.Append(Fila(FilaImportar, confirmando
                ? "Import now? Press again to confirm" : "Import")).Append('\n');
            sb.Append(Fila(FilaRestaurar, "Restore a Backup...")).Append('\n');
            sb.Append(Fila(FilaCancelar, "Cancel")).Append('\n');

            sb.Append("\n<size=80%>").Append(Tinta(Gris,
                "Profiles, settings, scores, favorites and song volumes replace the ones in this"
                + " copy; the old ones are backed up first in the Import Backups folder.\n"
                + "Custom and setlists only add what's missing: nothing there gets overwritten.\n"
                + "The game restarts by itself when the import is done."))
              .Append("</size>");
            return sb.ToString();
        }

        private static string TextoProgreso()
        {
            long hecho = Interlocked.Read(ref bytesHechos);
            long total = Interlocked.Read(ref bytesTotal);
            int pct = total > 0 ? (int)(hecho * 100 / total) : 0;
            if (pct > 100) pct = 100;
            string archivo = archivoActual;
            if (archivo != null && archivo.Length > 70)
            {
                archivo = "..." + archivo.Substring(archivo.Length - 67);
            }
            return (restaurando
                    ? "<size=40><b>Restoring...</b></size>\n\n"
                      + "Backup from " + Literal(FechaDe(carpetaRespaldo)) + "\n\n"
                    : "<size=40><b>Importing...</b></size>\n\n"
                      + "From: " + Literal(origen != null ? origen.Raiz : "") + "\n\n")
                + Literal(fase) + "\n"
                + (total > 0 ? Tam(hecho) + " of " + Tam(total) + "   (" + pct.ToString() + "%)" : "") + "\n"
                + Tinta(Gris, Literal(archivo)) + "\n\n"
                + Tinta(Gris, "Please don't close the game. It restarts by itself when it's done.");
        }

        private static string TextoListo()
        {
            if (restaurando)
            {
                return "<size=40><b>Restore complete</b></size>\n\n"
                    + Literal(resumen) + "\n\n"
                    + Tinta(Oro, "Restarting Clone Hero...");
            }
            return "<size=40><b>Import complete</b></size>\n\n"
                + Literal(resumen) + "\n\n"
                + Tinta(Gris, "The replaced files were backed up in:") + "\n"
                + Tinta(Gris, Literal(carpetaRespaldo)) + "\n\n"
                + Tinta(Oro, "Restarting Clone Hero...");
        }

        private static string Tam(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.0") + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0.0") + " MB";
            if (b >= 1L << 10) return (b / (double)(1L << 10)).ToString("0") + " KB";
            return b.ToString() + " bytes";
        }
    }
}
