using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;

namespace CloneHeroMod
{
    // Localiza los miembros del juego por su NOMBRE REAL (el ofuscado del
    // binario), no por el nombre que les pone Il2CppInterop.
    //
    // POR QUE: el interop numera los miembros ofuscados segun el orden en que
    // aparecen (Method_Public_Void_0, _1...), y cada compilacion del juego
    // baraja ese orden. De la 1.1.0.6142 a la 6161 nueve metodos cambiaron de
    // numero, y con el nombre viejo el mod enganchaba OTRO metodo sin dar
    // ningun error. Lo que se conserva entre versiones es el nombre real del
    // metodo o campo de verdad (los senuelos del ofuscador si cambian).
    //
    // COMO: cada clase del interop guarda en campos estaticos privados el
    // puntero nativo de cada miembro (NativeMethodInfoPtr_<nombre interop>,
    // NativeFieldInfoPtr_<nombre interop>). El runtime de il2cpp da el nombre
    // real de ese puntero, asi que se recorren y se emparejan. Se hace una vez
    // por tipo y se guarda.
    //
    // Si un nombre no aparece (otra version del juego lo renombro), devuelve
    // null y lo dice en el log: la funcion que lo usa se apaga en vez de tocar
    // lo que no es.
    public static class Nativo
    {
        private const string PrefijoMetodo = "NativeMethodInfoPtr_";
        private const string PrefijoCampo = "NativeFieldInfoPtr_";

        // tipo -> (nombre real -> nombres interop). Un nombre real puede
        // repetirse con otra firma (sobrecargas).
        private static readonly Dictionary<Type, Dictionary<string, List<string>>> metodos =
            new Dictionary<Type, Dictionary<string, List<string>>>();
        private static readonly Dictionary<Type, Dictionary<string, string>> campos =
            new Dictionary<Type, Dictionary<string, string>>();
        private static readonly object cerrojo = new object();

        private const BindingFlags Todo = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // El metodo con ese nombre real y exactamente esos parametros. Busca
        // tambien en las clases base.
        public static MethodInfo Metodo(Type t, string real, params Type[] parametros)
        {
            for (Type x = t; x != null && x != typeof(object); x = x.BaseType)
            {
                List<string> nombres;
                if (!IndiceMetodos(x).TryGetValue(real, out nombres))
                {
                    continue;
                }
                MethodInfo[] ms = x.GetMethods(Todo);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (nombres.Contains(ms[i].Name) && MismosParametros(ms[i], parametros))
                    {
                        return ms[i];
                    }
                }
            }
            Avisar("metodo", t, real);
            return null;
        }

        // La propiedad (prop_*) cuyo getter tiene ese nombre real. Busca
        // tambien en las clases base.
        public static PropertyInfo Propiedad(Type t, string real)
        {
            for (Type x = t; x != null && x != typeof(object); x = x.BaseType)
            {
                List<string> nombres;
                if (!IndiceMetodos(x).TryGetValue(real, out nombres))
                {
                    continue;
                }
                PropertyInfo[] ps = x.GetProperties(Todo);
                for (int i = 0; i < ps.Length; i++)
                {
                    MethodInfo g = ps[i].GetGetMethod(true);
                    if (g != null && nombres.Contains(g.Name))
                    {
                        return ps[i];
                    }
                }
            }
            Avisar("propiedad", t, real);
            return null;
        }

        // La propiedad field_* con la que el interop da acceso al campo que
        // en el juego se llama asi. Busca tambien en las clases base.
        public static PropertyInfo Campo(Type t, string real)
        {
            for (Type x = t; x != null && x != typeof(object); x = x.BaseType)
            {
                string nombre;
                if (!IndiceCampos(x).TryGetValue(real, out nombre))
                {
                    continue;
                }
                PropertyInfo p = x.GetProperty(nombre, Todo);
                if (p != null)
                {
                    return p;
                }
            }
            Avisar("campo", t, real);
            return null;
        }

