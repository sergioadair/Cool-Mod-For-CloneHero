using System;
using System.Collections.Generic;
using System.Reflection;

namespace CloneHeroMod
{
    // Puente entre los nombres ofuscados del juego y los nombres que acaba
    // teniendo el ensamblado interop.
    //
    // Il2CppInterop NO conserva los identificadores ofuscados del juego (son
    // letras modificadoras Unicode, U+02B2-U+02C1): renombra cada tipo y cada
    // miembro, y guarda el nombre original en un atributo
    // [ObfuscatedName("...")]. Por eso buscar por Type.Name no encuentra nada.
    //
    // Aqui se construye el indice inverso una sola vez y se resuelve por el
    // nombre real del juego, que es el que aparece en el volcado de
    // Il2CppDumper y el que documentamos en REPLICAR-MOD.md.
    public static class Ofuscado
    {
        private static Assembly ensamblado;
        private static Dictionary<string, Type> tiposPorNombreReal;

        public static Assembly Ensamblado
        {
            get
            {
                if (ensamblado == null)
                {
                    Assembly[] todos = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < todos.Length; i++)
                    {
                        if (todos[i].GetName().Name == "Il2CppCloneHero")
                        {
                            ensamblado = todos[i];
                            break;
                        }
                    }
                }
                return ensamblado;
            }
        }

        // Lee el valor de [ObfuscatedName]. Se hace por reflexion sobre la
        // propia instancia del atributo en vez de referenciar el tipo, para no
        // depender de como se llame su propiedad en esta version de
        // Il2CppInterop.
        public static string NombreReal(MemberInfo miembro)
        {
            object[] attrs = miembro.GetCustomAttributes(false);
            for (int i = 0; i < attrs.Length; i++)
            {
                object a = attrs[i];
                if (a == null || a.GetType().Name != "ObfuscatedNameAttribute")
                {
                    continue;
                }
                Type ta = a.GetType();
                PropertyInfo[] props = ta.GetProperties();
                for (int j = 0; j < props.Length; j++)
                {
                    if (props[j].PropertyType == typeof(string) && props[j].GetIndexParameters().Length == 0)
                    {
                        object v = props[j].GetValue(a);
                        if (v != null)
                        {
                            return (string)v;
                        }
                    }
                }
                FieldInfo[] campos = ta.GetFields();
                for (int j = 0; j < campos.Length; j++)
                {
                    if (campos[j].FieldType == typeof(string))
                    {
                        object v = campos[j].GetValue(a);
                        if (v != null)
                        {
                            return (string)v;
                        }
                    }
                }
            }
            return null;
        }

        private static void ConstruirIndice()
        {
            if (tiposPorNombreReal != null)
            {
                return;
            }
            tiposPorNombreReal = new Dictionary<string, Type>(StringComparer.Ordinal);
            Assembly ens = Ensamblado;
            if (ens == null)
            {
                return;
            }
            Type[] tipos;
            try
            {
                tipos = ens.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                tipos = ex.Types;
            }
            for (int i = 0; i < tipos.Length; i++)
            {
                Type t = tipos[i];
                if (t == null)
                {
                    continue;
                }
                string real = NombreReal(t);
                if (real != null && !tiposPorNombreReal.ContainsKey(real))
                {
                    tiposPorNombreReal[real] = t;
                }
                // Los tipos sin ofuscar (SongEntry, SongOptions...) se indexan
                // por su propio nombre, asi se resuelven igual desde fuera.
                if (!tiposPorNombreReal.ContainsKey(t.Name))
                {
                    tiposPorNombreReal[t.Name] = t;
                }
            }
        }

        // Devuelve el Type del interop a partir del nombre ofuscado real.
        public static Type Tipo(string nombreReal)
        {
            ConstruirIndice();
            Type t;
            if (tiposPorNombreReal.TryGetValue(nombreReal, out t))
            {
                return t;
            }
            return null;
        }

        public static int TiposIndexados
        {
            get
            {
                ConstruirIndice();
                return tiposPorNombreReal.Count;
            }
        }

        // Busca un miembro estatico por su nombre real dentro de un tipo ya
        // resuelto. Sirve igual para campos y para propiedades, porque
        // Il2CppInterop expone los campos del juego como propiedades.
        public static MemberInfo Miembro(Type tipo, string nombreReal)
        {
            if (tipo == null)
            {
                return null;
            }
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic
                           | BindingFlags.Static | BindingFlags.Instance;

            MemberInfo[] miembros = tipo.GetMembers(f);
            for (int i = 0; i < miembros.Length; i++)
            {
                if (miembros[i].Name == nombreReal || NombreReal(miembros[i]) == nombreReal)
                {
                    return miembros[i];
                }
            }
            return null;
        }

        // Lee un miembro estatico (campo o propiedad) por nombre real.
        public static object LeerEstatico(string tipoReal, string miembroReal)
        {
            Type t = Tipo(tipoReal);
            MemberInfo m = Miembro(t, miembroReal);
            PropertyInfo p = m as PropertyInfo;
            if (p != null)
            {
                return p.GetValue(null);
            }
            FieldInfo c = m as FieldInfo;
            if (c != null)
            {
                return c.GetValue(null);
            }
            return null;
        }
    }
}
