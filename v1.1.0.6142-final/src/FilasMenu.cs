using System;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace CloneHeroMod
{
    // Utilidades para anadir filas a los menus de ajustes.
    //
    // Los campos de BaseMenu (menuStrings, textObjects, backgroundObjects,
    // textPositionDifference) NO estan ofuscados: se piden por nombre exacto,
    // subiendo por la jerarquia porque son protegidos de la clase base.
    //
    // Buscarlos por tipo es una trampa: BaseSettingMenu declara ademas
    // dropdowns (TextMeshProUGUI[]) y toggleImages (Image[]), que son los
    // widgets de valor de la derecha; al resolver por forma la fila nueva
    // acababa metida entre los dropdowns.
    public static class FilasMenu
    {
        public static PropertyInfo Prop(Type t, string nombre)
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

        public static Il2CppStringArray Opciones(object menu)
        {
            PropertyInfo p = Prop(menu.GetType(), "menuStrings");
            return p != null ? p.GetValue(menu) as Il2CppStringArray : null;
        }

        public static int IndiceDe(object menu, string prefijo)
        {
            Il2CppStringArray filas = Opciones(menu);
            if (filas == null)
            {
                return -1;
            }
            for (int i = 0; i < filas.Length; i++)
            {
                if (filas[i] != null && filas[i].StartsWith(prefijo, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        public static void CambiarTexto(object menu, int indice, string texto)
        {
            Il2CppStringArray filas = Opciones(menu);
            if (filas != null && indice >= 0 && indice < filas.Length)
            {
                filas[indice] = texto;
            }
        }

        // Anade una fila al final. Devuelve true si la creo (false si ya estaba).
        public static bool Anadir(object menu, string texto, string prefijo)
        {
            try
            {
                PropertyInfo propOpciones = Prop(menu.GetType(), "menuStrings");
                if (propOpciones == null)
                {
                    return false;
                }
                Il2CppStringArray filas = propOpciones.GetValue(menu) as Il2CppStringArray;
                if (filas == null)
                {
                    return false;
                }
                for (int i = 0; i < filas.Length; i++)
                {
                    if (filas[i] != null && filas[i].StartsWith(prefijo, StringComparison.Ordinal))
                    {
                        return false;      // ya estaba
                    }
                }

                Il2CppStringArray nuevas = new Il2CppStringArray(filas.Length + 1);
                for (int i = 0; i < filas.Length; i++)
                {
                    nuevas[i] = filas[i];
                }
                nuevas[filas.Length] = texto;
                propOpciones.SetValue(menu, nuevas);

                Clonar(menu, nuevas.Length);
                MelonLogger.Msg("[Filas] '" + texto + "' anadida ("
                    + filas.Length.ToString() + " -> " + nuevas.Length.ToString() + ")");
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Filas] " + ex);
                return false;
            }
        }

        // Clona la ultima fila fisica si hacen falta mas.
        private static void Clonar(object menu, int filasNecesarias)
        {
            PropertyInfo propTextos = Prop(menu.GetType(), "textObjects");
            PropertyInfo propFondos = Prop(menu.GetType(), "backgroundObjects");
            PropertyInfo propPaso = Prop(menu.GetType(), "textPositionDifference");
            if (propTextos == null)
            {
                return;
            }
            object arr = propTextos.GetValue(menu);
            if (arr == null)
            {
                return;
            }
            PropertyInfo len = arr.GetType().GetProperty("Length");
            PropertyInfo idx = arr.GetType().GetProperty("Item");
            if (len == null || idx == null)
            {
                return;
            }
            int n = (int)len.GetValue(arr);
            if (n < 1 || n >= filasNecesarias)
            {
                return;      // ya hay filas fisicas de sobra
            }

            var ultimo = idx.GetValue(arr, new object[] { n - 1 }) as Il2CppTMPro.TextMeshProUGUI;
            if (ultimo == null)
            {
                return;
            }

            Vector3 paso = Vector3.zero;
            if (propPaso != null)
            {
                Vector3 d = (Vector3)propPaso.GetValue(menu);
                paso = new Vector3(0f, -d.y, 0f);
            }
            if (paso.sqrMagnitude < 0.0001f && n >= 2)
            {
                var previo = idx.GetValue(arr, new object[] { n - 2 }) as Il2CppTMPro.TextMeshProUGUI;
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
            nuevo.name = "FilaMod";
            nuevo.transform.SetParent(ultimo.transform.parent, false);
            nuevo.transform.localScale = ultimo.transform.localScale;
            nuevo.transform.localRotation = ultimo.transform.localRotation;
            nuevo.transform.localPosition = ultimo.transform.localPosition + paso;
            nuevo.transform.SetSiblingIndex(ultimo.transform.GetSiblingIndex() + 1);
            // La fila clonada puede venir de una de tipo Yes/No y arrastrar su
            // palomita y su texto de valor; la nuestra es de accion.
            for (int i = nuevo.transform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(nuevo.transform.GetChild(i).gameObject);
            }

            object arrNuevo = Activator.CreateInstance(arr.GetType(), new object[] { n + 1 });
            PropertyInfo idxN = arrNuevo.GetType().GetProperty("Item");
            for (int i = 0; i < n; i++)
            {
                idxN.SetValue(arrNuevo, idx.GetValue(arr, new object[] { i }), new object[] { i });
            }
            idxN.SetValue(arrNuevo, nuevo, new object[] { n });
            propTextos.SetValue(menu, arrNuevo);

            ClonarFondo(menu, propFondos, paso);
        }

        private static void ClonarFondo(object menu, PropertyInfo propFondos, Vector3 paso)
        {
            try
            {
                if (propFondos == null)
                {
                    return;
                }
                object arr = propFondos.GetValue(menu);
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
                    clon.name = "FilaModBg";
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
                propFondos.SetValue(menu, arrNuevo);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Filas] fondo: " + ex.Message);
            }
        }
    }
}
