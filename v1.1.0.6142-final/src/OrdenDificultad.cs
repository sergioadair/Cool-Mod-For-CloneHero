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
    // Tres piezas:
    //
    //   1. El array de nombres de orden (27 entradas) -> se le anade la nuestra.
    //   2. El ajuste sort_filter, cuyo maximo hay que subir de 26 a 27 o la
    //      opcion nueva queda inalcanzable.
    //   3. El propio Sort del juego, parcheado: con nuestro criterio construye
    //      las secciones en el mismo punto en que el juego construye las suyas
    //      (ver "dentro del Sort del juego").
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

        // ------------------------------------------- dentro del Sort del juego
        // Como ordena el juego (leido en la descompilacion, SongDirectory):
        //
        //   Sort(nombre):
        //     sortCounter = posicion de nombre en la lista de criterios
        //     si la biblioteca esta "sucia" (cambio un filtro): vacia la cache
        //       de secciones entera y los agrupamientos
        //     si cache[sortCounter] tiene algo: es la lista activa y fin
        //     si no: un switch sobre el nombre construye las secciones, y al
        //       final numera las de la lista activa
        //
        // El nuestro no esta en ese switch: sin ayuda, el juego no construye
        // nada y numera la lista del criterio anterior. Y cuando la biblioteca
        // esta sucia reutiliza un array de cache creado antes de anadir el
        // nuestro, con un hueco menos: de ahi el "outside the bounds" que se
        // veia al ordenar por Difficulty con un filtro puesto.
        //
        // Al cambiar un filtro no se llama a Sort, sino a un "reordenar con el
        // criterio actual" que marca la biblioteca como sucia y lleva una copia
        // de Sort dentro. Se parchean los dos: cuando el criterio es el
        // nuestro, se hace lo mismo que haria el juego con uno suyo y no se le
        // deja seguir.
        //
        // Los nombres son los que pone Il2CppInterop a este binario
        // (v1.1.0.6142); si no aparecen con la forma esperada, no se parchea
        // nada y el criterio sale sin agrupar, pero el juego sigue intacto.
        private const string MetodoOrdenar = "Method_Public_Static_Void_String_0";
        private const string MetodoReordenar = "Method_Public_Static_Void_6";
        private const string MetodoVaciarGrupos = "Method_Public_Static_Void_5";
        private const string PropContador = "field_Private_Static_Int32_0";
        private const string PropSucia = "field_Private_Static_Boolean_2";

        private static PropertyInfo propContador;
        private static PropertyInfo propSucia;
        private static PropertyInfo propActiva;
        private static MethodInfo metodoVaciarGrupos;

        public static void InstalarParches(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type t = Ofuscado.Tipo(TipoBiblioteca);
                if (t == null || tipoSeccion == null)
                {
                    MelonLogger.Warning("[Orden] sin biblioteca: no se parchea el orden");
                    return;
                }
                BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                MethodInfo ordenar = t.GetMethod(MetodoOrdenar, f);
                MethodInfo reordenar = t.GetMethod(MetodoReordenar, f);
                metodoVaciarGrupos = t.GetMethod(MetodoVaciarGrupos, f);
                propContador = t.GetProperty(PropContador, f);
                propSucia = t.GetProperty(PropSucia, f);
                Type tipoLista = typeof(Il2CppSystem.Collections.Generic.List<>).MakeGenericType(tipoSeccion);
                PropertyInfo[] ps = t.GetProperties(f);
                for (int i = 0; i < ps.Length; i++)
                {
                    // la lista activa: la unica List<Seccion> estatica
                    if (ps[i].PropertyType == tipoLista)
                    {
                        propActiva = ps[i];
                        break;
                    }
                }
                bool formaOk = ordenar != null && reordenar != null && metodoVaciarGrupos != null
                    && ordenar.GetParameters().Length == 1 && ordenar.GetParameters()[0].ParameterType == typeof(string)
                    && reordenar.GetParameters().Length == 0 && metodoVaciarGrupos.GetParameters().Length == 0
                    && propContador != null && propContador.PropertyType == typeof(int)
                    && propSucia != null && propSucia.PropertyType == typeof(bool)
                    && propActiva != null && campoCache != null;
                if (!formaOk)
                {
                    MelonLogger.Warning("[Orden] Sort no tiene la forma esperada: no se parchea");
                    return;
                }
                harmony.Patch(ordenar, new HarmonyLib.HarmonyMethod(typeof(OrdenDificultad).GetMethod(
                    "AntesDeOrdenar", BindingFlags.NonPublic | BindingFlags.Static)));
                harmony.Patch(reordenar, new HarmonyLib.HarmonyMethod(typeof(OrdenDificultad).GetMethod(
                    "AntesDeReordenar", BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Orden] parches: " + ex.Message);
            }
        }

        private static bool AntesDeOrdenar(string __0)
        {
            if (!instalado || __0 != Nombre)
            {
                return true;
            }
            Ordenar();
            return false;
        }

        private static bool AntesDeReordenar()
        {
            if (!instalado || CriterioActual() != Nombre)
            {
                return true;
            }
            propSucia.SetValue(null, true);
            Ordenar();
            return false;
        }

        private static string CriterioActual()
        {
            try
            {
                Il2CppStringArray nombres = ArrayNombres();
                int i = (int)propContador.GetValue(null);
                return nombres != null && i >= 0 && i < nombres.Length ? nombres[i] : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Lo mismo que hace Sort con un criterio suyo, pero con el nuestro.
        private static void Ordenar()
        {
            try
            {
                Il2CppStringArray nombres = ArrayNombres();
                int indice = -1;
                for (int i = 0; nombres != null && i < nombres.Length; i++)
                {
                    if (nombres[i] == Nombre)
                    {
                        indice = i;
                        break;
                    }
                }
                if (indice < 0)
                {
                    return;
                }
                nuestroIndice = indice;
                propContador.SetValue(null, indice);

                // la cache, con sitio para nuestro hueco
                object cache = LeerEstatico(campoCache);
                if (cache == null)
                {
                    cache = Activator.CreateInstance(TipoDe(campoCache), new object[] { (long)nombres.Length });
                    EscribirEstatico(campoCache, cache);
                }
                if (propLongitud == null || propElemento == null)
                {
                    propLongitud = cache.GetType().GetProperty("Length");
                    propElemento = cache.GetType().GetProperty("Item");
                }
                int longitud = (int)propLongitud.GetValue(cache);
                if (indice >= longitud)
                {
                    AmpliarCache(cache, longitud);
                    cache = LeerEstatico(campoCache);
                    longitud = (int)propLongitud.GetValue(cache);
                }

                // biblioteca sucia: se vacia todo, como hace el juego
                if ((bool)propSucia.GetValue(null))
                {
                    for (int i = 0; i < longitud; i++)
                    {
                        PonerElementoCache(cache, i, null);
                    }
                    metodoVaciarGrupos.Invoke(null, null);
                }

                object hecha = ElementoCache(cache, indice);
                if (hecha != null)
                {
                    propActiva.SetValue(null, hecha);
                    return;
                }

                // el agrupador del juego escribe cache[sortCounter] y la lista
                // activa; si no esta disponible, se construye a mano
                if (!AgruparNativo())
                {
                    object secciones = Construir();
                    if (secciones != null)
                    {
                        PonerElementoCache(cache, indice, secciones);
                        propActiva.SetValue(null, secciones);
                    }
                }
                propSucia.SetValue(null, false);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Orden] ordenar: " + ex);
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


                // Y los indices: cada Seccion lleva un BeginIndex sobre la
                // lista plana (cabeceras incluidas). Sort los fija al final con
                // sus criterios; con el nuestro no llega a ese paso.
                object activa = propActiva != null ? propActiva.GetValue(null) : null;
                if (activa != null)
                {
                    Indexar(activa);
                }
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Orden] agrupar nativo: " + ex);
                return false;
            }
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
