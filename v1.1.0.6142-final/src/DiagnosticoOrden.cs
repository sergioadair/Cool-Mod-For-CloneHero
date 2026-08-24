using System;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;

namespace CloneHeroMod
{
    // Volcado dirigido para montar el orden por dificultad. Necesito tres cosas
    // que no se ven en el volcado estatico de Il2CppDumper (no trae cuerpos de
    // metodo ni valores):
    //
    //   1. Que ajuste corresponde a "sort_filter" y como subirle el maximo,
    //      porque si no, el criterio nuevo queda inalcanzable.
    //   2. Como se construye el tipo Seccion (constructor y campos).
    //   3. Tamano del array de cache de secciones, para saber si hay hueco
    //      para un criterio mas.
    public static class DiagnosticoOrden
    {
        private const string TipoBiblioteca = "ʾʲʽʻʺʶʺʺʷʿʺ";   // SongLibrary
        private const string TipoAjustes = "ʹʺʽˁʽˁˀʼʶʷʼ";       // ajustes globales
        private const string TipoSeccion = "ˁʽʸʲʳʺʸʸˀʶʸ";       // Seccion de la lista
        private const string CacheSecciones = "ʳˀʸʿʹʸʻˀʴʿʲ";
        private const string BanderaSucia = "ʴʾˀʺʷʽʳʳˀʽʲ";

        // Todas las colecciones de cadenas estaticas de SongLibrary, con su
        // contenido. Hace falta para un fallo concreto: al ordenar por nuestro
        // criterio CON UN FILTRO ACTIVO, el juego llama a
        // SongLibrary.ʳʸʲʿˀʶˁʽʳʸʹ(string) con el nombre del criterio y revienta
        // con IndexOutOfRange. Eso apunta a que el nombre se busca en OTRA
        // coleccion —distinta del string[] al que lo anadimos— que devuelve -1.
        // Aqui se ve cual es la que tiene los otros 27 nombres y no el nuestro.
        public static void VolcarColecciones()
        {
            try
            {
                Type t = Ofuscado.Tipo(TipoBiblioteca);
                if (t == null)
                {
                    return;
                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== colecciones de cadenas de SongLibrary ===");
                BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic
                                 | BindingFlags.Static;
                MemberInfo[] ms = t.GetMembers(f);
                for (int i = 0; i < ms.Length; i++)
                {
                    PropertyInfo p = ms[i] as PropertyInfo;
                    if (p == null || p.GetIndexParameters().Length != 0)
                    {
                        continue;
                    }
                    string nt = p.PropertyType.Name;
                    if (nt.IndexOf("String", StringComparison.Ordinal) < 0
                        && nt.IndexOf("List", StringComparison.Ordinal) < 0
                        && nt.IndexOf("HashSet", StringComparison.Ordinal) < 0
                        && nt.IndexOf("Dictionary", StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }
                    object v;
                    try { v = p.GetValue(null); }
                    catch (Exception ex) { sb.AppendLine(p.Name + " -> error " + ex.Message); continue; }
                    sb.AppendLine();
                    sb.AppendLine(p.Name + "   (" + p.PropertyType.FullName + ")");
                    if (v == null)
                    {
                        sb.AppendLine("   (null)");
                        continue;
                    }
                    Volcado(sb, v);
                }
                string ruta = Path.Combine(MelonEnvironment.MelonLoaderDirectory,
                                           "colecciones-biblioteca.txt");
                File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false));
                MelonLogger.Msg("[DiagOrden] colecciones volcadas en " + ruta);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[DiagOrden] colecciones: " + ex);
            }
        }

