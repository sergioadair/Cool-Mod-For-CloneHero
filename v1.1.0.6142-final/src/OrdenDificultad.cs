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
        private static PropertyInfo propLongitud;
        private static PropertyInfo propElemento;
        private static int nuestroIndice = -1;
        private static bool instalado;
        private static bool fallado;

        private static readonly Dictionary<string, int> cacheValores =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static bool Instalado { get { return instalado; } }

        // ------------------------------------------------------------ instalar
        // Mismo problema que en FiltroFavoritos: al reescanear la biblioteca el
        // juego reconstruye sus arrays estaticos y nuestra entrada desaparece.
        // Aqui ademas habiamos subido el maximo del ajuste sort_filter para que
        // se pudiera seleccionar, asi que el criterio guardado sigue apuntando
        // a un indice que ya no existe y la lista sale vacia.
        //
        // Se comprueba cada pocos segundos en los menus y se reinstala si hace
        // falta. Leer un array estatico y comparar una cadena.
        public static void Verificar()
        {
            if (!instalado || fallado)
            {
                return;
            }
            try
            {
                Il2CppStringArray actual = ArrayNombres();
                if (actual == null)
                {
                    return;
                }
                for (int i = 0; i < actual.Length; i++)
                {
                    if (actual[i] == Nombre)
                    {
                        return;      // sigue ahi
                    }
                }
                instalado = false;
                nuestroIndice = -1;
                int longitudPrevia = actual.Length;
                if (AnadirCriterio(out nuestroIndice))
                {
                    SubirMaximoAjuste(longitudPrevia - 1, nuestroIndice);
                    ResolverCache();
                    LimpiarCache();
                    instalado = true;
                    MelonLogger.Msg("[Orden] la lista se reconstruyo; criterio reinstalado"
                                    + " en el indice " + nuestroIndice.ToString());
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Orden] verificar: " + ex.Message);
            }
        }

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
            // RENDIMIENTO: esto corre en cada fotograma, tambien mientras se
            // juega una cancion. Si nuestro criterio no es el activo no hay
            // nada que mantener, y esta comprobacion es una sola lectura sobre
            // un PropertyInfo ya resuelto.
            if (!EsNuestroCriterioActivo())
            {
                return;
            }
            // Y solo si estamos en la lista de canciones. Durante una cancion
            // no hay nada que mantener, y el recuento de visibles recorre miles
            // de entradas: era la causa de los tirones en pleno gameplay.
            if (!EtiquetaDificultad.HaySeleccion)
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
                    return;
                }
                // No se puede tipar el array en tiempo de compilacion: su
                // elemento es el tipo Seccion, que solo conocemos como Type.
                // Se trabaja por reflexion, pero los PropertyInfo se resuelven
                // UNA vez: antes se hacia GetProperty en cada fotograma, que es
                // una busqueda por nombre y sale cara.
                if (propLongitud == null || propElemento == null)
                {
                    Type ta = bruto.GetType();
                    propLongitud = ta.GetProperty("Length");
                    propElemento = ta.GetProperty("Item");
                }
                if (propLongitud == null || propElemento == null)
                {
                    return;
                }
                int longitud = (int)propLongitud.GetValue(bruto);
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
                    // Recuento cada ~2 segundos, no cada 20 fotogramas:
                    // recorrer miles de canciones leyendo una propiedad de
                    // Il2Cpp por cada una es justo lo que provocaba tirones.
                    contadorRevision++;
                    if (contadorRevision < 120)
                    {
                        return;
                    }
                    contadorRevision = 0;
                    int visibles = ContarVisibles();
                    if (visiblesUltimaConstruccion >= 0 && visibles == visiblesUltimaConstruccion)
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

                // Y AHORA LOS INDICES. Cada Seccion lleva un BeginIndex sobre
                // la lista plana (cabeceras incluidas); sin el, la pantalla no
                // sabe donde empieza cada una y solo se pinta la que cae en un
                // sitio valido. Normalmente los fija el propio juego al final de
                // su rutina, pero esa rutina es justo la que aborta con nuestro
                // criterio: la excepcion se contiene, y lo que venia despues no
                // llega a ejecutarse. Asi que los recalculamos nosotros, igual
                // que en el camino manual.
                object activa = ListaSeccionesActiva();
                if (activa != null)
                {
                    Indexar(activa);
                }
                visiblesUltimaConstruccion = ContarVisibles();
                if (Diagnostico.Detallado)
                {
                    RegistrarResultado();
                }
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
        // La lista de secciones que la pantalla esta mostrando: la propiedad
        // estatica de tipo List<Seccion> con contenido.
        private static PropertyInfo propListaActiva;

        private static object ListaSeccionesActiva()
        {
            try
            {
                if (propListaActiva != null)
                {
                    return propListaActiva.GetValue(null);
                }
                Type t = Ofuscado.Tipo(TipoBiblioteca);
                if (t == null || tipoSeccion == null)
                {
                    return null;
                }
                Type esperado = typeof(Il2CppSystem.Collections.Generic.List<>)
                    .MakeGenericType(tipoSeccion);
                PropertyInfo[] ps = t.GetProperties(BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.Static);
                for (int i = 0; i < ps.Length; i++)
                {
                    if (ps[i].PropertyType != esperado)
                    {
                        continue;
                    }
                    object v;
                    try { v = ps[i].GetValue(null); }
                    catch (Exception) { continue; }
                    if (v == null)
                    {
                        continue;
                    }
                    PropertyInfo c = v.GetType().GetProperty("Count");
                    if (c != null && (int)c.GetValue(v) > 0)
                    {
                        propListaActiva = ps[i];
                        MelonLogger.Msg("[Orden] lista activa: " + ps[i].Name);
                        return v;
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

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
        // Sonda: que criterio cree el juego que esta activo. Solo con
        // diagnostico.flag, y solo escribe cuando el valor cambia.

        // Por donde se sale Tick. Solo escribe cuando cambia el motivo, para
        // no llenar el log.

        // Que criterio de orden esta activo AHORA MISMO.
        //
        // Durante mucho tiempo esto comparaba el ajuste sort_filter con nuestro
        // indice, y estaba mal: ese ajuste no es el criterio en uso. Se le
        // sube el maximo para que el nuestro sea seleccionable en el menu, pero
        // su valor se queda con lo ultimo que se guardo en settings.ini y no se
        // mueve al cambiar de criterio. Se comprobo con una sonda: eligiendo
        // Playlist, Artist y Difficulty seguidos, el ajuste se quedo en 5 las
        // tres veces.
        //
        // El criterio en uso es una CADENA estatica de SongLibrary, con el
        // nombre tal cual. Como el guardia nunca se abria, no se construian las
        // secciones nunca: esa era la razon real de que ordenar por Difficulty
        // no agrupara nada.
        private static PropertyInfo propNombreOrden;
        private static string ultimoNombreVisto;
        private static bool avisadoNombreOrden;

        private static bool EsNuestroCriterioActivo()
        {
            try
            {
                if (!instalado)
                {
                    return false;
                }
                if (propNombreOrden == null && !ResolverNombreOrden())
                {
                    return false;
                }
                string activo = propNombreOrden.GetValue(null) as string;
                if (activo != ultimoNombreVisto)
                {
                    ultimoNombreVisto = activo;
                    if (activo == Nombre)
                    {
                        // Acaba de seleccionarse el nuestro: hay que rehacer las
                        // secciones YA. Esperar a la revision periodica dejaba
                        // la lista sin agrupar unos tres segundos, que es lo que
                        // se veia como "no pasa nada hasta que me muevo".
                        contadorRevision = 1000;
                        visiblesUltimaConstruccion = -1;
                    }
                }
                return activo == Nombre;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Se busca por CONTENIDO: la propiedad string estatica cuyo valor es
        // uno de los nombres de criterio. Los nombres de propiedad los genera
        // Il2CppInterop y no valen para identificarla.
        private static bool ResolverNombreOrden()
        {
            try
            {
                Type t = Ofuscado.Tipo(TipoBiblioteca);
                Il2CppStringArray nombres = ArrayNombres();
                if (t == null || nombres == null)
                {
                    return false;
                }
                PropertyInfo[] ps = t.GetProperties(BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.Static);
                for (int i = 0; i < ps.Length; i++)
                {
                    if (ps[i].PropertyType != typeof(string)
                        || ps[i].GetIndexParameters().Length != 0)
                    {
                        continue;
                    }
                    string v;
                    try { v = ps[i].GetValue(null) as string; }
                    catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(v))
                    {
                        continue;
                    }
                    for (int j = 0; j < nombres.Length; j++)
                    {
                        if (nombres[j] == v)
                        {
                            propNombreOrden = ps[i];
                            if (!avisadoNombreOrden)
                            {
                                avisadoNombreOrden = true;
                                MelonLogger.Msg("[Orden] criterio activo se lee de "
                                    + ps[i].Name + " (ahora '" + v + "')");
                            }
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
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

        // Con un filtro activo, ordenar por nuestro criterio hacia REVENTAR el
        // juego: IndexOutOfRangeException dentro de un
        // SongLibrary.<estatico> void(string), en bucle, y el sistema de
        // filtros quedaba inservible hasta reiniciar.
        //
        // El motivo: el juego traduce cada criterio de orden a uno de sus SIETE
        // tipos de agrupacion (Name, Artist, Album, Genre, Year, Charter,
        // Playlist — se ven en el string[] privado de 7 "Unknown X" y en los dos
        // Dictionary de 7 entradas). El nuestro no tiene traduccion posible, asi
        // que la busqueda devuelve -1 y se usa como indice. Solo se llega ahi
        // por el camino que dispara un cambio de filtro; por eso ordenar por
        // Difficulty a secas funciona y solo revienta con un filtro puesto.
        //
        // El primer intento fue saltarse el metodo con un prefix. Mala idea:
        // uno de esos tres void(string) es tambien el que APLICA el criterio,
        // asi que se dejaba de romper pero tampoco salian las secciones.
        //
        // Lo que se hace es dejarlo correr y tragarse esa unica excepcion con un
        // finalizer. La parte que falla es la traduccion, que a nosotros no nos
        // sirve para nada —las secciones las construimos aparte—, y lo demas
        // sigue su curso. Se filtra por el mensaje para no ocultar otros fallos.
        //
        // Se parchean los tres void(string) en vez de averiguar cual es: el
        // finalizer solo actua si de verdad ha saltado ese error, asi que
        // sobrar no molesta, y no dependemos de un nombre generado por
        // Il2CppInterop.
        public static void InstalarParcheNombre(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type t = Ofuscado.Tipo(TipoBiblioteca);
                if (t == null)
                {
                    return;
                }
                HarmonyLib.HarmonyMethod finalizador = new HarmonyLib.HarmonyMethod(
                    typeof(OrdenDificultad).GetMethod("TragarDesbordamiento",
                        BindingFlags.NonPublic | BindingFlags.Static));
                MethodInfo[] ms = t.GetMethods(BindingFlags.Public | BindingFlags.Static);
                int n = 0;
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].ReturnType != typeof(void))
                    {
                        continue;
                    }
                    ParameterInfo[] ps = ms[i].GetParameters();
                    if (ps.Length != 1 || ps[0].ParameterType != typeof(string))
                    {
                        continue;
                    }
                    try
                    {
                        harmony.Patch(ms[i], null, null, null, finalizador, null);
                        n++;
                    }
                    catch (Exception)
                    {
                    }
                }
                MelonLogger.Msg("[Orden] red de seguridad: " + n.ToString() + " metodo(s)");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Orden] red de seguridad: " + ex.Message);
            }
        }

        private static bool avisadoTragado;

        // Devolver null hace que Harmony descarte la excepcion.
        private static Exception TragarDesbordamiento(Exception __exception,
                                                      MethodBase __originalMethod)
        {
            if (__exception == null)
            {
                return null;
            }
            // Llega envuelta en Il2CppException, asi que se mira el mensaje.
            string m = __exception.Message;
            if (m == null || m.IndexOf("outside the bounds",
                                       StringComparison.OrdinalIgnoreCase) < 0)
            {
                return __exception;      // otro fallo: que se vea
            }
            if (!avisadoTragado)
            {
                avisadoTragado = true;
                MelonLogger.Msg("[Orden] desbordamiento contenido en "
                    + (__originalMethod == null ? "?" : __originalMethod.Name)
                    + " (el juego no sabe agrupar por '" + Nombre + "')");
            }
            return null;
        }

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

        private static object ElementoCache(object arr, int i)
        {
            PropertyInfo idx = propElemento ?? arr.GetType().GetProperty("Item");
            return idx != null ? idx.GetValue(arr, new object[] { i }) : null;
        }

        private static void PonerElementoCache(object arr, int i, object v)
        {
            PropertyInfo idx = propElemento ?? arr.GetType().GetProperty("Item");
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
