using System;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;

namespace CloneHeroMod
{
    // Anade "Favorites" a (Hold) Sort List > Filter Options.
    //
    // El juego v1.1.0.6142 ya trae favoritos nativos (GameData\favorites.bin) y
    // los expone como criterio de ORDEN (indice 14 de la lista de orden), pero
    // se dejo fuera de la lista de FILTROS, que tiene 21 entradas y no lo
    // incluye. Esto rellena ese hueco reutilizando el sistema nativo: no se
    // guarda nada aparte, se consulta el FavoritesManager del propio juego.
    public static class FiltroFavoritos
    {
        public const string Nombre = "Favorites";

        // Nombres reales (ofuscados) confirmados en el volcado de Il2CppDumper.
        private const string TipoBiblioteca = "ʾʲʽʻʺʶʺʺʷʿʺ";   // SongLibrary
        private const string TipoFabricaFiltros = "ʷʹˁʼʸʽˀʺʻʸʳ";
        private const string TipoFavoritos = "ʾʷʹʿʻʻʿʽʳʽˁ";     // FavoritesManager nativo

        private static MethodInfo esFavorita;       // bool (checksum)
        private static MemberInfo campoChecksum;    // SongEntry.checksum
        private static MemberInfo conjuntoFavoritos;   // HashSet<checksum> nativo
        private static bool instalado;

        // Lista de filtros: en SongLibrary es el segundo Il2CppStringArray
        // publico estatico. Se localiza por contenido y no por posicion, que es
        // lo unico estable si el juego reordena sus campos.
        private static PropertyInfo campoFiltros;

        public static void Instalar()
        {
            try
            {
                if (instalado)
                {
                    return;
                }
                if (!ResolverFavoritosNativos())
                {
                    MelonLogger.Warning("[Favoritos] no se localizo el gestor nativo; filtro no instalado");
                    return;
                }
                if (!ResolverListaFiltros())
                {
                    MelonLogger.Warning("[Favoritos] no se localizo la lista de filtros; filtro no instalado");
                    return;
                }
                if (!AnadirALaLista())
                {
                    return;
                }
                instalado = true;
                MelonLogger.Msg("[Favoritos] filtro instalado");
                Autocomprobar(PrimeraCancion());
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Favoritos] " + ex);
            }
        }

        public static bool Instalado
        {
            get { return instalado; }
        }

