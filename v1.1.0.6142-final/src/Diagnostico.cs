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
    // Mod de exploracion. No cambia nada del juego: solo vuelca a un archivo de
    // texto el estado real de las estructuras que necesitamos enganchar, para
    // no tener que adivinarlo desde el volcado estatico (que no trae cuerpos de
    // metodo, asi que no deja ver el contenido de los arrays).
    //
    // Genera:  <juego>\MelonLoader\diagnostico-ch.txt
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

        public override void OnUpdate()
        {
            // F10 lanza el calculo de dificultad de toda la biblioteca.
            // De momento va por tecla y no por opcion de menu: anadir filas a
            // los menus en IL2CPP es un trabajo aparte que todavia no esta.
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F10))
            {
                CalculadorDificultad.Lanzar();
            }

            if (!fondosListos)
            {
                preparacion += UnityEngine.Time.unscaledDeltaTime;
                if (preparacion >= 3f)
                {
                    fondosListos = true;
                    Ajustes.Cargar();
                    FondosPersonalizados.Instalar();
                }
            }

            if (yaVolcado)
            {
                // Mantiene lleno el hueco de la cache de secciones.
                OrdenDificultad.Tick();
                EtiquetaDificultad.Tick();
                OverlayProgreso.Refrescar();
                SfxFinDeCancion.Tick();
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
                OrdenDificultad.Instalar();
                FondosPersonalizados.Instalar();
                FiltroFavoritos.Instalar();
                FiltroFavoritos.InstalarParche(HarmonyInstance);
                OpcionCalcular.InstalarParches(HarmonyInstance);
                OrdenDificultad.InstalarParcheRefresco(HarmonyInstance);
                MenuVideo.InstalarParches(HarmonyInstance);
                MenuAudio.InstalarParches(HarmonyInstance);
                Volcar();
                DiagnosticoOrden.Volcar();
                DiagnosticoMenu.Volcar();
                LanzarSiHayBandera();
            }
            catch (Exception ex)
            {
                MelonLogger.Error("fallo el volcado: " + ex);
            }
        }

        public override void OnLateUpdate()
        {
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