        private static bool MismosParametros(MethodInfo m, Type[] esperados)
        {
            ParameterInfo[] ps = m.GetParameters();
            if (ps.Length != esperados.Length)
            {
                return false;
            }
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].ParameterType != esperados[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static Dictionary<string, List<string>> IndiceMetodos(Type t)
        {
            lock (cerrojo)
            {
                Dictionary<string, List<string>> d;
                if (metodos.TryGetValue(t, out d))
                {
                    return d;
                }
                d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> par in Punteros(t, PrefijoMetodo, true))
                {
                    List<string> l;
                    if (!d.TryGetValue(par.Key, out l))
                    {
                        l = new List<string>();
                        d[par.Key] = l;
                    }
                    l.Add(par.Value);
                }
                metodos[t] = d;
                return d;
            }
        }

        private static Dictionary<string, string> IndiceCampos(Type t)
        {
            lock (cerrojo)
            {
                Dictionary<string, string> d;
                if (campos.TryGetValue(t, out d))
                {
                    return d;
                }
                d = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> par in Punteros(t, PrefijoCampo, false))
                {
                    d[par.Key] = par.Value;
                }
                campos[t] = d;
                return d;
            }
        }

        // (nombre real, nombre interop) de cada puntero nativo de un tipo.
        private static List<KeyValuePair<string, string>> Punteros(Type t, string prefijo, bool esMetodo)
        {
            List<KeyValuePair<string, string>> r = new List<KeyValuePair<string, string>>();
            if (t.ContainsGenericParameters)
            {
                return r;       // generico sin cerrar: sus punteros no existen
            }
            FieldInfo[] fs;
            try
            {
                fs = t.GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
            }
            catch (Exception)
            {
                return r;
            }
            for (int i = 0; i < fs.Length; i++)
            {
                if (fs[i].FieldType != typeof(IntPtr) || !fs[i].Name.StartsWith(prefijo, StringComparison.Ordinal))
                {
                    continue;
                }
                try
                {
                    IntPtr p = (IntPtr)fs[i].GetValue(null);
                    if (p == IntPtr.Zero)
                    {
                        continue;
                    }
                    IntPtr n = esMetodo
                        ? Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name(p)
                        : Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_name(p);
                    string real = n == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(n);
                    if (real != null)
                    {
                        r.Add(new KeyValuePair<string, string>(real, fs[i].Name.Substring(prefijo.Length)));
                    }
                }
                catch (Exception)
                {
                }
            }
            return r;
        }

        private static readonly HashSet<string> avisados = new HashSet<string>();

        private static void Avisar(string que, Type t, string real)
        {
            string clave = que + "|" + (t != null ? t.FullName : "?") + "|" + real;
            lock (cerrojo)
            {
                if (!avisados.Add(clave))
                {
                    return;
                }
            }
            MelonLogger.Warning("[Nativo] no se encontro el " + que + " " + Escapado(real) + " en "
                + (t != null ? t.Name : "?") + ": esta version del juego lo cambio");
        }

        // Los nombres reales son letras modificadoras Unicode; en el log se
        // escriben como \uXXXX para poder buscarlos en el volcado.
        private static string Escapado(string s)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (c < 128)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4"));
                }
            }
            return sb.ToString();
        }
    }

    // Nombres reales de los miembros que usa el mod, sacados del volcado de
    // la 1.1.0.6142 y comprobados iguales en la 6161. Escritos como escapes
    // \uXXXX para que el compilador no los pueda estropear.
    public static class Real
    {
        // --- Metodos
        public const string ResetIndex = "\u02b7\u02be\u02b8\u02b7\u02b9\u02ba\u02b3\u02b6\u02b4\u02b7\u02b8";      // SongSelect.ResetIndex (lleva el cursor a la primera cancion)
        public const string SetMenuProperties = "\u02ba\u02bd\u02b3\u02bc\u02b8\u02bb\u02b8\u02ba\u02b5\u02c0\u02c0";      // SongSelect.SetMenuProperties (redibuja la lista)
        public const string Seleccionar = "\u02b5\u02b6\u02b8\u02bc\u02bf\u02bf\u02bc\u02b5\u02c0\u02bb\u02c0";      // BaseMenu.m71(int, bool): mueve el cursor a una posicion
        public const string Entrada = "\u02b3\u02ba\u02bf\u02bb\u02be\u02be\u02b4\u02b7\u02ba\u02bb\u02b6";      // BaseMenu.DoInput(CHPlayer): toda la entrada de los menus
        public const string ElementoEn = "\u02b2\u02b3\u02b3\u02be\u02b9\u02b3\u02b8\u02c1\u02b5\u02b5\u02b8";      // SongDirectory.m44(int): elemento de la lista visible
        public const string IndiceDe = "\u02b6\u02b5\u02b4\u02bd\u02b7\u02b4\u02b9\u02b8\u02b8\u02b5\u02bf";      // SongDirectory.m59(SongEntry): posicion de una cancion
        public const string Ordenar = "\u02b3\u02b8\u02b2\u02bf\u02c0\u02b6\u02c1\u02bd\u02b3\u02b8\u02b9";      // SongDirectory.Sort(string): agrupa por un criterio
        public const string Reordenar = "\u02ba\u02ba\u02b2\u02be\u02bd\u02be\u02c0\u02bb\u02c1\u02b7\u02b6";      // SongDirectory: vuelve a ordenar con el criterio actual
        public const string VaciarGrupos = "\u02bc\u02be\u02c0\u02b8\u02b4\u02b7\u02c0\u02b5\u02b8\u02b4\u02bd";      // SongDirectory: vacia las secciones
        public const string PrepararEscaneo = "\u02bb\u02b7\u02bc\u02c0\u02bc\u02b8\u02bb\u02b5\u02c1\u02b3\u02be";      // SongScan: preparacion del escaneo
        public const string Escanear = "\u02ba\u02b9\u02c0\u02c1\u02bf\u02bb\u02b3\u02b9\u02bb\u02b5\u02b3";      // SongScan: arranca el escaneo (true)
        public const string ConfirmarOpciones = "\u02b5\u02c1\u02ba\u02bb\u02b2\u02b9\u02c1\u02bb\u02b9\u02ba\u02b3";      // SongOptions: pulsar verde
        public const string CerrarOpciones = "\u02b3\u02bc\u02ba\u02c0\u02c0\u02b8\u02b2\u02b4\u02b3\u02c1\u02b8";      // SongOptions: cerrar el panel
        public const string RellenarBarraSp = "\u02b8\u02bf\u02b2\u02c1\u02b4\u02be\u02c1\u02b4\u02bb\u02b3\u02bc";      // SPBar(float, bool): rellena la barra de Star Power
        public const string PosicionAudio = "\u02b4\u02b4\u02b3\u02b6\u02ba\u02b4\u02be\u02b2\u02b8\u02b6\u02c0";      // BassAudioManager: segundo actual de la cancion
        public const string CargarCancion = "\u02be\u02b3\u02b8\u02b4\u02bb\u02b3\u02be\u02b4\u02ba\u02c0\u02ba";      // BassAudioManager.m106: carga las pistas de una cancion
        public const string CargarPrevia = "\u02c1\u02b8\u02b9\u02c1\u02be\u02bd\u02b2\u02b4\u02b6\u02b9\u02bf";      // BassAudioManager.m80: hilo que carga el preview
        public const string CrearSalida = "\u02b8\u02be\u02bc\u02b6\u02b8\u02ba\u02bf\u02b2\u02b6\u02b4\u02ba";      // BassAudioManager.m77(int): crea el mezclador de salida
        public const string CambiarVida = "\u02b7\u02b8\u02c1\u02be\u02bf\u02b9\u02bf\u02bd\u02b2\u02c0\u02be";      // motor (StrikeCore).ChangeHealth(bool)

        // --- Propiedades (nombre real de su getter)
        public const string EnLinea = "\u02b5\u02b5\u02b8\u02bf\u02b9\u02b7\u02b9\u02b8\u02ba\u02b7\u02b9";      // CHNetManager: partida online
        public const string SeleccionActual = "\u02b9\u02b4\u02b9\u02c0\u02b2\u02be\u02bd\u02b2\u02b9\u02c0\u02b4";      // BaseMenu.currentSelectionInt
        public const string CuentaLista = "\u02ba\u02b6\u02ba\u02bd\u02b3\u02bb\u02ba\u02b6\u02c0\u02b5\u02b7";      // SongDirectory.m35: largo de la lista visible
        public const string AjusteValor = "\u02b4\u02be\u02b6\u02b9\u02b3\u02b7\u02b8\u02c0\u02b3\u02bc\u02bd";      // GameSetting: valor actual
        public const string AjusteMaximo = "\u02b6\u02b9\u02b6\u02b8\u02bc\u02c0\u02c1\u02b8\u02b7\u02bb\u02b8";      // GameSetting: maximo
        public const string AjusteMinimo = "\u02be\u02b5\u02b9\u02be\u02b8\u02b7\u02b7\u02b6\u02b5\u02b6\u02bf";      // GameSetting: minimo
        public const string AjusteSiNo = "\u02bf\u02c1\u02bb\u02b8\u02b6\u02bc\u02c0\u02bf\u02b4\u02b3\u02b2";      // GameSetting.GetBoolValue: valor == 1

        // --- Campos
        public const string CambiandoAjuste = "\u02be\u02ba\u02b2\u02bb\u02ba\u02b3\u02b2\u02b2\u02b6\u02bf\u02b3";      // BaseMenu.changedSetting
        public const string AjusteActual = "\u02b7\u02b6\u02b7\u02b8\u02b7\u02b6\u02b5\u02bd\u02c1\u02b9\u02bc";      // BaseSettingMenu.currentSetting
        public const string CanalSalida = "\u02b4\u02b5\u02b3\u02c0\u02b4\u02bc\u02b4\u02ba\u02b7\u02b7\u02bd";      // BassAudioManager.linkedChannel
        public const string CancionPrevia = "\u02b5\u02b8\u02bb\u02bd\u02bd\u02bc\u02b2\u02bb\u02bd\u02ba\u02b8";      // BassAudioManager.f_80: cancion del preview pendiente
        public const string TablasActivas = "\u02b8\u02b3\u02b7\u02ba\u02b3\u02be\u02b2\u02b7\u02b4\u02bf\u02bb";      // LeaderboardsOnlineManager: leaderboards activos
        public const string SinSeccion = "\u02ba\u02b5\u02b2\u02ba\u02bf\u02bd\u02b9\u02bd\u02c1\u02b3\u02b4";      // Settings.NoSection
        public const string SinFallo = "\u02bb\u02c0\u02b7\u02b8\u02b3\u02be\u02b7\u02bb\u02ba\u02b8\u02ba";      // Settings.isNoFail (el ajuste No Fail)
        public const string ListaSucia = "\u02b4\u02be\u02c0\u02ba\u02b7\u02bd\u02b3\u02b3\u02c0\u02bd\u02b2";      // SongDirectory: hay que reagrupar
        public const string ContadorOrden = "\u02bd\u02b7\u02c0\u02b6\u02b5\u02bc\u02c1\u02bb\u02ba\u02be\u02bc";      // SongDirectory: contador del orden
        public const string InstrumentoJugador = "\u02b7\u02bb\u02ba\u02bc\u02b4\u02b6\u02b4\u02b3\u02bd\u02ba\u02bf";      // PlayerSelectionMenu.instrument
        public const string NivelJugador = "\u02b5\u02bd\u02c0\u02c1\u02bf\u02bf\u02b7\u02bf\u02b6\u02c1\u02ba";      // PlayerSelectionMenu.difficulty
        public const string CancionJugador = "\u02ba\u02b4\u02b8\u02ba\u02c1\u02b7\u02bf\u02bd\u02b5\u02be\u02ba";      // PlayerSelectionMenu.songEntry
    }
}
