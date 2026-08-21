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
                // El resultado de la ultima comprobacion no se arrastra entre
                // visitas al menu (salvo el aviso de reiniciar).
                Actualizador.OlvidarResultado();
                FilasMenu.Escribir(general, Actualizador.Etiqueta, Actualizador.Texto());
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
                if (actual == Nombre)
                {
                    MelonLogger.Msg("[Opcion] activada desde el menu");
                    CalculadorDificultad.Lanzar();
                    return;
                }
                // El texto de esta fila cambia segun como vaya la descarga, asi
                // que se compara por el principio.
                if (actual != null
                    && actual.StartsWith(Actualizador.Etiqueta, StringComparison.Ordinal))
                {
                    Actualizador.Lanzar();
                    FilasMenu.Escribir(menu, Actualizador.Etiqueta, Actualizador.Texto());
                }
            }
            catch (Exception)
            {
            }
        }

        // La descarga corre en otro hilo y ahi no se puede tocar Unity: cuando
        // termina deja aviso y la fila se reescribe desde el hilo principal.
        // Fuera de ese momento esto es una comparacion de un bool.
        public static void Tick()
        {
            if (!Actualizador.Consumir() || ultimoMenu == null)
            {
                return;
            }
            FilasMenu.Escribir(ultimoMenu, Actualizador.Etiqueta, Actualizador.Texto());
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
        // El montaje de la fila lo hace FilasMenu: las filas de estos menus son
        // contenedores dentro de un VerticalLayoutGroup, y clonar solo la
        // etiqueta la dejaba fuera del layout y del alcance del scroll, pegada
        // al borde difuminado de la mascara.
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
            FilasMenu.Anadir(menu, Nombre, Nombre);
            FilasMenu.Anadir(menu, Actualizador.Texto(), Actualizador.Etiqueta);
        }
    }
}
