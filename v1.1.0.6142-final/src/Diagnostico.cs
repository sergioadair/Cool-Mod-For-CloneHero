using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(CloneHeroMod.Diagnostico), "Cool Mod For Clone Hero", "1.0.0", "sergioadair")]
[assembly: MelonGame(null, null)]

namespace CloneHeroMod
{
    // Punto de entrada del mod: instala las funciones al arrancar y reparte
    // el trabajo por fotograma.
    //
    // RENDIMIENTO: mientras la escena activa es "Gameplay" no se ejecuta nada
    // en absoluto. Lo que habia antes (busquedas de objetos y recuentos sobre
    // miles de canciones corriendo en cada fotograma) costaba FPS y provocaba
    // tirones durante la cancion.
    //
    // Conserva los volcados de diagnostico que sirvieron para localizar las
    // estructuras del juego, pero solo se ejecutan si existe el archivo
    // MelonLoader\diagnostico.flag.
    public class Diagnostico : MelonMod
    {
        // Nombres ofuscados confirmados en el volcado de v1.1.0.6142.
        //
        // Van como escapes \uXXXX a proposito: son letras modificadoras Unicode
        // (U+02B2-U+02C1) y si se escriben literalmente, el compilador las lee
        // con la codepage ANSI del sistema cuando el .cs no lleva BOM, y la
        // comparacion por nombre falla en silencio.
        private const string TipoBiblioteca =    // SongLibrary
            "ʾʲʽʻʺʶʺʺʷʿʺ";
        private const string TipoFiltros =       // fabrica de filtros
            "ʷʹˁʼʸʽˀʺʻʸʳ";
        private const string TipoAjustes =       // ajustes globales
            "ʹʺʽˁʽˁˀʼʶʷʼ";
        private const string TipoFavoritos =     // FavoritesManager nativo
            "ʾʷʹʿʻʻʿʽʳʽˁ";

        private bool yaVolcado;
        private bool fondosListos;
        private float preparacion;
        private float transcurrido;
        private int revision;

        // Hay diagnostico pedido? Lo consultan otras partes del mod para
        // registrar detalles sin ensuciar el log en uso normal.
        private static bool detallado;
        public static bool Detallado { get { return detallado; } }

        public override void OnUpdate()
        {
            // Durante la cancion lo unico que puede correr es el cartel de
            // racha de notas, y solo si esta activado: su Tick sale en la
            // primera linea cuando no lo esta.
            if (Buscador.EnJuego)
            {
                RachaNotas.Tick();
                return;
            }
            // F10 lanza el calculo de dificultad de toda la biblioteca.
            // De momento va por tecla y no por opcion de menu: anadir filas a
            // los menus en IL2CPP es un trabajo aparte que todavia no esta.
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F10))
            {
                CalculadorDificultad.Lanzar();
            }
            // F9 vuelca el estado del jugador, para localizar instrumento y
            // dificultad. Solo con diagnostico.flag.
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
            {
                DiagnosticoJugador.Volcar();
            }

            if (!fondosListos)
            {
                preparacion += UnityEngine.Time.unscaledDeltaTime;
                if (preparacion >= 3f)
                {
                    fondosListos = true;
                    Ajustes.Cargar();
                    RachaNotas.ResolverEstilo();
                    FondosPersonalizados.Instalar();
                }
            }

