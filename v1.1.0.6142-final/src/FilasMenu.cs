using System;
using System.Collections.Generic;
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
        // Quita el degradado del final de la lista.
        //
        // Lo que se ve tenue al final de un menu de ajustes no es transparencia
        // de las filas —su alfa es 1— sino una imagen de degradado superpuesta:
        // BaseSettingMenu.fadeImage.
        //
        // SettingsMenu tiene dos sprites para ella, fadeGradient y
        // noFadeGradient, y el primer intento fue cambiar uno por el otro.
        // No sirve: se llaman FadedSettingsMenuGradient y SettingsMenuGradient,
        // y AMBOS son degradados; el segundo solo desvanece menos. Lo que de
        // verdad quita el efecto es apagar la imagen.
        //
        // El juego reserva dos filas de holgura al final de cada contenedor
        // para que su ultima opcion no caiga en esa banda. Como nosotros
        // anadimos filas y se la comemos, la alternativa era agrandar el
        // contenedor; se intento tres veces y siempre acababa deformando las
        // filas, porque segun el momento cuelgan de una caja de alto fijo o
        // directamente del contenedor y ancladas en estiramiento. Apagar la
        // imagen no toca ni geometria ni anclajes.

        // Un aviso por menu, no uno global: con uno solo no se veia cual de
        // los cuatro submenus no habia quedado bien.
        private static readonly List<string> avisados = new List<string>();

        public static void QuitarDegradado(Il2Cpp.BaseSettingMenu menu)
        {
            if (menu == null)
            {
                return;
            }
            string clave;
            try { clave = menu.GetIl2CppType().Name; }
            catch (Exception) { clave = "?"; }
            bool avisar = !avisados.Contains(clave);

            try
            {
                int apagadas = Apagar(menu.fadeImage) ? 1 : 0;

                // Por si la imagen que se ve no es la que el menu declara:
                // cualquier otra que muestre uno de los dos degradados.
                Il2Cpp.SettingsMenu padre = menu.settingsMenu != null
                    ? menu.settingsMenu.TryCast<Il2Cpp.SettingsMenu>()
                    : null;
                if (padre != null)
                {
                    var imagenes = menu.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                    for (int i = 0; i < imagenes.Length; i++)
                    {
                        var img = imagenes[i];
                        if (img == null || img.sprite == null)
                        {
                            continue;
                        }
                        if (img.sprite == padre.fadeGradient
                            || img.sprite == padre.noFadeGradient)
                        {
                            apagadas += Apagar(img) ? 1 : 0;
                        }
                    }
                }

                if (avisar)
                {
                    avisados.Add(clave);
                    MelonLogger.Msg("[Filas] degradado en " + clave + ": "
                        + apagadas.ToString() + " imagen(es) apagada(s)");
                }
            }
            catch (Exception ex)
            {
                if (avisar)
                {
                    avisados.Add(clave);
                    MelonLogger.Warning("[Filas] degradado en " + clave + ": " + ex.Message);
                }
            }
        }

        private static bool Apagar(UnityEngine.UI.Image img)
        {
            if (img == null || !img.enabled)
            {
                return false;
            }
            img.enabled = false;
            return true;
        }

        // Detector: que nuestras filas ensenen de verdad su texto.
        //
        // Nacio de un fallo que al principio no se pudo repetir: al entrar a
        // Settings > General nada mas cargar el juego, tres de nuestras filas
        // mostraban el texto de otra opcion y no se podia bajar hasta ellas. El
        // detector lo cazo a la primera:
        //
        //     fila 25: deberia decir 'Calculate Difficulty'
        //              y dice 'Remote Player Righty Flip'
        //
        // La causa esta explicada en Anadir. Se deja puesto porque es barato
        // —corre una vez por apertura de menu— y porque avisa en el log en vez
        // de dejar al jugador con un menu roto sin saber por que.
        //
        // SOLO se miran NUESTRAS filas. Las del juego no valen para esto: a
        // algunas les ensena un texto distinto del que tiene en menuStrings
        // ("Analytics Privacy Dialog" se ve como "ANALYTICS CONSENT DIALOG"),
        // asi que compararlas daba avisos falsos.
        public static void Comprobar(object menu, string etiqueta, string[] nuestras)
        {
            try
            {
                Il2CppStringArray filas = Opciones(menu);
                PropertyInfo p = Prop(menu.GetType(), "textObjects");
                object arr = p != null ? p.GetValue(menu) : null;
                if (filas == null || arr == null || nuestras == null)
                {
                    return;
                }
                PropertyInfo len = arr.GetType().GetProperty("Length");
                PropertyInfo idx = arr.GetType().GetProperty("Item");
                int n = (int)len.GetValue(arr);
                if (n < filas.Length)
                {
                    MelonLogger.Warning("[Filas] " + etiqueta + ": " + filas.Length.ToString()
                        + " textos pero solo " + n.ToString() + " filas fisicas");
                }
                for (int i = 0; i < n && i < filas.Length; i++)
                {
                    string esperado = filas[i];
                    if (string.IsNullOrEmpty(esperado) || !Nuestra(esperado, nuestras))
                    {
                        continue;
                    }
                    var t = idx.GetValue(arr, new object[] { i }) as Il2CppTMPro.TextMeshProUGUI;
                    string visto = t != null ? (t.text ?? "") : "(sin etiqueta)";
                    if (!string.Equals(esperado.Trim(), visto.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        MelonLogger.Warning("[Filas] " + etiqueta + " fila " + i.ToString()
                            + ": deberia decir '" + esperado + "' y dice '" + visto + "'");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Filas] comprobar: " + ex.Message);
            }
        }

        private static bool Nuestra(string texto, string[] nuestras)
        {
            for (int i = 0; i < nuestras.Length; i++)
            {
                if (nuestras[i] != null
                    && texto.StartsWith(nuestras[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

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

        // Reescribe una fila por su prefijo: el texto que guarda el menu y el
        // que se ve. Hacen falta los dos, porque menuStrings solo se relee al
        // redibujar el menu entero.
        public static void Escribir(object menu, string prefijo, string texto)
        {
            try
            {
                if (menu == null)
                {
                    return;
                }
                int indice = IndiceDe(menu, prefijo);
                if (indice < 0)
                {
                    return;
                }
                CambiarTexto(menu, indice, texto);

                PropertyInfo p = Prop(menu.GetType(), "textObjects");
                object arr = p != null ? p.GetValue(menu) : null;
                if (arr == null)
                {
                    return;
                }
                PropertyInfo len = arr.GetType().GetProperty("Length");
                PropertyInfo idx = arr.GetType().GetProperty("Item");
                if ((int)len.GetValue(arr) <= indice)
                {
                    return;
                }
                var t = idx.GetValue(arr, new object[] { indice }) as Il2CppTMPro.TextMeshProUGUI;
                if (t != null)
                {
                    t.text = texto;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Filas] escribir: " + ex.Message);
            }
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

                // PRIMERO LA FILA FISICA Y SOLO DESPUES EL TEXTO. Al reves
                // se rompe, y costo encontrarlo porque solo pasa si entras a
                // Settings nada mas cargar el juego: en ese momento el menu
                // todavia no tiene sus etiquetas montadas, Clonar se salia por
                // uno de sus return sin decir nada, y menuStrings crecia igual.
                // Resultado: 29 textos sobre 25 filas, tres opciones ensenando
                // el texto de otra y sin poder bajar hasta ellas.
                //
                // Si no se puede clonar, no se anade nada y se reintenta en la
                // siguiente apertura del menu, que es cuando ya esta montado.
                Etiquetas(menu, filas.Length);
                if (!Clonar(menu, filas.Length + 1, texto))
                {
                    MelonLogger.Warning("[Filas] '" + texto + "' no se anade todavia:"
                        + " el menu aun no tiene filas que clonar");
                    return false;
                }

                Il2CppStringArray nuevas = new Il2CppStringArray(filas.Length + 1);
                for (int i = 0; i < filas.Length; i++)
                {
                    nuevas[i] = filas[i];
                }
                nuevas[filas.Length] = texto;
                propOpciones.SetValue(menu, nuevas);

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

        // Cuantas etiquetas fisicas hay frente a cuantos textos. Se deja una vez
        // por menu: es el numero que explicaba por que unas veces se clonaba y
        // otras no.
        private static readonly List<string> contadas = new List<string>();

        private static void Etiquetas(object menu, int textos)
        {
            try
            {
                string clave = menu.GetType().Name;
                if (contadas.Contains(clave))
                {
                    return;
                }
                contadas.Add(clave);
                PropertyInfo p = Prop(menu.GetType(), "textObjects");
                object arr = p != null ? p.GetValue(menu) : null;
                int n = arr != null
                    ? (int)arr.GetType().GetProperty("Length").GetValue(arr) : -1;
                MelonLogger.Msg("[Filas] " + clave + ": " + textos.ToString()
                    + " textos, " + n.ToString() + " etiquetas fisicas");
                if (arr != null && n > 0)
                {
                    PropertyInfo idx = arr.GetType().GetProperty("Item");
                    Jerarquia(idx.GetValue(arr, new object[] { 0 })
                        as Il2CppTMPro.TextMeshProUGUI, "primera");
                    Jerarquia(idx.GetValue(arr, new object[] { n - 1 })
                        as Il2CppTMPro.TextMeshProUGUI, "ultima");
                }
            }
            catch (Exception)
            {
            }
        }

        // De donde cuelga una etiqueta, con tamanos y componentes. Es la misma
        // sonda que resolvio el panel de Song Options: mirar la jerarquia de
        // verdad en vez de deducirla.
        private static void Jerarquia(Il2CppTMPro.TextMeshProUGUI t, string cual)
        {
            try
            {
                if (t == null)
                {
                    MelonLogger.Msg("[Filas]   " + cual + ": no hay etiqueta");
                    return;
                }
                Transform tr = t.transform;
                for (int nivel = 0; nivel < 4 && tr != null; nivel++)
                {
                    RectTransform r = tr.TryCast<RectTransform>();
                    string tam = r != null
                        ? r.rect.width.ToString("0") + "x" + r.rect.height.ToString("0")
                        : "-";
                    string comps = "";
                    var cs = tr.gameObject.GetComponents<Component>();
                    for (int i = 0; i < cs.Length; i++)
                    {
                        if (cs[i] != null)
                        {
                            comps += cs[i].GetIl2CppType().Name + " ";
                        }
                    }
                    MelonLogger.Msg("[Filas]   " + cual + " " + nivel.ToString() + " "
                        + tr.name + " [" + tam + "] hermano="
                        + tr.GetSiblingIndex().ToString() + " " + comps);
                    tr = tr.parent;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Filas] jerarquia: " + ex.Message);
            }
        }

        // Crea la fila fisica que falta, si falta. Devuelve si al terminar hay
        // al menos filasNecesarias: quien llama NO debe tocar menuStrings si
        // sale false.
        private static bool Clonar(object menu, int filasNecesarias, string texto)
        {
            PropertyInfo propTextos = Prop(menu.GetType(), "textObjects");
            PropertyInfo propFondos = Prop(menu.GetType(), "backgroundObjects");
            if (propTextos == null)
            {
                return false;
            }
            object arr = propTextos.GetValue(menu);
            if (arr == null)
            {
                return false;
            }
            PropertyInfo len = arr.GetType().GetProperty("Length");
            PropertyInfo idx = arr.GetType().GetProperty("Item");
            if (len == null || idx == null)
            {
                return false;
            }
            int n = (int)len.GetValue(arr);
            if (n >= filasNecesarias)
            {
                return true;      // ya hay filas fisicas de sobra
            }
            if (n < 1)
            {
                return false;     // el menu no tiene ni una fila que copiar
            }

            var ultimo = idx.GetValue(arr, new object[] { n - 1 }) as Il2CppTMPro.TextMeshProUGUI;
            if (ultimo == null)
            {
                return false;     // las etiquetas aun no estan montadas
            }

            // Una fila del menu NO es la etiqueta: es un contenedor de 1260x80
            // (recuadro de fondo + etiqueta + widget de valor) que cuelga de un
            // VerticalLayoutGroup, con un ScrollRect por encima y una mascara
            // suave que difumina lo que se acerca al borde.
            //
            // Clonar solo la etiqueta y colocarla a mano la dejaba DENTRO del
            // contenedor de la ultima fila, desbordando por debajo: el layout no
            // la contaba, el scroll no llegaba hasta ella y se quedaba fija en
            // la franja difuminada del fondo.
            Transform contenedor = ultimo.transform.parent;
            Transform layout = contenedor != null ? contenedor.parent : null;
            if (layout != null
                && layout.gameObject.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() != null)
            {
                ClonarContenedor(menu, propTextos, propFondos, arr, n,
                                 ultimo, contenedor, layout, texto);
                return Suficientes(propTextos, menu, filasNecesarias);
            }
            ClonarSuelta(menu, propTextos, propFondos, arr, n, ultimo);
            return Suficientes(propTextos, menu, filasNecesarias);
        }

        // Se vuelve a leer el array en vez de fiarse: los clonadores tienen sus
        // propios caminos de error y lo unico que importa es el resultado.
        private static bool Suficientes(PropertyInfo propTextos, object menu, int hacen)
        {
            try
            {
                object arr = propTextos.GetValue(menu);
                if (arr == null)
                {
                    return false;
                }
                return (int)arr.GetType().GetProperty("Length").GetValue(arr) >= hacen;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Anade un contenedor de fila al final del layout, como hermano de los
        // del juego: asi lo coloca el propio VerticalLayoutGroup y entra en el
        // recorrido del scroll.
        private static void ClonarContenedor(object menu, PropertyInfo propTextos,
            PropertyInfo propFondos, object arr, int n, Il2CppTMPro.TextMeshProUGUI ultimo,
            Transform contenedor, Transform layout, string texto)
        {
            try
            {
                int indiceEtiqueta = ultimo.transform.GetSiblingIndex();

                GameObject clon = UnityEngine.Object.Instantiate(contenedor.gameObject);
                clon.name = "mod_row";
                clon.transform.SetParent(layout, false);
                clon.transform.SetSiblingIndex(contenedor.GetSiblingIndex() + 1);

                // Del clon se conservan el recuadro de fondo y la etiqueta. El
                // widget de valor (interruptor, desplegable, deslizador) sobra:
                // nuestras filas llevan el valor en el propio texto.
                Il2CppTMPro.TextMeshProUGUI etiqueta = null;
                UnityEngine.UI.Image fondo = null;
                Transform tr = clon.transform;
                for (int i = tr.childCount - 1; i >= 0; i--)
                {
                    Transform h = tr.GetChild(i);
                    if (i == indiceEtiqueta)
                    {
                        etiqueta = h.GetComponent<Il2CppTMPro.TextMeshProUGUI>();
                        continue;
                    }
                    if (fondo == null && i == 0
                        && h.GetComponent<Il2CppTMPro.TextMeshProUGUI>() == null)
                    {
                        fondo = h.GetComponent<UnityEngine.UI.Image>();
                        if (fondo != null)
                        {
                            continue;
                        }
                    }
                    UnityEngine.Object.Destroy(h.gameObject);
                }
                if (etiqueta == null)
                {
                    MelonLogger.Warning("[Filas] el clon no trae etiqueta; se descarta");
                    UnityEngine.Object.Destroy(clon);
                    ClonarSuelta(menu, propTextos, propFondos, arr, n, ultimo);
                    return;
                }
                etiqueta.text = texto;

                // El contenido del scroll no lo dimensiona el layout (no lleva
                // ContentSizeFitter), asi que hay que estirarlo a mano. El juego
                // deja una fila de holgura al final justamente para que la
                // ultima no caiga en el degradado de la mascara; sin estirar,
                // nuestra fila se come esa holgura.
                Estirar(layout, contenedor);

                Ampliar(propTextos, menu, arr, n, etiqueta);
                if (propFondos != null && fondo != null)
                {
                    object arrF = propFondos.GetValue(menu);
                    if (arrF != null)
                    {
                        PropertyInfo lenF = arrF.GetType().GetProperty("Length");
                        Ampliar(propFondos, menu, arrF, (int)lenF.GetValue(arrF), fondo);
                    }
                }

                UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(layout.TryCast<RectTransform>());
                MelonLogger.Msg("[Filas] contenedor anadido al layout de " + layout.name
                    + " (" + (n + 1).ToString() + " filas fisicas)");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Filas] clonar contenedor: " + ex);
            }
        }

        // NO SE TOCA EL ALTO DE main_container. Se intento —otra vez— y volvio
        // a romper el menu, igual que las tres veces anteriores que ya avisaba
        // el comentario de mas abajo. La sonda de jerarquia explico por fin por
        // que hay un limite real de filas en los menus de ajustes:
        //
        //     main_container [1280x2160]  VerticalLayoutGroup UnrollChildMenuitems
        //     Options        [1280x720]   Mask
        //     General Settings Menu       ScrollRect
        //
        // 2160 son 27 filas de 80 clavadas, y main_container NO lleva
        // ContentSizeFitter: no crece solo. General trae 25 opciones, asi que
        // caben DOS nuestras y ni una mas. Con dos funciono siempre; a la
        // tercera el menu se queda corto de scroll y a la cuarta se deforma.
        //
        // De ahi que las opciones de lote se hayan movido a Song Options, que
        // no estira nada: desplaza una ventana de siete filas sobre la lista y
        // le da igual cuantas haya.

        // Crece el contenido del scroll la altura de una fila.
        private static void Estirar(Transform layout, Transform contenedor)
        {
            try
            {
                RectTransform rl = layout.TryCast<RectTransform>();
                RectTransform rc = contenedor.TryCast<RectTransform>();
                if (rl == null || rc == null || rc.rect.height <= 0f)
                {
                    return;
                }
                rl.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                                             rl.rect.height + rc.rect.height);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Filas] estirar: " + ex.Message);
            }
        }

        // Copia un array de Il2Cpp con un elemento mas al final.
        private static void Ampliar(PropertyInfo propiedad, object menu, object arr,
                                    int n, object nuevo)
        {
            PropertyInfo idx = arr.GetType().GetProperty("Item");
            object arrNuevo = Activator.CreateInstance(arr.GetType(), new object[] { n + 1 });
            PropertyInfo idxN = arrNuevo.GetType().GetProperty("Item");
            for (int i = 0; i < n; i++)
            {
                idxN.SetValue(arrNuevo, idx.GetValue(arr, new object[] { i }), new object[] { i });
            }
            idxN.SetValue(arrNuevo, nuevo, new object[] { n });
            propiedad.SetValue(menu, arrNuevo);
        }

        // Camino antiguo, por si algun menu no monta sus filas sobre un
        // VerticalLayoutGroup: clona la etiqueta suelta y la coloca a mano.
        private static void ClonarSuelta(object menu, PropertyInfo propTextos,
            PropertyInfo propFondos, object arr, int n, Il2CppTMPro.TextMeshProUGUI ultimo)
        {
            PropertyInfo propPaso = Prop(menu.GetType(), "textPositionDifference");
            PropertyInfo idx = arr.GetType().GetProperty("Item");

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
