using System;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace CloneHeroMod
{
    // Anade la fila "Calculate Difficulty" a Settings > General y la conecta
    // con el calculo.
    //
    // Los metodos no conservan el nombre ofuscado y en GeneralSettingsMenu hay
    // una veintena de void() candidatos, asi que no se puede localizar el de
    // "Select" a ojo. En vez de adivinar:
    //
    //   - La fila se anade desde OnEnable, que si conserva su nombre.
    //   - Para la activacion se parchean LOS DOS unicos "public virtual void()"
    //     declarados en GeneralSettingsMenu; el postfix comprueba si la fila
    //     resaltada es la nuestra antes de hacer nada, asi que parchear de mas
    //     es inofensivo (y Lanzar() ya se protege de la doble llamada).
    public static class OpcionCalcular
    {
        public const string Nombre = "Calculate Difficulty";

        private static PropertyInfo propMenuStrings;
        private static PropertyInfo propTextObjects;
        private static PropertyInfo propBackgrounds;
        private static PropertyInfo propTextPosDiff;
        private static PropertyInfo propOpcionActual;
        private static bool resuelto;
        private static bool parcheado;
        private static object ultimoMenu;

        // ------------------------------------------------------------ parches
        public static void InstalarParches(HarmonyLib.Harmony harmony)
        {
            if (parcheado)
            {
                return;
            }
            parcheado = true;
            try
            {
                Type tBase = Ofuscado.Tipo("BaseSettingMenu");
                Type tGeneral = Ofuscado.Tipo("GeneralSettingsMenu");
                if (tBase == null || tGeneral == null)
                {
                    MelonLogger.Warning("[Opcion] no se localizaron los tipos de menu");
                    return;
                }

                MethodInfo onEnable = tBase.GetMethod("OnEnable",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (onEnable != null)
                {
                    // PREFIX, no postfix: OnEnable calcula ahi mismo los limites
                    // de navegacion a partir de menuStrings. Si la fila se anade
                    // despues, el menu queda contando una opcion de menos y la
                    // ultima es visible pero inalcanzable.
                    harmony.Patch(onEnable, new HarmonyMethod(
                        typeof(OpcionCalcular).GetMethod("PreOnEnable",
                            BindingFlags.NonPublic | BindingFlags.Static)), null);
                    MelonLogger.Msg("[Opcion] OnEnable parcheado (prefix)");
                }

                MethodInfo postSelect = typeof(OpcionCalcular).GetMethod("PostSelect",
                    BindingFlags.NonPublic | BindingFlags.Static);
                int n = 0;
                MethodInfo[] ms = tGeneral.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].ReturnType != typeof(void) || ms[i].GetParameters().Length != 0
                        || !ms[i].IsVirtual || ms[i].IsSpecialName)
                    {
                        continue;
                    }
                    // Los de ciclo de vida de Unity no son el de Select, y
                    // parchear Update dispara el postfix en cada fotograma.
                    string n2 = ms[i].Name;
                    if (n2 == "Update" || n2 == "Start" || n2 == "Awake"
                        || n2 == "OnEnable" || n2 == "OnDisable" || n2 == "OnDestroy")
                    {
                        continue;
                    }
                    // Solo los realmente publicos. Los protegidos (entrar y
                    // salir del modo edicion) no son el de Select, y ademas
                    // Harmony no consigue reconstruir su cuerpo: al parchearlos
                    // el juego lanzaba NullReference en cada llamada.
                    if (!n2.StartsWith("Method_Public_"))
                    {
                        continue;
                    }
                    try
                    {
                        harmony.Patch(ms[i], null, new HarmonyMethod(postSelect));
                        n++;
                    }
                    catch (Exception)
                    {
                    }
                }
                MelonLogger.Msg("[Opcion] candidatos a Select parcheados: " + n.ToString());
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Opcion] " + ex);
            }
        }

        // __instance se pide como Il2CppSystem.Object y se convierte a mano:
        // BaseSettingMenu es abstracta y pedirla directamente hacia fallar la
        // conversion del trampolin.
        private static void PreOnEnable(Il2CppSystem.Object __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }
                Il2Cpp.GeneralSettingsMenu general = __instance.TryCast<Il2Cpp.GeneralSettingsMenu>();
                if (general == null)
                {
                    return;      // es otro menu de ajustes
                }
                ultimoMenu = general;
                Resolver(general.GetType());
                AnadirFila(general);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Opcion] PreOnEnable: " + ex);
            }
        }

        // Sin parametros a proposito: pedir __instance obliga a Harmony a
        // convertir el puntero nativo en cada llamada, y ahi es donde
        // reventaban los trampolines. La instancia se guarda en OnEnable.
        private static void PostSelect()
        {
            try
            {
                object menu = ultimoMenu;
                if (menu == null)
                {
                    return;
                }
                // Se resuelve aqui y no en OnEnable: en ese momento todavia no
                // hay ninguna fila resaltada, asi que la busqueda fallaba y el
                // postfix se salia siempre sin hacer nada.
                if (propOpcionActual == null && propMenuStrings != null)
                {
                    ResolverOpcionActual(menu, propMenuStrings.GetValue(menu) as Il2CppStringArray);
                    if (propOpcionActual == null)
                    {
                        return;
                    }
                }
                string actual = propOpcionActual.GetValue(menu) as string;
                if (actual != Nombre)
                {
                    return;
                }
                MelonLogger.Msg("[Opcion] activada desde el menu");
                CalculadorDificultad.Lanzar();
            }
            catch (Exception)
            {
            }
        }

        // ---------------------------------------------------------- resolucion
        // Los campos de BaseMenu son protegidos y estan renombrados, asi que se
        // identifican por tipo: solo hay uno de cada forma.
        private static void Resolver(Type t)
        {
            if (resuelto)
            {
                return;
            }
            resuelto = true;

            // Por NOMBRE, no por forma. Estos campos de BaseMenu no estan
            // ofuscados, y buscarlos por tipo es una trampa: BaseSettingMenu
            // declara ademas dropdowns (TextMeshProUGUI[]) y toggleImages
            // (Image[]), que son los widgets de valor de la derecha. Al
            // resolver por forma se cogia el primero que aparecia y la fila
            // nueva acababa metida entre los dropdowns, al lado de otra opcion
            // y con pinta de palomita.
            propMenuStrings = Prop(t, "menuStrings");
            propTextObjects = Prop(t, "textObjects");
            propBackgrounds = Prop(t, "backgroundObjects");
            propTextPosDiff = Prop(t, "textPositionDifference");

            // La opcion resaltada: es la propiedad string cuyo valor coincide
            // con alguna entrada de menuStrings.
            propOpcionActual = null;
            MelonLogger.Msg("[Opcion] resuelto  menuStrings=" + Nom(propMenuStrings)
                + " textObjects=" + Nom(propTextObjects)
                + " backgrounds=" + Nom(propBackgrounds)
                + " textPosDiff=" + Nom(propTextPosDiff));

            if (propMenuStrings == null || propTextObjects == null)
            {
                MelonLogger.Error("[Opcion] faltan campos del menu; no se anade la fila");
            }
        }

        private static string Nom(PropertyInfo p)
        {
            return p == null ? "NO" : p.Name;
        }

        // Busca la propiedad por nombre subiendo por la jerarquia, porque los
        // campos protegidos de la clase base no salen en GetProperties del tipo
        // derivado con DeclaredOnly.
        private static PropertyInfo Prop(Type t, string nombre)
        {
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type actual = t;
            while (actual != null && actual != typeof(object))
            {
                PropertyInfo p = actual.GetProperty(nombre, f);
                if (p != null)
                {
                    return p;
                }
                actual = actual.BaseType;
            }
            return null;
        }

        // Se busca en caliente, cuando ya hay una fila resaltada.
        private static void ResolverOpcionActual(object menu, Il2CppStringArray filas)
        {
            if (propOpcionActual != null || filas == null)
            {
                return;
            }
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo[] props = menu.GetType().GetProperties(f);
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].PropertyType != typeof(string) || props[i].GetIndexParameters().Length != 0)
                {
                    continue;
                }
                string v;
                try { v = props[i].GetValue(menu) as string; } catch (Exception) { continue; }
                if (string.IsNullOrEmpty(v))
                {
                    continue;
                }
                for (int j = 0; j < filas.Length; j++)
                {
                    if (filas[j] == v)
                    {
                        propOpcionActual = props[i];
                        MelonLogger.Msg("[Opcion] opcion resaltada: " + props[i].Name);
                        return;
                    }
                }
            }
        }

        // -------------------------------------------------------------- fila -
        private static void AnadirFila(object menu)
        {
            if (propMenuStrings == null)
            {
                return;
            }
            Il2CppStringArray filas = propMenuStrings.GetValue(menu) as Il2CppStringArray;
            if (filas == null)
            {
                return;
            }
            ResolverOpcionActual(menu, filas);

            for (int i = 0; i < filas.Length; i++)
            {
                if (filas[i] == Nombre)
                {
                    return;      // ya estaba
                }
            }

            int filasAntes = filas.Length;
            Il2CppStringArray nuevas = new Il2CppStringArray(filas.Length + 1);
            for (int i = 0; i < filas.Length; i++)
            {
                nuevas[i] = filas[i];
            }
            nuevas[filas.Length] = Nombre;
            propMenuStrings.SetValue(menu, nuevas);

            ClonarFila(menu);

            MelonLogger.Msg("[Opcion] fila anadida: " + filasAntes.ToString()
                            + " -> " + nuevas.Length.ToString());
        }

        // Clona la ultima fila fisica. Mismo procedimiento que en la v1.0: el
        // paso vertical sale de textPositionDifference, y si viene a cero se
        // mide entre las dos ultimas filas.
        private static void ClonarFila(object menu)
        {
            try
            {
                if (propTextObjects == null)
                {
                    return;
                }
                object arrTexto = propTextObjects.GetValue(menu);
                if (arrTexto == null)
                {
                    return;
                }
                PropertyInfo len = arrTexto.GetType().GetProperty("Length");
                PropertyInfo idx = arrTexto.GetType().GetProperty("Item");
                if (len == null || idx == null)
                {
                    return;
                }
                int n = (int)len.GetValue(arrTexto);
                if (n < 1)
                {
                    return;
                }
                // Solo se clona si de verdad falta una fila fisica. Clonar
                // cuando el menu ya tenia de sobra deja una fila fantasma.
                Il2CppStringArray filas = propMenuStrings.GetValue(menu) as Il2CppStringArray;
                if (filas != null && n >= filas.Length)
                {
                    MelonLogger.Msg("[Opcion] no hace falta clonar (filas=" + n.ToString()
                                    + ", opciones=" + filas.Length.ToString() + ")");
                    return;
                }

                var ultimo = idx.GetValue(arrTexto, new object[] { n - 1 }) as Il2CppTMPro.TextMeshProUGUI;
                if (ultimo == null)
                {
                    return;
                }

                Vector3 paso = Vector3.zero;
                if (propTextPosDiff != null)
                {
                    Vector3 d = (Vector3)propTextPosDiff.GetValue(menu);
                    paso = new Vector3(0f, -d.y, 0f);
                }
                if (paso.sqrMagnitude < 0.0001f && n >= 2)
                {
                    var previo = idx.GetValue(arrTexto, new object[] { n - 2 }) as Il2CppTMPro.TextMeshProUGUI;
                    if (previo != null)
                    {
                        paso = ultimo.transform.localPosition - previo.transform.localPosition;
                    }
                }
                if (paso.sqrMagnitude < 0.0001f)
                {
                    paso = new Vector3(0f, -80f, 0f);
                }

                var nuevo = UnityEngine.Object.Instantiate(ultimo);
                nuevo.name = "CalculateDifficultyRow";
                nuevo.transform.SetParent(ultimo.transform.parent, false);
                nuevo.transform.localScale = ultimo.transform.localScale;
                nuevo.transform.localRotation = ultimo.transform.localRotation;
                nuevo.transform.localPosition = ultimo.transform.localPosition + paso;
                nuevo.transform.SetSiblingIndex(ultimo.transform.GetSiblingIndex() + 1);

                // La fila que se clona puede ser de tipo Yes/No, y entonces el
                // clon arrastra su palomita y su texto de valor. La nuestra es
                // de accion, no de ajuste: se le quitan los hijos heredados.
                LimpiarHijos(nuevo.transform);
                nuevo.text = Nombre;

                object arrNuevo = Activator.CreateInstance(arrTexto.GetType(), new object[] { n + 1 });
                PropertyInfo idxN = arrNuevo.GetType().GetProperty("Item");
                for (int i = 0; i < n; i++)
                {
                    idxN.SetValue(arrNuevo, idx.GetValue(arrTexto, new object[] { i }), new object[] { i });
                }
                idxN.SetValue(arrNuevo, nuevo, new object[] { n });
                propTextObjects.SetValue(menu, arrNuevo);

                ClonarFondo(menu, paso);
                MelonLogger.Msg("[Opcion] fila clonada, paso=" + paso.ToString());
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Opcion] clonar: " + ex);
            }
        }

        private static void LimpiarHijos(Transform t)
        {
            try
            {
                for (int i = t.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Opcion] limpiar hijos: " + ex.Message);
            }
        }

        private static void ClonarFondo(object menu, Vector3 paso)
        {
            try
            {
                if (propBackgrounds == null)
                {
                    return;
                }
                object arr = propBackgrounds.GetValue(menu);
                if (arr == null)
                {
                    return;
                }
                PropertyInfo len = arr.GetType().GetProperty("Length");
                PropertyInfo idx = arr.GetType().GetProperty("Item");
                int n = (int)len.GetValue(arr);
                if (n < 1)
                {
                    return;
                }
                var ultimo = idx.GetValue(arr, new object[] { n - 1 }) as UnityEngine.UI.Image;
                object nuevo = null;
                if (ultimo != null)
                {
                    var clon = UnityEngine.Object.Instantiate(ultimo);
                    clon.name = "CalculateDifficultyBg";
                    clon.transform.SetParent(ultimo.transform.parent, false);
                    clon.transform.localScale = ultimo.transform.localScale;
                    clon.transform.localRotation = ultimo.transform.localRotation;
                    clon.transform.localPosition = ultimo.transform.localPosition + paso;
                    clon.transform.SetSiblingIndex(ultimo.transform.GetSiblingIndex() + 1);
                    nuevo = clon;
                }
                object arrNuevo = Activator.CreateInstance(arr.GetType(), new object[] { n + 1 });
                PropertyInfo idxN = arrNuevo.GetType().GetProperty("Item");
                for (int i = 0; i < n; i++)
                {
                    idxN.SetValue(arrNuevo, idx.GetValue(arr, new object[] { i }), new object[] { i });
                }
                idxN.SetValue(arrNuevo, nuevo, new object[] { n });
                propBackgrounds.SetValue(menu, arrNuevo);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Opcion] fondo: " + ex.Message);
            }
        }
    }
}