            if (yaVolcado)
            {
                // Mantiene lleno el hueco de la cache de secciones.
                // La etiqueta primero: refresca la referencia a SongSelect que
                // el orden consulta para saber si hay que hacer algo.
                RachaNotas.ResolverEstilo();
                AvisoVersion.Tick();
                EtiquetaDificultad.Tick();
                PanelPerfil.Tick();
                OrdenDificultad.Tick();
                OpcionCalcular.Tick();
                MenuVideo.Tick();
                MenuAudio.Tick();
                MenuGameplay.Tick();
                OverlayProgreso.Refrescar();
                SfxFinDeCancion.Tick();

                // El juego rehace sus listas de orden y de filtro al reescanear
                // la biblioteca, y se lleva por delante lo que anadimos. Se
                // comprueba de vez en cuando —no en cada fotograma— y se vuelve
                // a poner si hace falta.
                revision++;
                if (revision >= 180)
                {
                    revision = 0;
                    FiltroFavoritos.Verificar();
                    OrdenDificultad.Verificar();
                }
                return;
            }
            // Se espera un poco a que el juego termine de inicializarse: las
            // listas de orden y filtro se rellenan durante el arranque.
            transcurrido += UnityEngine.Time.unscaledDeltaTime;
            if (transcurrido < 12f)
            {
                return;
            }
            yaVolcado = true;
            try
            {
                Actualizador.LimpiarRestos();
                OrdenDificultad.Instalar();
                FondosPersonalizados.Instalar();
                FiltroFavoritos.Instalar();
                FiltroFavoritos.InstalarParche(HarmonyInstance);
                OpcionCalcular.InstalarParches(HarmonyInstance);
                OrdenDificultad.InstalarParcheRefresco(HarmonyInstance);
                OrdenDificultad.InstalarParcheNombre(HarmonyInstance);
                MenuVideo.InstalarParches(HarmonyInstance);
                MenuAudio.InstalarParches(HarmonyInstance);
                MenuGameplay.InstalarParches(HarmonyInstance);
                Actualizador.Comprobar();
                // Los volcados recorren los 1475 tipos del juego y leen todos
                // sus miembros estaticos: util para investigar, pero caro y sin
                // sentido en uso normal. Solo si se pide con un archivo.
                detallado = File.Exists(Path.Combine(
                    MelonEnvironment.MelonLoaderDirectory, "diagnostico.flag"));
                if (detallado)
                {
                    Volcar();
                    DiagnosticoOrden.Volcar();
                    DiagnosticoOrden.VolcarColecciones();
                    DiagnosticoMenu.Volcar();
                }
                LanzarSiHayBandera();
            }
            catch (Exception ex)
            {
                MelonLogger.Error("fallo el volcado: " + ex);
            }
        }

        // Al cambiar de escena es cuando de verdad pueden aparecer o
        // desaparecer los objetos que buscamos, asi que se reinicia la espera
        // de las busquedas. Fuera de esos momentos van espaciandose solas.
        public override void OnSceneWasLoaded(int indice, string nombre)
        {
            Buscador.EscenaCambiada(nombre);
            RachaNotas.EscenaCambiada(nombre, Buscador.EnJuego);
            // Los paneles del menu se destruyen al cambiar de escena; sus
            // punteros pueden reutilizarse, asi que la cache de etiquetas se
            // tira para no dar por buena una que ya no existe.
            EtiquetaDificultad.OlvidarPaneles();
            PanelPerfil.EscenaCambiada();
            AvisoVersion.EscenaCambiada();
        }

        public override void OnLateUpdate()
        {
            if (Buscador.EnJuego)
            {
                return;
            }
            FondosPersonalizados.Tick();
        }

        private void Volcar()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Diagnostico Clone Hero v1.1.0.6142 ===");
            sb.AppendLine("fecha: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            Assembly ensamblado = LocalizarEnsamblado();
            if (ensamblado == null)
            {
                sb.AppendLine("NO se encontro el ensamblado Il2CppCloneHero.");
                Escribir(sb);
                return;
            }
            sb.AppendLine("ensamblado: " + ensamblado.GetName().Name);
            sb.AppendLine();

            InventarioTipos(sb, ensamblado);
            VolcarTipo(sb, ensamblado, TipoBiblioteca, "BIBLIOTECA DE CANCIONES (SongLibrary)");
            VolcarTipo(sb, ensamblado, TipoAjustes, "AJUSTES GLOBALES");
            VolcarTipo(sb, ensamblado, TipoFavoritos, "FAVORITOS NATIVOS");
            VolcarMiembros(sb, ensamblado, TipoFiltros, "FABRICA DE FILTROS");

            Escribir(sb);
        }

        // Red de seguridad: si la busqueda por nombre vuelve a fallar, esto deja
        // ver con que esquema quedaron nombrados los tipos ofuscados y cuantos
        // hay, sin tener que volver a arrancar el juego para averiguarlo.
        private void InventarioTipos(StringBuilder sb, Assembly ens)
        {
            sb.AppendLine("---------- INVENTARIO DE TIPOS ----------");
            Type[] tipos;
            try
            {
                tipos = ens.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                tipos = ex.Types;
                sb.AppendLine("(carga parcial: " + ex.LoaderExceptions.Length.ToString() + " errores)");
            }
            int total = 0;
            int ofuscados = 0;
            for (int i = 0; i < tipos.Length; i++)
            {
                if (tipos[i] == null)
                {
                    continue;
                }
                total++;
                if (EsOfuscado(tipos[i].Name))
                {
                    ofuscados++;
                }
            }
            sb.AppendLine("tipos totales: " + total.ToString());
            sb.AppendLine("con nombre ofuscado literal: " + ofuscados.ToString());
            sb.AppendLine("indexados por [ObfuscatedName]: " + Ofuscado.TiposIndexados.ToString());

            sb.AppendLine("muestra de renombrados (real -> interop):");
            int n = 0;
            for (int i = 0; i < tipos.Length && n < 12; i++)
            {
                if (tipos[i] == null)
                {
                    continue;
                }
                string real = Ofuscado.NombreReal(tipos[i]);
                if (real == null)
                {
                    continue;
                }
                sb.AppendLine("  " + real + "  ->  " + tipos[i].FullName);
                n++;
            }
            sb.AppendLine();
        }

        // "nombreReal (nombreInterop)" cuando difieren, para poder copiar
        // directamente el identificador que hay que usar en el mod.
        private string Etiqueta(MemberInfo m)
        {
            string real = Ofuscado.NombreReal(m);
            if (real == null || real == m.Name)
            {
                return m.Name;
            }
            return real + " (" + m.Name + ")";
        }

        private bool EsOfuscado(string nombre)
        {
            for (int i = 0; i < nombre.Length; i++)
            {
                if (nombre[i] >= 'ʲ' && nombre[i] <= 'ˁ')
                {
                    return true;
                }
            }
            return false;
        }

        // Si existe <juego>\MelonLoader\calcular-dificultad.flag, se lanza el
        // calculo al arrancar y se borra la bandera. Sirve para dispararlo sin
        // tener que estar delante del juego.
        private void LanzarSiHayBandera()
        {
            try
            {
                string bandera = Path.Combine(MelonEnvironment.MelonLoaderDirectory,
                                              "calcular-dificultad.flag");
                if (!File.Exists(bandera))
                {
                    return;
                }
                File.Delete(bandera);
                MelonLogger.Msg("[Dificultad] bandera encontrada, lanzando");
                CalculadorDificultad.Lanzar();
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Dificultad] bandera: " + ex);
            }
        }

        private Assembly LocalizarEnsamblado()
        {
            Assembly[] todos = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < todos.Length; i++)
            {
                string nombre = todos[i].GetName().Name;
                if (nombre == "Il2CppCloneHero")
                {
                    return todos[i];
                }
            }
            return null;
        }

        // Vuelca los valores de todos los miembros estaticos que parezcan
        // listas de cadenas: es donde viven los nombres de orden y de filtro.
        private void VolcarTipo(StringBuilder sb, Assembly ens, string nombreTipo, string titulo)
        {
            sb.AppendLine("---------- " + titulo + " ----------");
            Type t = BuscarTipo(ens, nombreTipo);
            if (t == null)
            {
                sb.AppendLine("tipo no encontrado: " + nombreTipo);
                sb.AppendLine();
                return;
            }
            sb.AppendLine("tipo: " + t.FullName);

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            PropertyInfo[] props = t.GetProperties(flags);
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].GetIndexParameters().Length != 0)
                {
                    continue;
                }
                VolcarValor(sb, Etiqueta(props[i]), props[i].PropertyType, LeerSeguro(props[i]));
            }

            FieldInfo[] campos = t.GetFields(flags);
            for (int i = 0; i < campos.Length; i++)
            {
                VolcarValor(sb, Etiqueta(campos[i]), campos[i].FieldType, LeerSeguro(campos[i]));
            }
            sb.AppendLine();
        }

        private void VolcarMiembros(StringBuilder sb, Assembly ens, string nombreTipo, string titulo)
        {
            sb.AppendLine("---------- " + titulo + " ----------");
            Type t = BuscarTipo(ens, nombreTipo);
            if (t == null)
            {
                sb.AppendLine("tipo no encontrado: " + nombreTipo);
                sb.AppendLine();
                return;
            }
            sb.AppendLine("tipo: " + t.FullName);
            MethodInfo[] metodos = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < metodos.Length; i++)
            {
                MethodInfo m = metodos[i];
                if (m.DeclaringType != t)
                {
                    continue;
                }
                ParameterInfo[] ps = m.GetParameters();
                StringBuilder firma = new StringBuilder();
                firma.Append("  ").Append(m.ReturnType.Name).Append(' ').Append(m.Name).Append('(');
                for (int j = 0; j < ps.Length; j++)
                {
                    if (j > 0)
                    {
                        firma.Append(", ");
                    }
                    firma.Append(ps[j].ParameterType.Name);
                }
                firma.Append(')');
                sb.AppendLine(firma.ToString());
            }
            sb.AppendLine();
        }

        private Type BuscarTipo(Assembly ens, string nombre)
        {
            return Ofuscado.Tipo(nombre);
        }

        private object LeerSeguro(PropertyInfo p)
        {
            try
            {
                return p.GetValue(null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private object LeerSeguro(FieldInfo f)
        {
            try
            {
                return f.GetValue(null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Solo interesa lo que sea enumerable de cadenas (arrays de nombres de
        // orden/filtro) y los escalares simples. Lo demas se lista por tipo.
        private void VolcarValor(StringBuilder sb, string nombre, Type tipo, object valor)
        {
            if (valor == null)
            {
                return;
            }
            string nt = tipo.Name;
            bool interesante = nt.Contains("String") || nt.Contains("HashSet") || nt.Contains("List");
            if (!interesante)
            {
                return;
            }

            sb.Append("  ").Append(nombre).Append("  <").Append(nt).Append(">");

            IEnumerable e = valor as IEnumerable;
            if (e == null)
            {
                sb.AppendLine(" = " + valor.ToString());
                return;
            }

            sb.AppendLine();
            int n = 0;
            try
            {
                foreach (object o in e)
                {
                    sb.Append("      [").Append(n.ToString()).Append("] ")
                      .AppendLine(o == null ? "(null)" : o.ToString());
                    n++;
                    if (n > 200)
                    {
                        sb.AppendLine("      ... cortado");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("      no enumerable: " + ex.GetType().Name);
            }
            if (n == 0)
            {
                sb.AppendLine("      (vacio)");
            }
        }

        private void Escribir(StringBuilder sb)
        {
            string destino = Path.Combine(MelonEnvironment.MelonLoaderDirectory, "diagnostico-ch.txt");
            File.WriteAllText(destino, sb.ToString(), Encoding.UTF8);
            MelonLogger.Msg("diagnostico escrito en " + destino);
        }
    }
}
