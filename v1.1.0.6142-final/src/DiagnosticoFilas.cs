using System;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace CloneHeroMod
{
    // Volcado puntual para averiguar de donde sale la transparencia creciente
    // de las filas de un menu de ajustes.
    //
    // Hay dos explicaciones posibles y se distinguen mirando: o el juego pone
    // un alfa distinto en cada fila (estaria en el color del texto o en el
    // CanvasRenderer), o hay una mascara con desvanecido en los bordes y las
    // filas de abajo caen dentro de esa zona.
    public static class DiagnosticoFilas
    {
        private static bool hecho;

        // Vuelca un subarbol con los componentes de cada nodo.
        private static void Detallar(StringBuilder sb, Transform t, int nivel)
        {
            string sangria = new string(' ', nivel * 2);
            sb.Append(sangria).Append("> ").Append(t.name);
            RectTransform rt = t.TryCast<RectTransform>();
            if (rt != null)
            {
                sb.Append("  size=" + rt.rect.width.ToString("0") + "x"
                          + rt.rect.height.ToString("0")
                          + "  pos=" + t.localPosition.x.ToString("0") + ","
                          + t.localPosition.y.ToString("0"));
            }
            sb.Append("  activo=").Append(t.gameObject.activeSelf.ToString()).AppendLine();

            var comps = t.gameObject.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) { continue; }
                sb.Append(sangria).Append("    · ").AppendLine(comps[i].GetIl2CppType().Name);
            }
            for (int i = 0; i < t.childCount && nivel < 4; i++)
            {
                Detallar(sb, t.GetChild(i), nivel + 1);
            }
        }

        public static void Volcar(object menu)
        {
            if (hecho || menu == null)
            {
                return;
            }
            hecho = true;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== filas de " + menu.GetType().Name + " ===");

                PropertyInfo p = FilasMenu.Prop(menu.GetType(), "textObjects");
                object arr = p != null ? p.GetValue(menu) : null;
                Transform primero = null;
                if (arr != null)
                {
                    PropertyInfo len = arr.GetType().GetProperty("Length");
                    PropertyInfo idx = arr.GetType().GetProperty("Item");
                    int n = (int)len.GetValue(arr);
                    sb.AppendLine("textObjects: " + n.ToString());
                    for (int i = 0; i < n; i++)
                    {
                        var t = idx.GetValue(arr, new object[] { i }) as Il2CppTMPro.TextMeshProUGUI;
                        if (t == null)
                        {
                            sb.AppendLine("  [" + i + "] (null)");
                            continue;
                        }
                        if (primero == null) { primero = t.transform; }
                        Color c = t.color;
                        float crAlfa = -1f;
                        try { crAlfa = t.canvasRenderer.GetAlpha(); } catch (Exception) { }
                        sb.AppendLine("  [" + i + "] '" + t.text + "'"
                            + "  obj=" + t.gameObject.name
                            + "  activo=" + t.gameObject.activeInHierarchy
                            + "  y=" + t.transform.localPosition.y.ToString("0.0")
                            + "  color=" + c.r.ToString("0.00") + "," + c.g.ToString("0.00")
                                         + "," + c.b.ToString("0.00") + ",a=" + c.a.ToString("0.000")
                            + "  tmpAlfa=" + t.alpha.ToString("0.000")
                            + "  crAlfa=" + crAlfa.ToString("0.000")
                            + "  grad=" + t.enableVertexGradient.ToString());
                    }
                }
                else
                {
                    sb.AppendLine("textObjects: NO");
                }

                // Cadena de padres: aqui saldria una mascara, un CanvasGroup o
                // un ScrollRect si el desvanecido no esta en las filas.
                sb.AppendLine();
                sb.AppendLine("--- jerarquia hacia arriba ---");
                Transform t2 = primero;
                int nivel = 0;
                while (t2 != null && nivel < 12)
                {
                    sb.Append(new string(' ', nivel * 2)).Append(t2.name);
                    RectTransform rt = t2.TryCast<RectTransform>();
                    if (rt != null)
                    {
                        Rect r = rt.rect;
                        sb.Append("  rect=(" + r.x.ToString("0") + "," + r.y.ToString("0")
                                  + " " + r.width.ToString("0") + "x" + r.height.ToString("0") + ")");
                    }
                    sb.AppendLine();

                    var comps = t2.gameObject.GetComponents<Component>();
                    for (int i = 0; i < comps.Length; i++)
                    {
                        if (comps[i] == null) { continue; }
                        string nom = comps[i].GetIl2CppType().Name;
                        sb.Append(new string(' ', nivel * 2 + 2)).Append("· ").Append(nom);

                        var cg = comps[i].TryCast<CanvasGroup>();
                        if (cg != null) { sb.Append("  alpha=" + cg.alpha.ToString("0.000")); }

                        var mask = comps[i].TryCast<UnityEngine.UI.RectMask2D>();
                        if (mask != null)
                        {
                            sb.Append("  softness=" + mask.softness.ToString()
                                      + "  padding=" + mask.padding.ToString());
                        }
                        sb.AppendLine();
                    }
                    t2 = t2.parent;
                    nivel++;
                }

                // Como es de verdad una fila: el contenedor, no la etiqueta.
                // Las filas del juego cuelgan de un VerticalLayoutGroup, asi
                // que para anadir una hay que clonar el contenedor entero.
                sb.AppendLine();
                sb.AppendLine("--- contenedores dentro del layout ---");
                Transform layout = null;
                if (primero != null && primero.parent != null)
                {
                    layout = primero.parent.parent;      // main_container
                }
                if (layout != null)
                {
                    sb.AppendLine(layout.name + "  hijos=" + layout.childCount.ToString());
                    var vlg = layout.gameObject.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
                    if (vlg != null)
                    {
                        sb.AppendLine("  VerticalLayoutGroup  spacing=" + vlg.spacing.ToString("0.0")
                            + "  padding=" + vlg.padding.top + "," + vlg.padding.bottom
                            + "  childForceExpandHeight=" + vlg.childForceExpandHeight.ToString()
                            + "  childControlHeight=" + vlg.childControlHeight.ToString());
                    }
                    for (int i = 0; i < layout.childCount; i++)
                    {
                        Transform h = layout.GetChild(i);
                        RectTransform hr = h.TryCast<RectTransform>();
                        sb.Append("  [" + i + "] " + h.name
                            + "  activo=" + h.gameObject.activeSelf.ToString()
                            + "  y=" + h.localPosition.y.ToString("0"));
                        if (hr != null)
                        {
                            sb.Append("  size=" + hr.rect.width.ToString("0") + "x"
                                      + hr.rect.height.ToString("0"));
                        }
                        sb.AppendLine();
                    }

                    // El detalle de los dos ultimos contenedores del juego:
                    // que hijos y que componentes llevan.
                    for (int i = Math.Max(0, layout.childCount - 3); i < layout.childCount; i++)
                    {
                        sb.AppendLine();
                        Detallar(sb, layout.GetChild(i), 0);
                    }
                }

                string ruta = Path.Combine(MelonEnvironment.MelonLoaderDirectory,
                                           "filas-" + menu.GetType().Name + ".txt");
                File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false));
                MelonLogger.Msg("[DiagFilas] volcado en " + ruta);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[DiagFilas] " + ex);
            }
        }
    }
}
