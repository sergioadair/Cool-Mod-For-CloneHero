using System;
using System.IO;
using System.Reflection;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;

namespace CloneHeroMod
{
    // Sonda para localizar el instrumento y la dificultad que el jugador tiene
    // elegidos.
    //
    // El juego los recuerda entre canciones, asi que estan guardados en alguna
    // parte del objeto del jugador. GlobalVariables esta sin ofuscar y expone
    // playerList, pero de ahi para dentro todo son nombres generados: no se
    // puede saber a ojo cual es cual.
    //
    // Se vuelca dos veces con selecciones distintas y se compara: lo que cambie
    // es lo que buscamos. Es la misma tactica que localizo el contador de racha
    // y la propiedad de "opcion abierta" de los menus.
    public static class DiagnosticoJugador
    {
        private static int numero;

        public static void Volcar()
        {
            if (!Diagnostico.Detallado)
            {
                return;
            }
            try
            {
                numero++;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== volcado #" + numero.ToString() + " ===");

                Il2Cpp.GlobalVariables g = Il2Cpp.GlobalVariables.instance;
                if (g == null)
                {
                    sb.AppendLine("GlobalVariables.instance = null");
                }
                else
                {
                    sb.AppendLine("playerCount = " + g.playerCount.ToString());
                    var lista = g.playerList;
                    if (lista == null)
                    {
                        sb.AppendLine("playerList = null");
                    }
                    else
                    {
                        sb.AppendLine("playerList: " + lista.Length.ToString());
                        for (int i = 0; i < lista.Length && i < 2; i++)
                        {
                            if (lista[i] == null)
                            {
                                continue;
                            }
                            sb.AppendLine();
                            sb.AppendLine("--- jugador " + i.ToString() + " ---");
                            Volcar(sb, lista[i], 0);
                        }
                    }
                }

                string ruta = Path.Combine(MelonEnvironment.MelonLoaderDirectory,
                                           "jugador-" + numero.ToString() + ".txt");
                File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false));
                MelonLogger.Msg("[DiagJugador] volcado en " + ruta);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[DiagJugador] " + ex);
            }
        }

        // Numeros, booleanos y enums de un objeto, bajando un nivel por los
        // miembros que sean a su vez objetos del juego. Mas profundo no: la
        // jerarquia se dispara y lo que buscamos deberia estar cerca.
        private static void Volcar(StringBuilder sb, object obj, int nivel)
        {
            if (obj == null || nivel > 1)
            {
                return;
            }
            string sangria = new string(' ', nivel * 2);
            PropertyInfo[] props = obj.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < props.Length; i++)
            {
                PropertyInfo p = props[i];
                if (p.GetIndexParameters().Length != 0 || p.Name == "Pointer"
                    || p.Name == "ObjectClass" || p.Name == "WasCollected")
                {
                    continue;
                }
                object v;
                try { v = p.GetValue(obj); }
                catch (Exception) { continue; }
                if (v == null)
                {
                    continue;
                }
                Type t = p.PropertyType;
                if (t == typeof(int) || t == typeof(bool) || t == typeof(float)
                    || t == typeof(string) || t.IsEnum)
                {
                    sb.AppendLine(sangria + p.Name + " (" + t.Name + ") = " + v.ToString());
                    continue;
                }
                // Enums de Il2Cpp: llegan como estructura con un value interno.
                if (t.Namespace != null && t.Namespace.StartsWith("Il2Cpp", StringComparison.Ordinal))
                {
                    sb.AppendLine(sangria + p.Name + " (" + t.Name + ") = " + v.ToString());
                    if (nivel == 0)
                    {
                        Volcar(sb, v, nivel + 1);
                    }
                }
            }
        }
    }
}
