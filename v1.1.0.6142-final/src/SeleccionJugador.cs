using System;
using System.Reflection;
using MelonLoader;

namespace CloneHeroMod
{
    // Que instrumento y que dificultad tiene elegidos el jugador.
    //
    // El juego los recuerda entre canciones, asi que no hay que esperar a que
    // entre en una: en cuanto esta en la lista ya se sabe con que va a tocar.
    //
    // COMO SE LOCALIZAN: GlobalVariables esta sin ofuscar y expone playerList,
    // pero de ahi para dentro los nombres los genera Il2CppInterop y no
    // significan nada. Asi que no se piden por nombre — se reconocen POR SU
    // VALOR: la propiedad de dificultad es la que vale "Easy", "Medium",
    // "Hard" o "Expert", y la de instrumento la que vale "Guitar", "Bass"...
    // Los enums del juego imprimen su nombre, asi que basta comparar cadenas y
    // no hace falta conocer los tipos en compilacion.
    //
    // Se comprobo con dos volcados seguidos cambiando la seleccion: de todo el
    // objeto del jugador, solo esos dos valores cambiaban.
    public static class SeleccionJugador
    {
        // Nombre del enum del juego -> nombre de la pista en el chart. El enum
        // trae mas valores (Band, SixFretRhythm) que no tienen pista propia.
        private static readonly string[,] Equivalencias =
        {
            { "Guitar", "Single" },
            { "Bass", "DoubleBass" },
            { "Rhythm", "DoubleRhythm" },
            { "GuitarCoop", "DoubleGuitar" },
            { "SixFretGuitar", "GHLGuitar" },
            { "SixFretBass", "GHLBass" },
            { "Drums", "Drums" },
            { "ProDrums", "Drums" },
            { "Keys", "Keyboard" }
        };

        private static readonly string[] Dificultades = { "Easy", "Medium", "Hard", "Expert" };

        private static object contenedor;
        private static PropertyInfo propInstrumento;
        private static PropertyInfo propDificultad;
        private static bool avisado;

        // Devuelve false si no se ha podido averiguar.
        public static bool Leer(out string pista, out int dificultad)
        {
            pista = null;
            dificultad = -1;
            try
            {
                if (contenedor == null && !Resolver())
                {
                    return false;
                }
                string inst = Texto(propInstrumento);
                string dif = Texto(propDificultad);
                if (inst == null || dif == null)
                {
                    contenedor = null;      // el jugador ya no vale; se rehace
                    return false;
                }
                dificultad = IndiceDificultad(dif);
                pista = Pista(inst);
                return pista != null && dificultad >= 0;
            }
            catch (Exception)
            {
                contenedor = null;
                return false;
            }
        }

        private static string Texto(PropertyInfo p)
        {
            object v = p.GetValue(contenedor);
            return v == null ? null : v.ToString();
        }

        public static int IndiceDificultad(string nombre)
        {
            for (int i = 0; i < Dificultades.Length; i++)
            {
                if (Dificultades[i] == nombre)
                {
                    return i;
                }
            }
            return -1;
        }

        public static string Pista(string instrumento)
        {
            for (int i = 0; i < Equivalencias.GetLength(0); i++)
            {
                if (Equivalencias[i, 0] == instrumento)
                {
                    return Equivalencias[i, 1];
                }
            }
            return null;
        }

        // Busca el objeto que tenga las dos cosas. Se mira el jugador y un
        // nivel por dentro: en la version 1.1.0.6142 estan en el primer objeto
        // que cuelga de el, pero eso no tiene por que aguantar.
        private static bool Resolver()
        {
            try
            {
                Il2Cpp.GlobalVariables g = Il2Cpp.GlobalVariables.instance;
                if (g == null || g.playerList == null)
                {
                    return false;
                }
                for (int i = 0; i < g.playerList.Length; i++)
                {
                    object jugador = g.playerList[i];
                    if (jugador == null)
                    {
                        continue;
                    }
                    if (Reconocer(jugador))
                    {
                        return true;
                    }
                    PropertyInfo[] ps = jugador.GetType().GetProperties(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    for (int k = 0; k < ps.Length; k++)
                    {
                        if (ps[k].GetIndexParameters().Length != 0 || ps[k].Name == "Pointer")
                        {
                            continue;
                        }
                        object hijo;
                        try { hijo = ps[k].GetValue(jugador); }
                        catch (Exception) { continue; }
                        if (hijo != null && Reconocer(hijo))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        private static bool Reconocer(object obj)
        {
            PropertyInfo inst = null;
            PropertyInfo dif = null;
            PropertyInfo[] ps = obj.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].GetIndexParameters().Length != 0 || ps[i].Name == "Pointer")
                {
                    continue;
                }
                // Las cadenas NO valen, aunque digan "Guitar". La seleccion se
                // guarda en enums; hay ademas una string que vale "Guitar" y
                // no cambia nunca — se comprobo con dos volcados cambiando de
                // instrumento — y engancharse a ella dejaba el panel siempre en
                // guitarra pasara lo que pasara.
                if (ps[i].PropertyType == typeof(string))
                {
                    continue;
                }
                object v;
                try { v = ps[i].GetValue(obj); }
                catch (Exception) { continue; }
                if (v == null)
                {
                    continue;
                }
                string s = v.ToString();
                if (dif == null && IndiceDificultad(s) >= 0)
                {
                    dif = ps[i];
                }
                else if (inst == null && Pista(s) != null)
                {
                    inst = ps[i];
                }
            }
            if (inst == null || dif == null)
            {
                return false;
            }
            contenedor = obj;
            propInstrumento = inst;
            propDificultad = dif;
            if (!avisado)
            {
                avisado = true;
                MelonLogger.Msg("[Seleccion] instrumento en " + inst.Name
                                + ", dificultad en " + dif.Name);
            }
            return true;
        }
    }
}