        // Imprime hasta 40 elementos de lo que sea que se le pase, sin
        // conocer su tipo en tiempo de compilacion.
        private static void Volcado(StringBuilder sb, object v)
        {
            try
            {
                Type tv = v.GetType();
                PropertyInfo len = tv.GetProperty("Length") ?? tv.GetProperty("Count");
                PropertyInfo idx = tv.GetProperty("Item");
                if (len == null)
                {
                    sb.AppendLine("   " + v.ToString());
                    return;
                }
                int n = (int)len.GetValue(v);
                sb.AppendLine("   " + n.ToString() + " elemento(s)");
                if (idx == null || idx.GetIndexParameters().Length != 1
                    || idx.GetIndexParameters()[0].ParameterType != typeof(int))
                {
                    return;      // Dictionary/HashSet: no se puede indexar
                }
                for (int i = 0; i < n && i < 40; i++)
                {
                    object e;
                    try { e = idx.GetValue(v, new object[] { i }); }
                    catch (Exception) { break; }
                    sb.AppendLine("   [" + i.ToString() + "] "
                                  + (e == null ? "(null)" : e.ToString()));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("   error: " + ex.Message);
            }
        }

        public static void Volcar()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Diagnostico orden por dificultad ===");
            sb.AppendLine("fecha: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            try { Seccion(sb); } catch (Exception ex) { sb.AppendLine("seccion: " + ex); }
            try { SeccionesDelJuego(sb); } catch (Exception ex) { sb.AppendLine("secciones juego: " + ex); }
            try { Cache(sb); } catch (Exception ex) { sb.AppendLine("cache: " + ex); }
            try { AjustesCandidatos(sb); } catch (Exception ex) { sb.AppendLine("ajustes: " + ex); }

            string destino = Path.Combine(MelonEnvironment.MelonLoaderDirectory, "diagnostico-orden.txt");
            File.WriteAllText(destino, sb.ToString(), Encoding.UTF8);
            MelonLogger.Msg("[Orden] diagnostico escrito en " + destino);
        }

        // ------------------------------------------------------------ seccion
        private static void Seccion(StringBuilder sb)
        {
            sb.AppendLine("---------- TIPO SECCION ----------");
            Type t = Ofuscado.Tipo(TipoSeccion);
            if (t == null)
            {
                sb.AppendLine("no encontrado");
                return;
            }
            sb.AppendLine("interop: " + t.FullName);

            ConstructorInfo[] ctors = t.GetConstructors();
            sb.AppendLine("constructores:");
            for (int i = 0; i < ctors.Length; i++)
            {
                ParameterInfo[] ps = ctors[i].GetParameters();
                StringBuilder f = new StringBuilder("  .ctor(");
                for (int j = 0; j < ps.Length; j++)
                {
                    if (j > 0) { f.Append(", "); }
                    f.Append(ps[j].ParameterType.Name).Append(' ').Append(ps[j].Name);
                }
                sb.AppendLine(f.Append(')').ToString());
            }

            sb.AppendLine("miembros de instancia:");
            MemberInfo[] ms = t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            for (int i = 0; i < ms.Length; i++)
            {
                string real = Ofuscado.NombreReal(ms[i]);
                string tipo = null;
                if (ms[i] is PropertyInfo p) { tipo = p.PropertyType.Name; }
                else if (ms[i] is FieldInfo c) { tipo = c.FieldType.Name; }
                if (tipo == null) { continue; }
                sb.Append("  ").Append(tipo).Append("  ").Append(ms[i].Name);
                if (real != null && real != ms[i].Name) { sb.Append("   [real: ").Append(real).Append(']'); }
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        // Como son las secciones que construye el propio juego: es la unica
        // forma de saber que convenio siguen BeginIndex y LastIndex.
        private static void SeccionesDelJuego(StringBuilder sb)
        {
            sb.AppendLine("---------- SECCIONES DEL JUEGO ----------");
            Type t = Ofuscado.Tipo(TipoBiblioteca);
            Type tSec = Ofuscado.Tipo(TipoSeccion);
            if (t == null || tSec == null)
            {
                sb.AppendLine("tipos no encontrados");
                return;
            }
            Type tLista = typeof(Il2CppSystem.Collections.Generic.List<>).MakeGenericType(tSec);
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            MemberInfo[] ms = t.GetMembers(f);
            for (int i = 0; i < ms.Length; i++)
            {
                Type tipo = null;
                if (ms[i] is PropertyInfo p && p.GetIndexParameters().Length == 0) { tipo = p.PropertyType; }
                else if (ms[i] is FieldInfo c) { tipo = c.FieldType; }
                if (tipo != tLista)
                {
                    continue;
                }
                object v = Leer(ms[i]);
                sb.AppendLine("miembro: " + ms[i].Name + (v == null ? "  (null)" : ""));
                if (v != null)
                {
                    Detallar(sb, v);
                }
            }

            // Y las que hay dentro de la cache, por criterio.
            MemberInfo cache = null;
            for (int i = 0; i < ms.Length; i++)
            {
                Type tipo = null;
                if (ms[i] is PropertyInfo p2 && p2.GetIndexParameters().Length == 0) { tipo = p2.PropertyType; }
                else if (ms[i] is FieldInfo c2) { tipo = c2.FieldType; }
                if (tipo == null || !tipo.IsGenericType || !tipo.Name.StartsWith("Il2CppReferenceArray"))
                {
                    continue;
                }
                Type[] a = tipo.GetGenericArguments();
                if (a.Length == 1 && a[0] == tLista)
                {
                    cache = ms[i];
                    break;
                }
            }
            if (cache != null)
            {
                object arr = Leer(cache);
                if (arr != null)
                {
                    PropertyInfo len = arr.GetType().GetProperty("Length");
                    PropertyInfo item = arr.GetType().GetProperty("Item");
                    int n = (int)len.GetValue(arr);
                    sb.AppendLine("cache: " + cache.Name + "  longitud=" + n.ToString());
                    for (int i = 0; i < n; i++)
                    {
                        object lista = item.GetValue(arr, new object[] { i });
                        if (lista == null)
                        {
                            continue;
                        }
                        sb.AppendLine("  criterio " + i.ToString() + ":");
                        Detallar(sb, lista);
                    }
                }
            }
            sb.AppendLine();
        }

        private static void Detallar(StringBuilder sb, object lista)
        {
            PropertyInfo cuenta = lista.GetType().GetProperty("Count");
            PropertyInfo item = lista.GetType().GetProperty("Item");
            if (cuenta == null || item == null)
            {
                return;
            }
            int n = (int)cuenta.GetValue(lista);
            sb.AppendLine("    secciones=" + n.ToString());
            int max = n < 4 ? n : 4;
            for (int i = 0; i < max; i++)
            {
                object s = item.GetValue(lista, new object[] { i });
                if (s == null)
                {
                    continue;
                }
                sb.Append("    [").Append(i).Append("] ");
                PropertyInfo[] props = s.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.Instance);
                for (int j = 0; j < props.Length; j++)
                {
                    if (props[j].GetIndexParameters().Length != 0)
                    {
                        continue;
                    }
                    Type pt = props[j].PropertyType;
                    if (pt != typeof(int) && pt != typeof(string) && pt != typeof(bool))
                    {
                        continue;
                    }
                    try
                    {
                        sb.Append(props[j].Name).Append('=').Append(props[j].GetValue(s)).Append("  ");
                    }
                    catch (Exception)
                    {
                    }
                }
                // cuantas canciones lleva
                FieldInfo[] campos = s.GetType().GetFields(
                    BindingFlags.Public | BindingFlags.Instance);
                for (int j = 0; j < campos.Length; j++)
                {
                    if (!campos[j].FieldType.Name.StartsWith("List"))
                    {
                        continue;
                    }
                    object l = campos[j].GetValue(s);
                    if (l != null)
                    {
                        PropertyInfo c = l.GetType().GetProperty("Count");
                        if (c != null)
                        {
                            sb.Append("songs=").Append(c.GetValue(l)).Append("  ");
                        }
                    }
                }
                sb.AppendLine();
            }
        }

        // -------------------------------------------------------------- cache
        private static void Cache(StringBuilder sb)
        {
            sb.AppendLine("---------- CACHE DE SECCIONES ----------");
            Type t = Ofuscado.Tipo(TipoBiblioteca);
            if (t == null) { sb.AppendLine("biblioteca no encontrada"); return; }

            MemberInfo m = Ofuscado.Miembro(t, CacheSecciones);
            sb.AppendLine("campo cache (" + CacheSecciones + "): " + (m == null ? "NO encontrado" : m.Name));
            if (m != null)
            {
                object v = Leer(m);
                if (v == null)
                {
                    sb.AppendLine("  valor: null (aun no inicializado)");
                }
                else
                {
                    sb.AppendLine("  tipo: " + v.GetType().FullName);
                    PropertyInfo len = v.GetType().GetProperty("Length") ?? v.GetType().GetProperty("Count");
                    if (len != null)
                    {
                        sb.AppendLine("  longitud: " + len.GetValue(v));
                    }
                }
            }
            MemberInfo d = Ofuscado.Miembro(t, BanderaSucia);
            sb.AppendLine("bandera sucia (" + BanderaSucia + "): " + (d == null ? "NO encontrada" : d.Name));
            sb.AppendLine();
        }

        // ------------------------------------------------------------ ajustes
        // Vuelca cada miembro estatico de la clase de ajustes junto con todos
        // sus valores int, para identificar cual es sort_filter (max 26).
        private static void AjustesCandidatos(StringBuilder sb)
        {
            sb.AppendLine("---------- AJUSTES: CANDIDATOS A sort_filter ----------");
            Type t = Ofuscado.Tipo(TipoAjustes);
            if (t == null) { sb.AppendLine("no encontrado"); return; }
            sb.AppendLine("interop: " + t.FullName);

            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            MemberInfo[] ms = t.GetMembers(f);
            int mostrados = 0;

            for (int i = 0; i < ms.Length; i++)
            {
                if (!(ms[i] is PropertyInfo) && !(ms[i] is FieldInfo))
                {
                    continue;
                }
                object v;
                try { v = Leer(ms[i]); } catch (Exception) { continue; }
                if (v == null) { continue; }

                Type tv = v.GetType();
                // Solo interesan objetos-ajuste, no primitivas sueltas.
                if (tv.IsPrimitive || v is string) { continue; }

                string ints = ValoresInt(v);
                if (ints == null) { continue; }

                sb.Append("  ").Append(ms[i].Name).Append("  <").Append(tv.Name).Append(">  ")
                  .AppendLine(ints);
                mostrados++;
                if (mostrados > 120) { sb.AppendLine("  ... cortado"); break; }
            }
            sb.AppendLine();
            sb.AppendLine("Buscar arriba el que tenga un valor 26: ese es el maximo de sort_filter.");
        }

        // Concatena todas las propiedades int del objeto, con su nombre.
        private static string ValoresInt(object o)
        {
            StringBuilder sb = new StringBuilder();
            PropertyInfo[] props = o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].PropertyType != typeof(int) || props[i].GetIndexParameters().Length != 0)
                {
                    continue;
                }
                try
                {
                    object v = props[i].GetValue(o);
                    sb.Append(props[i].Name).Append('=').Append(v).Append("  ");
                }
                catch (Exception)
                {
                }
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        private static object Leer(MemberInfo m)
        {
            if (m is PropertyInfo p)
            {
                return p.GetIndexParameters().Length == 0 ? p.GetValue(null) : null;
            }
            if (m is FieldInfo c)
            {
                return c.GetValue(null);
            }
            return null;
        }
    }
}
