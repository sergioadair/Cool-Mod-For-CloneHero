using System;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;

namespace CloneHeroMod
{
    // Volcado dirigido para anadir la fila "Calculate Difficulty" a
    // Settings > General.
    //
    // Hace falta saber dos cosas que el volcado estatico no da: cual de los
    // metodos de GeneralSettingsMenu es el de "Select" (los metodos no
    // conservan el nombre ofuscado y hay varios candidatos con la misma firma),
    // y como estan las filas del menu en tiempo de ejecucion.
    public static class DiagnosticoMenu
    {
        public static void Volcar()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Diagnostico menu General ===");
            sb.AppendLine("fecha: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            try { Instancia(sb); } catch (Exception ex) { sb.AppendLine("instancia: " + ex); }
            try { Metodos(sb); } catch (Exception ex) { sb.AppendLine("metodos: " + ex); }

            string destino = Path.Combine(MelonEnvironment.MelonLoaderDirectory, "diagnostico-menu.txt");
            File.WriteAllText(destino, sb.ToString(), Encoding.UTF8);
            MelonLogger.Msg("[Menu] diagnostico escrito en " + destino);
        }

        // Las filas del menu viven en campos serializados de BaseMenu. Se busca
        // la instancia incluso desactivada, porque el menu de ajustes no esta
        // activo mientras estamos en el menu principal.
        private static void Instancia(StringBuilder sb)
        {
            sb.AppendLine("---------- INSTANCIA ----------");
            var todas = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.GeneralSettingsMenu>();
            sb.AppendLine("instancias encontradas: " + (todas == null ? 0 : todas.Length));
            if (todas == null || todas.Length == 0)
            {
                return;
            }
            Il2Cpp.GeneralSettingsMenu m = todas[0];
            Type t = m.GetType();
            sb.AppendLine("tipo: " + t.FullName);

            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo[] props = t.GetProperties(f);
            for (int i = 0; i < props.Length; i++)
            {
                string n = props[i].PropertyType.Name;
                if (!n.Contains("StringArray") && !n.Contains("Vector3")
                    && !n.Contains("TextMeshProUGUI") && !n.Contains("Image"))
                {
                    continue;
                }
                object v;
                try { v = props[i].GetValue(m); } catch (Exception) { continue; }
                sb.Append("  ").Append(n).Append("  ").Append(props[i].Name).Append("  =  ");
                sb.AppendLine(Describir(v));
            }
            sb.AppendLine();
        }

        private static string Describir(object v)
        {
            if (v == null)
            {
                return "(null)";
            }
            PropertyInfo len = v.GetType().GetProperty("Length");
            if (len == null)
            {
                return v.ToString();
            }
            int n = (int)len.GetValue(v);
            StringBuilder sb = new StringBuilder();
            sb.Append("len=").Append(n);
            PropertyInfo idx = v.GetType().GetProperty("Item");
            if (idx != null && n > 0 && n < 80)
            {
                sb.Append("  [");
                for (int i = 0; i < n; i++)
                {
                    object e;
                    try { e = idx.GetValue(v, new object[] { i }); } catch (Exception) { break; }
                    if (i > 0) { sb.Append(" | "); }
                    sb.Append(e == null ? "null" : e.ToString());
                }
                sb.Append(']');
            }
            return sb.ToString();
        }

        // Todos los metodos sin parametros que devuelven void: el de "Select"
        // esta entre ellos.
        private static void Metodos(StringBuilder sb)
        {
            sb.AppendLine("---------- METODOS CANDIDATOS A Select ----------");
            Type t = Ofuscado.Tipo("GeneralSettingsMenu");
            if (t == null)
            {
                sb.AppendLine("tipo no encontrado");
                return;
            }
            Type actual = t;
            while (actual != null && actual != typeof(object))
            {
                sb.AppendLine("== " + actual.Name + " ==");
                MethodInfo[] ms = actual.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                    | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                for (int i = 0; i < ms.Length; i++)
                {
                    MethodInfo mi = ms[i];
                    if (mi.ReturnType != typeof(void) || mi.GetParameters().Length != 0)
                    {
                        continue;
                    }
                    if (mi.IsSpecialName)
                    {
                        continue;
                    }
                    sb.Append("  ").Append(mi.Name);
                    if (mi.IsVirtual) { sb.Append("   [virtual]"); }
                    sb.AppendLine();
                }
                actual = actual.BaseType;
            }
            sb.AppendLine();
        }
    }
}
