using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;

namespace CloneHeroMod
{
    // Anade "Difficulty" a (Hold) Sort List > Sort Options, agrupando las
    // canciones de diez en diez: "90-99 Difficulty", "80-89 Difficulty"...
    //
    // Tres piezas, y ninguna se puede localizar por nombre: Il2CppInterop solo
    // conserva el [ObfuscatedName] en los TIPOS, no en campos ni metodos. Todo
    // lo de dentro se identifica por forma o por contenido.
    //
    //   1. El array de nombres de orden (27 entradas) -> se le anade la nuestra.
    //   2. El ajuste sort_filter, cuyo maximo hay que subir de 26 a 27 o la
    //      opcion nueva queda inalcanzable.
    //   3. La cache de secciones: un List<Seccion>[] indexado por criterio. Si
    //      nuestro hueco esta lleno, el juego no intenta construirlo el mismo
    //      (su switch no conoce nuestro indice) y usa nuestra lista.
    public static class OrdenDificultad
    {
        public const string Nombre = "Difficulty";
        public const string SufijoCabecera = " Difficulty";
        public const string CabeceraSinValor = "No Difficulty";

        private const string TipoBiblioteca = "ʾʲʽʻʺʶʺʺʷʿʺ";
        private const string TipoAjustes = "ʹʺʽˁʽˁˀʼʶʷʼ";
        private const string TipoSeccion = "ˁʽʸʲʳʺʸʸˀʶʸ";

        private static PropertyInfo campoNombresOrden;
        private static MemberInfo campoCache;
        private static Type tipoSeccion;
        private static ConstructorInfo ctorSeccion;
        private static FieldInfo campoCancionesSeccion;
        private static PropertyInfo propCancionesSeccion;

        private static PropertyInfo campoInicioSeccion;
        private static int visiblesUltimaConstruccion = -1;
        private static int contadorRevision;
        private static int nuestroIndice = -1;
        private static bool instalado;
        private static bool fallado;

        private static readonly Dictionary<string, int> cacheValores =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static bool Instalado { get { return instalado; } }

        // ------------------------------------------------------------ instalar
        public static void Instalar()
        {
            if (instalado || fallado)
            {
                return;
            }
            try
            {
                if (!ResolverNombresOrden())
                {
                    Fallo("no se localizo la lista de criterios de orden");
                    return;
                }
                if (!ResolverSeccion())
                {
                    Fallo("no se localizo el tipo Seccion");
                    return;
                }
                int longitudPrevia = ArrayNombres().Length;
                if (!AnadirCriterio(out nuestroIndice))
                {
                    Fallo("no se pudo anadir el criterio");
                    return;
                }
                SubirMaximoAjuste(longitudPrevia - 1, nuestroIndice);
                ResolverCache();

                instalado = true;
                MelonLogger.Msg("[Orden] instalado en el indice " + nuestroIndice.ToString());
            }
            catch (Exception ex)
            {
                Fallo(ex.ToString());
            }
        }

        private static void Fallo(string msg)
        {
            fallado = true;
            MelonLogger.Warning("[Orden] " + msg);
        }

        // ---------------------------------------------------------- resolucion
        private static Il2CppStringArray ArrayNombres()
        {
            return campoNombresOrden.GetValue(null) as Il2CppStringArray;
        }