        // ------------------------------------------------------------------
        // CUIDADO: el gestor nativo tiene DOS metodos "static bool (SongEntry)":
        //
        //   ʹʳʲʼʴʽʿʻʴˁʶ(SongEntry)   -> IsFavorite   (solo consulta)
        //   ʿʷʽʼʽʽʳʿʴˀʻ(SongEntry)   -> Toggle       (CAMBIA el estado)
        //
        // Elegir por firma "bool(SongEntry)" es ambiguo, y quedarse con el
        // Toggle hace que filtrar marque y desmarque canciones. Por eso aqui se
        // usa la otra sobrecarga, la que recibe el checksum, que es unica y no
        // se puede confundir con nada.
        private static bool ResolverFavoritosNativos()
        {
            Type t = Ofuscado.Tipo(TipoFavoritos);
            Type tSong = Ofuscado.Tipo("SongEntry");
            if (t == null || tSong == null)
            {
                return false;
            }

            campoChecksum = MiembroChecksum(tSong);
            if (campoChecksum == null)
            {
                MelonLogger.Warning("[Favoritos] no se localizo SongEntry.checksum");
                return false;
            }
            Type tChecksum = TipoDe(campoChecksum);

            MethodInfo[] ms = t.GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < ms.Length; i++)
            {
                if (ms[i].ReturnType != typeof(bool))
                {
                    continue;
                }
                ParameterInfo[] ps = ms[i].GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == tChecksum)
                {
                    esFavorita = ms[i];
                    MelonLogger.Msg("[Favoritos] comprobador nativo (por checksum): " + ms[i].Name);
                    break;
                }
            }
            if (esFavorita == null)
            {
                return false;
            }

            // Red de seguridad para detectar un metodo mutante. NO sirve
            // cualquier "static int": la clase expone tambien la constante de
            // version (20260201) como propiedad, y compararla consigo misma no
            // detecta nada. Se usa el HashSet de checksums, que es el estado
            // real de las favoritas.
            conjuntoFavoritos = ConjuntoFavoritos(t, tChecksum);
            if (conjuntoFavoritos == null)
            {
                MelonLogger.Warning("[Favoritos] sin contador fiable; se instala sin autocomprobacion");
            }
            return true;
        }

        // Una cancion cualquiera de la biblioteca, solo para la autocomprobacion.
        private static Il2Cpp.SongEntry PrimeraCancion()
        {
            try
            {
                Type t = Ofuscado.Tipo(TipoBiblioteca);
                if (t == null)
                {
                    return null;
                }
                Type esperado = typeof(Il2CppSystem.Collections.Generic.List<>)
                    .MakeGenericType(typeof(Il2Cpp.SongEntry));
                PropertyInfo[] props = t.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                for (int i = 0; i < props.Length; i++)
                {
                    if (props[i].PropertyType != esperado)
                    {
                        continue;
                    }
                    Il2CppSystem.Collections.Generic.List<Il2Cpp.SongEntry> lista =
                        props[i].GetValue(null) as Il2CppSystem.Collections.Generic.List<Il2Cpp.SongEntry>;
                    if (lista != null && lista.Count > 0)
                    {
                        return lista[0];
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private static MemberInfo MiembroChecksum(Type tSong)
        {
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            MemberInfo[] ms = tSong.GetMembers(f);
            for (int i = 0; i < ms.Length; i++)
            {
                if (ms[i].Name == "checksum" && (ms[i] is FieldInfo || ms[i] is PropertyInfo))
                {
                    return ms[i];
                }
            }
            return null;
        }

        private static Type TipoDe(MemberInfo m)
        {
            FieldInfo f = m as FieldInfo;
            if (f != null)
            {
                return f.FieldType;
            }
            PropertyInfo p = m as PropertyInfo;
            if (p != null)
            {
                return p.PropertyType;
            }
            return null;
        }

        private static object LeerChecksum(object cancion)
        {
            FieldInfo f = campoChecksum as FieldInfo;
            if (f != null)
            {
                return f.GetValue(cancion);
            }
            PropertyInfo p = campoChecksum as PropertyInfo;
            if (p != null)
            {
                return p.GetValue(cancion);
            }
            return null;
        }

        // Localiza el HashSet<checksum> estatico donde el juego guarda las
        // favoritas. Es el unico miembro cuyo valor refleja el estado real.
        private static MemberInfo ConjuntoFavoritos(Type t, Type tChecksum)
        {
            // Se compara por forma y no por typeof: Il2CppSystem.HashSet vive
            // en un ensamblado que este proyecto no referencia.
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            PropertyInfo[] props = t.GetProperties(f);
            for (int i = 0; i < props.Length; i++)
            {
                if (EsConjuntoDe(props[i].PropertyType, tChecksum))
                {
                    return props[i];
                }
            }
            FieldInfo[] campos = t.GetFields(f);
            for (int i = 0; i < campos.Length; i++)
            {
                if (EsConjuntoDe(campos[i].FieldType, tChecksum))
                {
                    return campos[i];
                }
            }
            return null;
        }

        private static bool EsConjuntoDe(Type tipo, Type elemento)
        {
            if (tipo == null || !tipo.IsGenericType || !tipo.Name.StartsWith("HashSet"))
            {
                return false;
            }
            Type[] args = tipo.GetGenericArguments();
            return args.Length == 1 && args[0] == elemento;
        }

        public static int FavoritasContadas
        {
            get
            {
                try
                {
                    if (conjuntoFavoritos == null)
                    {
                        return -1;
                    }
                    object v = null;
                    PropertyInfo p = conjuntoFavoritos as PropertyInfo;
                    if (p != null)
                    {
                        v = p.GetValue(null);
                    }
                    FieldInfo c = conjuntoFavoritos as FieldInfo;
                    if (c != null)
                    {
                        v = c.GetValue(null);
                    }
                    if (v == null)
                    {
                        return -1;
                    }
                    PropertyInfo cuenta = v.GetType().GetProperty("Count");
                    if (cuenta == null)
                    {
                        return -1;
                    }
                    object n = cuenta.GetValue(v);
                    return (n is int) ? (int)n : -1;
                }
                catch (Exception)
                {
                    return -1;
                }
            }
        }

        // Busca el Il2CppStringArray estatico que contiene los nombres de
        // filtro, identificandolo por dos entradas que sabemos que lleva.
        private static bool ResolverListaFiltros()
        {
            Type t = Ofuscado.Tipo(TipoBiblioteca);
            if (t == null)
            {
                return false;
            }
            PropertyInfo[] props = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].PropertyType != typeof(Il2CppStringArray))
                {
                    continue;
                }
                Il2CppStringArray arr = null;
                try
                {
                    arr = props[i].GetValue(null) as Il2CppStringArray;
                }
                catch (Exception)
                {
                    continue;
                }
                if (arr == null)
                {
                    continue;
                }
                bool tieneLimpiar = false;
                bool tieneFuente = false;
                for (int j = 0; j < arr.Length; j++)
                {
                    if (arr[j] == "Clear All Filters")
                    {
                        tieneLimpiar = true;
                    }
                    else if (arr[j] == "Current Source")
                    {
                        tieneFuente = true;
                    }
                }
                if (tieneLimpiar && tieneFuente)
                {
                    campoFiltros = props[i];
                    MelonLogger.Msg("[Favoritos] lista de filtros: " + props[i].Name
                                    + " (" + arr.Length.ToString() + " entradas)");
                    return true;
                }
            }
            return false;
        }

        private static bool AnadirALaLista()
        {
            Il2CppStringArray actual = campoFiltros.GetValue(null) as Il2CppStringArray;
            if (actual == null)
            {
                return false;
            }
            for (int i = 0; i < actual.Length; i++)
            {
                if (actual[i] == Nombre)
                {
                    instalado = true;
                    return false;   // ya estaba: nada que hacer
                }
            }
            Il2CppStringArray nueva = new Il2CppStringArray(actual.Length + 1);
            for (int i = 0; i < actual.Length; i++)
            {
                nueva[i] = actual[i];
            }
            nueva[actual.Length] = Nombre;
            campoFiltros.SetValue(null, nueva);
            return true;
        }

        // ------------------------------------------------------------------
        // La fabrica de filtros devuelve null para un nombre que no conoce.
        // Este postfix rellena ese null cuando el nombre es el nuestro.
        public static void InstalarParche(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type t = Ofuscado.Tipo(TipoFabricaFiltros);
                if (t == null)
                {
                    MelonLogger.Warning("[Favoritos] no se localizo la fabrica de filtros");
                    return;
                }
                // Il2CppInterop renombra los metodos con un esquema descriptivo
                // (Method_Public_Static_...) y, a diferencia de los tipos, NO
                // les deja el [ObfuscatedName]. Asi que se localiza por firma:
                // es el unico estatico que devuelve Func<SongEntry,bool> y
                // recibe (string, algo).
                MethodInfo original = LocalizarFabrica(t);
                if (original == null)
                {
                    MelonLogger.Warning("[Favoritos] no se localizo el metodo de fabrica");
                    return;
                }
                MethodInfo postfix = typeof(FiltroFavoritos).GetMethod(
                    "PostfixFabrica", BindingFlags.NonPublic | BindingFlags.Static);
                harmony.Patch(original, null, new HarmonyMethod(postfix));
                MelonLogger.Msg("[Favoritos] fabrica parcheada: " + original.Name);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Favoritos] parche: " + ex);
            }
        }

        private static MethodInfo LocalizarFabrica(Type t)
        {
            Type tSong = Ofuscado.Tipo("SongEntry");
            if (tSong == null)
            {
                return null;
            }
            Type esperado = typeof(Il2CppSystem.Func<,>).MakeGenericType(tSong, typeof(bool));
            MethodInfo[] ms = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < ms.Length; i++)
            {
                if (ms[i].ReturnType != esperado)
                {
                    continue;
                }
                ParameterInfo[] ps = ms[i].GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType == typeof(string))
                {
                    return ms[i];
                }
            }
            return null;
        }

        private static void PostfixFabrica(string __0, ref Il2CppSystem.Func<Il2Cpp.SongEntry, bool> __result)
        {
            try
            {
                if (__0 != Nombre || __result != null)
                {
                    return;
                }
                __result = ConstruirPredicado();
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Favoritos] postfix: " + ex);
            }
        }

        private static Il2CppSystem.Func<Il2Cpp.SongEntry, bool> predicadoCache;

        private static Il2CppSystem.Func<Il2Cpp.SongEntry, bool> ConstruirPredicado()
        {
            if (predicadoCache == null)
            {
                predicadoCache = DelegateSupport.ConvertDelegate<
                    Il2CppSystem.Func<Il2Cpp.SongEntry, bool>>(
                        new Func<Il2Cpp.SongEntry, bool>(EsFavorita));
            }
            return predicadoCache;
        }

        public static bool EsFavorita(Il2Cpp.SongEntry cancion)
        {
            try
            {
                if (cancion == null || esFavorita == null)
                {
                    return false;
                }
                object checksum = LeerChecksum(cancion);
                if (checksum == null)
                {
                    return false;
                }
                object r = esFavorita.Invoke(null, new object[] { checksum });
                return r is bool && (bool)r;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Comprobacion de que el metodo elegido solo consulta. Se evalua una
        // cancion y se mira si el numero de favoritas cambia; si cambia, el
        // filtro se desactiva en vez de seguir corrompiendo favorites.bin.
        public static void Autocomprobar(Il2Cpp.SongEntry muestra)
        {
            try
            {
                if (!instalado || muestra == null || conjuntoFavoritos == null)
                {
                    return;
                }
                int antes = FavoritasContadas;
                EsFavorita(muestra);
                int despues = FavoritasContadas;
                if (antes >= 0 && despues >= 0 && antes != despues)
                {
                    instalado = false;
                    esFavorita = null;
                    MelonLogger.Error("[Favoritos] el comprobador MUTA el estado ("
                        + antes.ToString() + " -> " + despues.ToString()
                        + "). Filtro desactivado para no danar favorites.bin.");
                    return;
                }
                MelonLogger.Msg("[Favoritos] autocomprobacion OK (favoritas="
                    + antes.ToString() + ")");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Favoritos] autocomprobacion: " + ex);
            }
        }
    }
}
