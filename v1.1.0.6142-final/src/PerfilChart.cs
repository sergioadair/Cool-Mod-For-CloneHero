using System;
using System.Collections.Generic;
using System.Threading;
using MelonLoader;

namespace CloneHeroMod
{
    // Perfil del chart que el jugador va a tocar, calculado cuando hace falta.
    //
    // POR QUE NO SE GUARDA EN song.ini: harian falta hasta 32 perfiles por
    // cancion (ocho instrumentos por cuatro dificultades), cada uno con siete
    // cifras. Inflar el archivo del usuario con doscientos numeros para que
    // consulte uno es mal negocio.
    //
    // POR QUE SE PUEDE CALCULAR AL VUELO: el parser no toca nada del juego, solo
    // archivos y cadenas, asi que corre en un hilo aparte sin rozar la API de
    // Il2Cpp. Parsear un chart cuesta decenas de milisegundos; hacerlo en el
    // hilo del juego se veria como un tiron al abrir el panel.
    //
    // Se pide al resaltar la cancion, no al abrir el panel, para que ya este
    // listo cuando se mire. Y solo manda la ultima peticion: al recorrer la
    // lista deprisa no tiene sentido terminar las de las canciones por las que
    // ya se paso.
    public static class PerfilChart
    {
        private class Peticion
        {
            public string chart;
            public bool esMidi;
            public string pista;
            public int dificultad;
            public string clave;
        }

        private static readonly Dictionary<string, Dificultad.Perfil> cache =
            new Dictionary<string, Dificultad.Perfil>(StringComparer.OrdinalIgnoreCase);
        private static readonly object candado = new object();

        private static Peticion pendiente;
        private static bool trabajando;

        // Devuelve el perfil si ya esta calculado. Si no, lo encarga y devuelve
        // null.
        //
        // "listo" separa dos casos que se ven igual desde fuera y no lo son:
        // todavia no esta calculado, o ya se calculo y la cancion NO TRAE ese
        // chart. El panel lo dice de forma distinta, porque a un jugador con
        // Keys elegido le importa saber que esta cancion no tiene Keys.
        public static Dificultad.Perfil Pedir(string chart, bool esMidi, string pista,
                                              int dificultad, out bool listo)
        {
            listo = false;
            if (string.IsNullOrEmpty(chart) || string.IsNullOrEmpty(pista) || dificultad < 0)
            {
                return null;
            }
            string clave = chart + "|" + pista + "|" + dificultad.ToString();
            lock (candado)
            {
                if (cache.TryGetValue(clave, out Dificultad.Perfil p))
                {
                    listo = true;
                    return p;      // null aqui significa: no hay ese chart
                }
                pendiente = new Peticion
                {
                    chart = chart,
                    esMidi = esMidi,
                    pista = pista,
                    dificultad = dificultad,
                    clave = clave
                };
                if (!trabajando)
                {
                    trabajando = true;
                    Thread hilo = new Thread(Trabajar);
                    hilo.IsBackground = true;
                    hilo.Name = "PerfilChart";
                    hilo.Start();
                }
            }
            return null;
        }

        public static void Olvidar()
        {
            lock (candado)
            {
                cache.Clear();
            }
        }

        private static void Trabajar()
        {
            while (true)
            {
                Peticion p;
                lock (candado)
                {
                    p = pendiente;
                    pendiente = null;
                    if (p == null)
                    {
                        trabajando = false;
                        return;
                    }
                }
                Dificultad.Perfil perfil = null;
                try
                {
                    Dificultad.CalcularChart(p.chart, p.esMidi, p.pista, p.dificultad,
                                             out perfil);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning("[PerfilChart] " + ex.Message);
                }
                lock (candado)
                {
                    // Se guarda incluso si salio null: asi no se reintenta en
                    // bucle una cancion que no trae ese chart.
                    cache[p.clave] = perfil;
                }
            }
        }
    }
}