        // Se identifica por contenido: es el array que trae los criterios de
        // orden. "Intensity - Lead Guitar" no aparece en ningun otro.
        private static bool ResolverNombresOrden()
        {
            Type t = Ofuscado.Tipo(TipoBiblioteca);
            if (t == null)
            {
                return false;
            }
            PropertyInfo[] props = t.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].PropertyType != typeof(Il2CppStringArray))
                {
                    continue;
                }
                Il2CppStringArray arr;
                try { arr = props[i].GetValue(null) as Il2CppStringArray; }
                catch (Exception) { continue; }
                if (arr == null)
                {
                    continue;
                }
                bool intensidad = false;
                bool jugadas = false;
                for (int j = 0; j < arr.Length; j++)
                {
                    if (arr[j] == "Intensity - Lead Guitar") { intensidad = true; }
                    else if (arr[j] == "Play Count") { jugadas = true; }
                }
                if (intensidad && jugadas)
                {
                    campoNombresOrden = props[i];
                    MelonLogger.Msg("[Orden] criterios: " + props[i].Name
                                    + " (" + arr.Length.ToString() + " entradas)");
                    return true;
                }
            }
            return false;
        }

        private static bool ResolverSeccion()
        {
            tipoSeccion = Ofuscado.Tipo(TipoSeccion);
            if (tipoSeccion == null)
            {
                return false;
            }
            ConstructorInfo[] ctors = tipoSeccion.GetConstructors();
            for (int i = 0; i < ctors.Length; i++)
            {
                ParameterInfo[] ps = ctors[i].GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                {
                    ctorSeccion = ctors[i];
                    break;
                }
            }
            if (ctorSeccion == null)
            {
                return false;
            }
            // La lista de canciones de la seccion: unico List<SongEntry> suyo.
            Type esperado = typeof(Il2CppSystem.Collections.Generic.List<>)
                .MakeGenericType(typeof(Il2Cpp.SongEntry));
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo[] campos = tipoSeccion.GetFields(f);
            for (int i = 0; i < campos.Length; i++)
            {
                if (campos[i].FieldType == esperado)
                {
                    campoCancionesSeccion = campos[i];
                    break;
                }
            }
            if (campoCancionesSeccion == null)
            {
                PropertyInfo[] props = tipoSeccion.GetProperties(f);
                for (int i = 0; i < props.Length; i++)
                {
                    if (props[i].PropertyType == esperado)
                    {
                        propCancionesSeccion = props[i];
                        break;
                    }
                }
            }
            return campoCancionesSeccion != null || propCancionesSeccion != null;
        }

        // La cache es el unico array estatico de List<Seccion> de la biblioteca.
        private static void ResolverCache()
        {
            Type t = Ofuscado.Tipo(TipoBiblioteca);
            if (t == null)
            {
                return;
            }
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            MemberInfo[] ms = t.GetMembers(f);
            for (int i = 0; i < ms.Length; i++)
            {
                Type tipo = TipoDe(ms[i]);
                if (tipo == null || !tipo.IsGenericType)
                {
                    continue;
                }
                if (!tipo.Name.StartsWith("Il2CppReferenceArray"))
                {
                    continue;
                }
                Type[] args = tipo.GetGenericArguments();
                if (args.Length != 1 || !args[0].IsGenericType)
                {
                    continue;
                }
                if (!args[0].Name.StartsWith("List"))
                {
                    continue;
                }
                Type[] elem = args[0].GetGenericArguments();
                if (elem.Length == 1 && elem[0] == tipoSeccion)
                {
                    campoCache = ms[i];
                    MelonLogger.Msg("[Orden] cache de secciones: " + ms[i].Name);
                    return;
                }
            }
            MelonLogger.Warning("[Orden] cache de secciones no localizada todavia");
        }

        private static Type TipoDe(MemberInfo m)
        {
            if (m is FieldInfo f) { return f.FieldType; }
            if (m is PropertyInfo p) { return p.GetIndexParameters().Length == 0 ? p.PropertyType : null; }
            return null;
        }

        private static object LeerEstatico(MemberInfo m)
        {
            if (m is FieldInfo f) { return f.GetValue(null); }
            if (m is PropertyInfo p) { return p.GetValue(null); }
            return null;
        }

        private static void EscribirEstatico(MemberInfo m, object v)
        {
            if (m is FieldInfo f) { f.SetValue(null, v); }
            else if (m is PropertyInfo p) { p.SetValue(null, v); }
        }

        // ------------------------------------------------------------- alta --
        private static bool AnadirCriterio(out int indice)
        {
            indice = -1;
            Il2CppStringArray actual = ArrayNombres();
            if (actual == null)
            {
                return false;
            }
            for (int i = 0; i < actual.Length; i++)
            {
                if (actual[i] == Nombre)
                {
                    indice = i;
                    return true;      // ya estaba
                }
            }
            Il2CppStringArray nueva = new Il2CppStringArray(actual.Length + 1);
            for (int i = 0; i < actual.Length; i++)
            {
                nueva[i] = actual[i];
            }
            nueva[actual.Length] = Nombre;
            campoNombresOrden.SetValue(null, nueva);
            indice = actual.Length;
            return true;
        }

        // El ajuste sort_filter limita que criterios puede elegir el usuario.
        // Se identifica por su maximo, que coincide con el ultimo indice valido
        // antes de que anadamos el nuestro.
        private static void SubirMaximoAjuste(int maximoViejo, int maximoNuevo)
        {
            try
            {
                Type t = Ofuscado.Tipo(TipoAjustes);
                if (t == null)
                {
                    return;
                }
                BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                MemberInfo[] ms = t.GetMembers(f);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (!(ms[i] is FieldInfo) && !(ms[i] is PropertyInfo))
                    {
                        continue;
                    }
                    object ajuste;
                    try { ajuste = LeerEstatico(ms[i]); } catch (Exception) { continue; }
                    if (ajuste == null || ajuste is string || ajuste.GetType().IsPrimitive)
                    {
                        continue;
                    }
                    PropertyInfo max = PropiedadMaximo(ajuste, maximoViejo);
                    if (max == null)
                    {
                        continue;
                    }
                    max.SetValue(ajuste, maximoNuevo);
                    ajusteOrden = ajuste;
                    MelonLogger.Msg("[Orden] maximo de sort_filter: " + maximoViejo.ToString()
                                    + " -> " + maximoNuevo.ToString()
                                    + " (" + ms[i].Name + "." + max.Name + ")");
                    return;
                }
                MelonLogger.Warning("[Orden] no se localizo el ajuste sort_filter; "
                    + "el criterio nuevo puede no ser seleccionable");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Orden] maximo: " + ex.Message);
            }
        }

        // En estos objetos de ajuste el patron es prop_T_0=actual, T_1=maximo,
        // T_2=minimo, T_3=por defecto. Se busca la que valga justo el maximo
        // viejo y ademas sea escribible.
        private static PropertyInfo PropiedadMaximo(object ajuste, int maximoViejo)
        {
            PropertyInfo[] props = ajuste.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo candidata = null;
            int coincidencias = 0;
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].PropertyType != typeof(int)
                    || props[i].GetIndexParameters().Length != 0
                    || !props[i].CanWrite)
                {
                    continue;
                }
                object v;
                try { v = props[i].GetValue(ajuste); } catch (Exception) { continue; }
                if (v is int n && n == maximoViejo)
                {
                    candidata = props[i];
                    coincidencias++;
                }
            }
            // Si varias propiedades valen lo mismo no se puede distinguir cual
            // es el maximo: mejor no tocar nada que romper otro ajuste.
            return coincidencias == 1 ? candidata : null;
        }

        // -------------------------------------------------------------- tick -
        // Mantiene lleno nuestro hueco de la cache. Sale en dos comprobaciones
        // mientras no haya nada que hacer.
        public static void Tick()
        {
            if (!instalado || nuestroIndice < 0)
            {
                return;
            }
            try
            {
                if (campoCache == null)
                {
                    ResolverCache();
                    if (campoCache == null)
                    {
                        return;
                    }
                }
                object bruto = LeerEstatico(campoCache);
                if (bruto == null)
                {
                    return;      // la cache aun no existe
                }
                // No se puede tipar el array en tiempo de compilacion: su
                // elemento es el tipo Seccion, que solo conocemos como Type.
                // Se trabaja por reflexion.
                PropertyInfo len = bruto.GetType().GetProperty("Length");
                if (len == null)
                {
                    return;
                }
                int longitud = (int)len.GetValue(bruto);
                if (nuestroIndice >= longitud)
                {
                    AmpliarCache(bruto, longitud);
                    return;
                }
                object actual = ElementoCache(bruto, nuestroIndice);
                if (actual != null)
                {
                    // Ya esta construido, pero puede haberse quedado obsoleto:
                    // el juego marca song.filtered al cambiar los filtros y no
                    // siempre invalida esta cache. Se comprueba de vez en
                    // cuando (no en cada fotograma: son miles de canciones) y
                    // se fuerza la reconstruccion si el conjunto visible
                    // cambio. Sin esto, ordenando por Difficulty los filtros
                    // parecen no hacer nada.
                    contadorRevision++;
                    if (contadorRevision < 20)
                    {
                        return;
                    }
                    contadorRevision = 0;
                    if (ContarVisibles() == visiblesUltimaConstruccion)
                    {
                        return;
                    }
                    PonerElementoCache(bruto, nuestroIndice, null);
                    actual = null;
                }

                // Primero se intenta con el agrupador del propio juego: asi las
                // secciones nacen dentro de su tuberia y el filtro y el
                // redibujado funcionan solos. Solo si no esta disponible se cae
                // al metodo manual, que construye bien pero no refresca la
                // pantalla al filtrar.
                // SOLO si el criterio activo es el nuestro. El agrupador nativo
                // escribe en el hueco del criterio ACTIVO, asi que llamarlo con
                // otro seleccionado le machaca sus secciones (se comprobo:
                // estando en el criterio 5, nos dejo cache[5] con las nuestras).
                if (EsNuestroCriterioActivo() && AgruparNativo())
                {
                    return;
                }
                object secciones = Construir();
                if (secciones != null)
                {
                    PonerElementoCache(bruto, nuestroIndice, secciones);
                }
            }
            catch (Exception ex)
            {
                fallado = true;
                instalado = false;
                MelonLogger.Error("[Orden] tick: " + ex);
            }
        }

        // ------------------------------------------------- via nativa --------
        // El juego tiene un agrupador generico:
        //
        //   static void (Func<SongEntry,string> cabecera,
        //                Func<SongEntry,string> claveOrden, bool)
        //
        // Es lo que usa para todos sus criterios de orden por texto (su cache
        // de lambdas tiene ~15 pares). Llamarlo con nuestros dos selectores
        // construye las secciones DENTRO de su tuberia, asi que los indices,
        // la lista activa, el filtro y el redibujado los gestiona el.
        //
        // Es la alternativa a rellenar la cache por fuera, que es lo que hacia
        // que los filtros no se reflejaran en pantalla.
        private static MethodInfo metodoAgrupar;
        private static object funcCabecera;
        private static object funcClave;
        private static bool agrupadorFallado;
        private static object ajusteOrden;
        private static PropertyInfo propOrdenActual;

        private static bool ResolverAgrupador()
        {
            if (metodoAgrupar != null)
            {
                return true;
            }
            if (agrupadorFallado)
            {
                return false;
            }
            agrupadorFallado = true;
            try
            {
                Type t = Ofuscado.Tipo(TipoBiblioteca);
                Type tFunc = typeof(Il2CppSystem.Func<,>)
                    .MakeGenericType(typeof(Il2Cpp.SongEntry), typeof(string));
                MethodInfo[] ms = t.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].ReturnType != typeof(void))
                    {
                        continue;
                    }
                    ParameterInfo[] ps = ms[i].GetParameters();
                    if (ps.Length == 3
                        && ps[0].ParameterType == tFunc
                        && ps[1].ParameterType == tFunc
                        && ps[2].ParameterType == typeof(bool))
                    {
                        metodoAgrupar = ms[i];
                        agrupadorFallado = false;
                        MelonLogger.Msg("[Orden] agrupador nativo: " + ms[i].Name);
                        return true;
                    }
                }
                MelonLogger.Warning("[Orden] no se localizo el agrupador nativo");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Orden] agrupador: " + ex.Message);
            }
            return false;
        }

        // Cabecera visible de la seccion a la que pertenece la cancion.
        private static string CabeceraDe(Il2Cpp.SongEntry s)
        {
            int v = ValorDe(s);
            return v < 0 ? CabeceraSinValor : Cabecera(Grupo(v));
        }

        // Clave de ordenacion. El juego ordena por texto, asi que se devuelve
        // un numero con ceros a la izquierda e invertido, para que de mas
        // dificil a menos y "No Difficulty" quede al final.
        private static string ClaveDe(Il2Cpp.SongEntry s)
        {
            int v = ValorDe(s);
            if (v < 0)
            {
                return "999";
            }
            int inverso = 100 - Grupo(v);
            return inverso.ToString("000");
        }

        private static bool AgruparNativo()
        {
            try
            {
                if (!ResolverAgrupador())
                {
                    return false;
                }
                if (funcCabecera == null)
                {
                    funcCabecera = DelegateSupport.ConvertDelegate<
                        Il2CppSystem.Func<Il2Cpp.SongEntry, string>>(
                            new Func<Il2Cpp.SongEntry, string>(CabeceraDe));
                    funcClave = DelegateSupport.ConvertDelegate<
                        Il2CppSystem.Func<Il2Cpp.SongEntry, string>>(
                            new Func<Il2Cpp.SongEntry, string>(ClaveDe));
                }
                // ORDEN DE LOS PARAMETROS: el primero es la clave por la que se
                // agrupa y se ordena; el segundo es la cabecera que se ve.
                // (Comprobado al reves: pasando la cabecera primero, las
                // secciones salian ordenadas alfabeticamente -"0-9", "100",
                // "10-19"- y se mostraba la clave.)
                metodoAgrupar.Invoke(null, new object[] { funcClave, funcCabecera, true });
                visiblesUltimaConstruccion = ContarVisibles();
                RegistrarResultado();
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Orden] agrupar nativo: " + ex);
                return false;
            }
        }

        // Deja en el log las primeras cabeceras que quedaron, para poder
        // comprobar el orden sin tener que mirar la pantalla.
        private static void RegistrarResultado()
        {
            try
            {
                object bruto = campoCache != null ? LeerEstatico(campoCache) : null;
                if (bruto == null)
                {
                    return;
                }
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("[Orden] nativo -> ");

                // 1. En que hueco de la cache quedo algo
                PropertyInfo len = bruto.GetType().GetProperty("Length");
                int nc = (int)len.GetValue(bruto);
                sb.Append("cache[");
                for (int i = 0; i < nc; i++)
                {
                    object l = ElementoCache(bruto, i);
                    if (l == null)
                    {
                        continue;
                    }
                    PropertyInfo c = l.GetType().GetProperty("Count");
                    sb.Append(i).Append(':').Append(c.GetValue(l)).Append(' ');
                }
                sb.Append("]  ");

                // 2. Que criterio esta activo segun el ajuste
                sb.Append("ajuste=").Append(ValoresAjuste()).Append("  ");

                // 3. Cabeceras de la lista activa, para ver el orden resultante
                Detallar(sb);

                MelonLogger.Msg(sb.ToString());
            }
            catch (Exception)
            {
            }
        }

        // El criterio de orden seleccionado ahora mismo. En estos objetos de
        // ajuste el valor actual vive en "prop_T_0" (comprobado: valia 5 estando
        // el juego ordenado por el criterio 5, con prop_T_1=27 de maximo).
        private static bool EsNuestroCriterioActivo()
        {
            try
            {
                if (ajusteOrden == null || nuestroIndice < 0)
                {
                    return false;
                }
                if (propOrdenActual == null)
                {
                    propOrdenActual = ajusteOrden.GetType().GetProperty("prop_T_0",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (propOrdenActual == null || propOrdenActual.PropertyType != typeof(int))
                    {
                        propOrdenActual = null;
                        return false;
                    }
                }
                object v = propOrdenActual.GetValue(ajusteOrden);
                return v is int n && n == nuestroIndice;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------ parche de refresco -
        // El juego invalida la cache de secciones y la reconstruye a traves de
        // un switch sobre el criterio de orden. El nuestro no esta en ese
        // switch, asi que no construye nada Y, sobre todo, no repinta: por eso
        // los filtros no se veian al ordenar por Difficulty.
        //
        // La sonda localizo la secuencia que corre justo antes de la
        // invalidacion, identica al aplicar y al quitar un filtro:
        //
        //   Void_5  ->  Void_String_0  ->  Void_6
        //
        // Enganchandonos al final de esa secuencia construimos las secciones
        // dentro de su propio ciclo de refresco.
        private static readonly string[] MetodosRefresco =
        {
            "Method_Public_Static_Void_6",
            "Method_Public_Static_Void_5"
        };

        public static void InstalarParcheRefresco(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type t = Ofuscado.Tipo(TipoBiblioteca);
                if (t == null)
                {
                    return;
                }
                MethodInfo postfix = typeof(OrdenDificultad).GetMethod(
                    "TrasRefresco", BindingFlags.NonPublic | BindingFlags.Static);
                int n = 0;
                for (int i = 0; i < MetodosRefresco.Length; i++)
                {
                    MethodInfo m = t.GetMethod(MetodosRefresco[i],
                        BindingFlags.Public | BindingFlags.Static);
                    if (m == null)
                    {
                        continue;
                    }
                    try
                    {
                        harmony.Patch(m, null, new HarmonyLib.HarmonyMethod(postfix));
                        n++;
                    }
                    catch (Exception)
                    {
                    }
                }
                MelonLogger.Msg("[Orden] parche de refresco: " + n.ToString() + " metodo(s)");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Orden] parche de refresco: " + ex.Message);
            }
        }

        private static void TrasRefresco()
        {
            try
            {
                if (!instalado || !EsNuestroCriterioActivo())
                {
                    return;
                }
                AgruparNativo();
            }
            catch (Exception)
            {
            }
        }

        // Acceso al criterio de orden seleccionado, para la sonda y para el
        // posible "empujon" que fuerce al juego a reconstruir.
        public static bool LeerCriterioActivo(out int valor)
        {
            valor = -1;
            try
            {
                if (ajusteOrden == null)
                {
                    return false;
                }
                if (propOrdenActual == null)
                {
                    propOrdenActual = ajusteOrden.GetType().GetProperty("prop_T_0",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (propOrdenActual == null)
                    {
                        return false;
                    }
                }
                object v = propOrdenActual.GetValue(ajusteOrden);
                if (v is int n)
                {
                    valor = n;
                    return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        public static void EscribirCriterioActivo(int valor)
        {
            try
            {
                if (ajusteOrden != null && propOrdenActual != null)
                {
                    propOrdenActual.SetValue(ajusteOrden, valor);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Orden] escribir criterio: " + ex.Message);
            }
        }

        // Los cuatro int del ajuste de orden: actual, maximo, minimo, defecto.
        private static string ValoresAjuste()
        {
            try
            {
                if (ajusteOrden == null)
                {
                    return "?";
                }
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                PropertyInfo[] props = ajusteOrden.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < props.Length; i++)
                {
                    if (props[i].PropertyType != typeof(int)
                        || props[i].GetIndexParameters().Length != 0)
                    {
                        continue;
                    }
                    sb.Append(props[i].Name).Append('=').Append(props[i].GetValue(ajusteOrden)).Append(' ');
                }
                return sb.ToString();
            }
            catch (Exception)
            {
                return "?";
            }
        }

        // Cabeceras de la lista de secciones que el juego esta mostrando.
        private static void Detallar(System.Text.StringBuilder sb)
        {
            try
            {
                Type t = Ofuscado.Tipo(TipoBiblioteca);
                Type esperado = typeof(Il2CppSystem.Collections.Generic.List<>)
                    .MakeGenericType(tipoSeccion);
                PropertyInfo[] props = t.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                for (int i = 0; i < props.Length; i++)
                {
                    if (props[i].PropertyType != esperado)
                    {
                        continue;
                    }
                    object lista = props[i].GetValue(null);
                    if (lista == null)
                    {
                        continue;
                    }
                    PropertyInfo cuenta = lista.GetType().GetProperty("Count");
                    PropertyInfo item = lista.GetType().GetProperty("Item");
                    int n = (int)cuenta.GetValue(lista);
                    sb.Append("activa(").Append(props[i].Name).Append(")=").Append(n).Append(": ");
                    int max = n < 5 ? n : 5;
                    for (int j = 0; j < max; j++)
                    {
                        object s = item.GetValue(lista, new object[] { j });
                        if (s == null)
                        {
                            continue;
                        }
                        PropertyInfo p = s.GetType().GetProperty("prop_String_0");
                        sb.Append(p != null ? p.GetValue(s) : "?").Append(" | ");
                    }
                    return;
                }
            }
            catch (Exception)
            {
            }
        }

        // Cuantas canciones sobreviven al filtro actual.
        private static int ContarVisibles()
        {
            var canciones = ListaCanciones();
            if (canciones == null)
            {
                return -1;
            }
            int n = 0;
            for (int i = 0; i < canciones.Count; i++)
            {
                Il2Cpp.SongEntry s = canciones[i];
                if (s != null && !s.filtered)
                {
                    n++;
                }
            }
            return n;
        }

        // Huella de que huecos de la cache estan llenos, para detectar cuando
        // el juego reconstruye.
        private static string EstadoCache(object arr, int longitud)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < longitud; i++)
            {
                sb.Append(ElementoCache(arr, i) != null ? '#' : '.');
            }
            return sb.ToString();
        }

        private static object ElementoCache(object arr, int i)
        {
            PropertyInfo idx = arr.GetType().GetProperty("Item");
            return idx != null ? idx.GetValue(arr, new object[] { i }) : null;
        }

        private static void PonerElementoCache(object arr, int i, object v)
        {
            PropertyInfo idx = arr.GetType().GetProperty("Item");
            if (idx != null)
            {
                idx.SetValue(arr, v, new object[] { i });
            }
        }

        // Si el array se creo con el numero de criterios de antes, nuestro
        // indice se sale. Se sustituye por uno mas grande conservando lo que
        // hubiera dentro.
        private static void AmpliarCache(object viejo, int longitud)
        {
            try
            {
                Type tipoArr = viejo.GetType();
                object nuevo = Activator.CreateInstance(tipoArr, new object[] { nuestroIndice + 1 });
                for (int i = 0; i < longitud; i++)
                {
                    PonerElementoCache(nuevo, i, ElementoCache(viejo, i));
                }
                EscribirEstatico(campoCache, nuevo);
                MelonLogger.Msg("[Orden] cache ampliada de " + longitud.ToString()
                                + " a " + (nuestroIndice + 1).ToString());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Orden] no se pudo ampliar la cache: " + ex.Message);
            }
        }

        // --------------------------------------------------------- secciones -
        private static object Construir()
        {
            var canciones = ListaCanciones();
            if (canciones == null || canciones.Count == 0)
            {
                return null;
            }

            List<Il2Cpp.SongEntry> conValor = new List<Il2Cpp.SongEntry>();
            List<Il2Cpp.SongEntry> sinValor = new List<Il2Cpp.SongEntry>();
            for (int i = 0; i < canciones.Count; i++)
            {
                Il2Cpp.SongEntry s = canciones[i];
                if (s == null || s.filtered)
                {
                    continue;      // el juego la ha descartado por el filtro
                }
                if (ValorDe(s) >= 0) { conValor.Add(s); } else { sinValor.Add(s); }
            }
            visiblesUltimaConstruccion = conValor.Count + sinValor.Count;

            conValor.Sort(delegate (Il2Cpp.SongEntry a, Il2Cpp.SongEntry b)
            {
                int c = ValorDe(b).CompareTo(ValorDe(a));   // de mas dificil a menos
                return c;
            });

            // Se crea una lista nueva en cada reconstruccion. Reutilizar la
            // misma y vaciarla en el sitio parecia mejor idea (la referencia
            // que guarda el juego seguiria siendo valida), pero en la practica
            // le corrompe el estado: dejaba el filtro bloqueado y la lista de
            // canciones vacia al volver a entrar. Ver README §"lo que no
            // funciono".
            Type tipoLista = typeof(Il2CppSystem.Collections.Generic.List<>).MakeGenericType(tipoSeccion);
            object resultado = Activator.CreateInstance(tipoLista);
            MethodInfo add = tipoLista.GetMethod("Add");

            object seccionActual = null;
            int grupoActual = -1;
            for (int i = 0; i < conValor.Count; i++)
            {
                int grupo = Grupo(ValorDe(conValor[i]));
                if (seccionActual == null || grupo != grupoActual)
                {
                    grupoActual = grupo;
                    seccionActual = NuevaSeccion(Cabecera(grupo));
                    add.Invoke(resultado, new object[] { seccionActual });
                }
                AnadirCancion(seccionActual, conValor[i]);
            }
            if (sinValor.Count > 0)
            {
                object s = NuevaSeccion(CabeceraSinValor);
                for (int i = 0; i < sinValor.Count; i++)
                {
                    AnadirCancion(s, sinValor[i]);
                }
                add.Invoke(resultado, new object[] { s });
            }

            // Sin esto la lista sale VACIA: cada Seccion lleva un BeginIndex y
            // un LastIndex que indexan la lista plana de canciones, y recien
            // construida valen cero. La biblioteca tiene un metodo que los
            // recalcula; es el mismo que usa el juego con sus propias secciones.
            Indexar(resultado);
            MelonLogger.Msg("[Orden] secciones construidas: con valor="
                + conValor.Count.ToString() + " sin valor=" + sinValor.Count.ToString());
            return resultado;
        }

        // Localiza y llama al metodo que numera las secciones. Se identifica por
        // firma: es el unico "static void (List<Seccion>)" de la biblioteca.
        // Numera las secciones igual que hace el juego con las suyas.
        //
        // El convenio, deducido observando las secciones nativas: los indices
        // son acumulativos sobre una lista PLANA que incluye las cabeceras.
        // Cada seccion ocupa 1 (su cabecera) + sus canciones. Ejemplo real:
        //
        //   [0] Begin=0   Last=1    (1 cancion)
        //   [1] Begin=2   Last=16   (14 canciones)
        //   [2] Begin=17  Last=19   (2 canciones)
        //
        // Solo hay que fijar BeginIndex: LastIndex se calcula solo como
        // Begin + canciones. Si se dejan a cero, la lista sale VACIA.
        private static void Indexar(object listaSecciones)
        {
            try
            {
                PropertyInfo cuenta = listaSecciones.GetType().GetProperty("Count");
                PropertyInfo item = listaSecciones.GetType().GetProperty("Item");
                if (cuenta == null || item == null)
                {
                    return;
                }
                int n = (int)cuenta.GetValue(listaSecciones);
                if (n == 0)
                {
                    return;
                }

                if (campoInicioSeccion == null)
                {
                    campoInicioSeccion = CampoInicio(tipoSeccion);
                    if (campoInicioSeccion == null)
                    {
                        MelonLogger.Warning("[Orden] no se localizo BeginIndex; "
                            + "la lista saldra vacia");
                        return;
                    }
                    MelonLogger.Msg("[Orden] BeginIndex: " + campoInicioSeccion.Name);
                }

                int inicio = 0;
                for (int i = 0; i < n; i++)
                {
                    object s = item.GetValue(listaSecciones, new object[] { i });
                    if (s == null)
                    {
                        continue;
                    }
                    campoInicioSeccion.SetValue(s, inicio);
                    inicio += 1 + CancionesDe(s);
                }
                MelonLogger.Msg("[Orden] indexado: " + n.ToString()
                    + " secciones, " + inicio.ToString() + " filas");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Orden] indexar: " + ex.Message);
            }
        }

        // Il2CppInterop NO genera campos de instancia: expone cada campo del
        // juego como una propiedad, y las distingue por prefijo en el nombre.
        //
        //   field_*  -> era un CAMPO en el juego (tiene almacenamiento)
        //   prop_*   -> era una PROPIEDAD (puede ser calculada)
        //
        // El tipo Seccion tiene tres propiedades int: field_Private_Int32_0
        // (BeginIndex, con almacenamiento), prop_Int32_0 (su getter) y
        // prop_Int32_1 (LastIndex, calculado). La buena es la del prefijo
        // field_, que ademas es unica.
        private static PropertyInfo CampoInicio(Type t)
        {
            PropertyInfo[] props = t.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            PropertyInfo encontrado = null;
            int n = 0;
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].PropertyType == typeof(int)
                    && props[i].CanWrite
                    && props[i].GetIndexParameters().Length == 0
                    && props[i].Name.StartsWith("field_"))
                {
                    encontrado = props[i];
                    n++;
                }
            }
            return n == 1 ? encontrado : null;
        }

        private static int CancionesDe(object seccion)
        {
            object lista = campoCancionesSeccion != null
                ? campoCancionesSeccion.GetValue(seccion)
                : propCancionesSeccion.GetValue(seccion);
            if (lista == null)
            {
                return 0;
            }
            PropertyInfo c = lista.GetType().GetProperty("Count");
            return c != null ? (int)c.GetValue(lista) : 0;
        }

        private static object NuevaSeccion(string cabecera)
        {
            return ctorSeccion.Invoke(new object[] { cabecera });
        }

        private static void AnadirCancion(object seccion, Il2Cpp.SongEntry cancion)
        {
            object lista = campoCancionesSeccion != null
                ? campoCancionesSeccion.GetValue(seccion)
                : propCancionesSeccion.GetValue(seccion);
            if (lista == null)
            {
                return;
            }
            MethodInfo add = lista.GetType().GetMethod("Add");
            if (add != null)
            {
                add.Invoke(lista, new object[] { cancion });
            }
        }

        // Agrupacion de diez en diez. El 100 va en su propia seccion.
        private static int Grupo(int valor)
        {
            return valor >= 100 ? 100 : valor / 10 * 10;
        }

        private static string Cabecera(int grupo)
        {
            if (grupo >= 100)
            {
                return "100" + SufijoCabecera;
            }
            return grupo.ToString() + "-" + (grupo + 9).ToString() + SufijoCabecera;
        }

        private static int ValorDe(Il2Cpp.SongEntry cancion)
        {
            try
            {
                if (cancion == null || cancion.isEnc)
                {
                    return -1;
                }
                string ruta = cancion.IniPath;
                if (string.IsNullOrEmpty(ruta))
                {
                    return -1;
                }
                if (!cacheValores.TryGetValue(ruta, out int v))
                {
                    v = Dificultad.LeerIni(ruta);
                    cacheValores[ruta] = v;
                }
                return v;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        // Tras recalcular hay que tirar lo cacheado para que se reconstruya.
        public static void LimpiarCache()
        {
            cacheValores.Clear();
            try
            {
                if (campoCache != null && nuestroIndice >= 0)
                {
                    object bruto = LeerEstatico(campoCache);
                    if (bruto != null)
                    {
                        PropertyInfo len = bruto.GetType().GetProperty("Length");
                        if (len != null && nuestroIndice < (int)len.GetValue(bruto))
                        {
                            PonerElementoCache(bruto, nuestroIndice, null);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static Il2CppSystem.Collections.Generic.List<Il2Cpp.SongEntry> ListaCanciones()
        {
            Type t = Ofuscado.Tipo(TipoBiblioteca);
            if (t == null)
            {
                return null;
            }
            Type esperado = typeof(Il2CppSystem.Collections.Generic.List<>)
                .MakeGenericType(typeof(Il2Cpp.SongEntry));
            Il2CppSystem.Collections.Generic.List<Il2Cpp.SongEntry> mejor = null;
            PropertyInfo[] props = t.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].PropertyType != esperado)
                {
                    continue;
                }
                try
                {
                    var l = props[i].GetValue(null)
                        as Il2CppSystem.Collections.Generic.List<Il2Cpp.SongEntry>;
                    if (l != null && (mejor == null || l.Count > mejor.Count))
                    {
                        mejor = l;
                    }
                }
                catch (Exception)
                {
                }
            }
            return mejor;
        }
    }
}
