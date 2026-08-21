using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using MelonLoader;
using MelonLoader.Utils;

namespace CloneHeroMod
{
    // Descarga la ultima version del mod desde GitHub y la instala.
    //
    // El problema de fondo es que el .dll esta cargado por MelonLoader y no se
    // puede sobrescribir. Windows si permite RENOMBRAR un archivo abierto (el
    // runtime lo mapea con FILE_SHARE_DELETE), asi que el intercambio es:
    // el actual pasa a .old y el nuevo ocupa su sitio. El .old se borra en el
    // siguiente arranque, cuando ya no lo usa nadie.
    //
    // La descarga va en un hilo aparte: bloquear el hilo principal congelaria
    // el juego. Ese hilo NO toca nada de Unity, solo red y archivos; el menu
    // consulta el resultado desde OnUpdate.
    public static class Actualizador
    {
        public const string Etiqueta = "Update Cool Mod";

        public const string Url = "https://raw.githubusercontent.com/sergioadair/"
            + "Cool-Mod-For-CloneHero/main/v1.1.0.6142-final/CloneHeroMod.dll";

        private enum Estado
        {
            Reposo,
            Descargando,
            AlDia,
            Instalado,
            Fallo
        }

        // volatile: los escribe el hilo de descarga y los lee el principal.
        private static volatile int estado = (int)Estado.Reposo;
        private static volatile bool hayNoticia;

        public static bool Ocupado
        {
            get { return estado == (int)Estado.Descargando; }
        }

        // True una sola vez por cambio de estado: le dice al menu que hay que
        // reescribir el texto de la fila.
        public static bool Consumir()
        {
            if (!hayNoticia)
            {
                return false;
            }
            hayNoticia = false;
            return true;
        }

        // Texto de la fila del menu. Corto a proposito: el hueco de la etiqueta
        // son 547 px y TextMeshPro encoge la letra si no cabe.
        public static string Texto()
        {
            switch ((Estado)estado)
            {
                case Estado.Descargando: return Etiqueta + ": checking...";
                case Estado.AlDia:       return Etiqueta + ": up to date";
                case Estado.Instalado:   return Etiqueta + ": done, restart";
                case Estado.Fallo:       return Etiqueta + ": failed";
                default:                 return Etiqueta;
            }
        }

        // Al reabrir el menu se limpia el resultado anterior, pero no el aviso
        // de reiniciar: ese sigue haciendo falta hasta que se reinicie.
        public static void OlvidarResultado()
        {
            if (estado == (int)Estado.AlDia || estado == (int)Estado.Fallo)
            {
                estado = (int)Estado.Reposo;
                hayNoticia = true;
            }
        }

        public static void Lanzar()
        {
            if (Ocupado || estado == (int)Estado.Instalado)
            {
                return;      // ya se esta bajando, o ya esta y falta reiniciar
            }
            Fijar(Estado.Descargando);
            MelonLogger.Msg("[Update] descargando " + Url);
            Thread hilo = new Thread(Trabajo);
            hilo.IsBackground = true;
            hilo.Name = "CoolModUpdate";
            hilo.Start();
        }

        private static void Fijar(Estado nuevo)
        {
            estado = (int)nuevo;
            hayNoticia = true;
        }

        // ------------------------------------------------------- hilo aparte -
        private static void Trabajo()
        {
            try
            {
                byte[] datos = Descargar();
                if (datos == null)
                {
                    return;      // Descargar ya dejo el motivo
                }
                // Comprobacion minima de que es un ejecutable de Windows y no
                // una pagina de error servida con codigo 200.
                if (datos.Length < 4096 || datos[0] != (byte)'M' || datos[1] != (byte)'Z')
                {
                    Fallar("lo descargado no es un .dll (" + datos.Length + " bytes)");
                    return;
                }

                string destino = RutaDll();
                if (destino == null)
                {
                    Fallar("no se localizo el .dll instalado");
                    return;
                }
                if (File.Exists(destino) && Iguales(File.ReadAllBytes(destino), datos))
                {
                    Fijar(Estado.AlDia);
                    MelonLogger.Msg("[Update] ya esta en la ultima version");
                    return;
                }

                Intercambiar(destino, datos);
                Fijar(Estado.Instalado);
                MelonLogger.Msg("[Update] instalado en " + destino
                                + "; hay que reiniciar el juego");
            }
            catch (Exception ex)
            {
                Fallar(ex.Message);
            }
        }

        private static byte[] Descargar()
        {
            try
            {
                using (HttpClient cliente = new HttpClient())
                {
                    cliente.Timeout = TimeSpan.FromSeconds(30);
                    cliente.DefaultRequestHeaders.Add("User-Agent", "CoolModForCloneHero");
                    HttpResponseMessage r = cliente.GetAsync(Url).GetAwaiter().GetResult();
                    if (!r.IsSuccessStatusCode)
                    {
                        Fallar("HTTP " + ((int)r.StatusCode).ToString());
                        return null;
                    }
                    return r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                Fallar(ex.Message);
                return null;
            }
        }

        // El actual se aparta a .old y el nuevo ocupa su sitio. Si algo falla a
        // medias se deshace el renombrado: mejor quedarse en la version vieja
        // que sin mod.
        private static void Intercambiar(string destino, byte[] datos)
        {
            string apartado = destino + ".old";
            try
            {
                if (File.Exists(apartado))
                {
                    File.Delete(apartado);
                }
            }
            catch (Exception)
            {
                // Suele ser el .old del arranque anterior, todavia en uso.
                apartado = destino + "." + DateTime.Now.Ticks.ToString() + ".old";
            }

            bool movido = false;
            try
            {
                if (File.Exists(destino))
                {
                    File.Move(destino, apartado);
                    movido = true;
                }
                File.WriteAllBytes(destino, datos);
            }
            catch (Exception)
            {
                if (movido && !File.Exists(destino))
                {
                    try { File.Move(apartado, destino); } catch (Exception) { }
                }
                throw;
            }
        }

        private static void Fallar(string porque)
        {
            Fijar(Estado.Fallo);
            MelonLogger.Warning("[Update] fallo: " + porque);
        }

        // ----------------------------------------------------------- rutas --
        // La ruta real del ensamblado que se esta ejecutando. Si el cargador lo
        // monto desde memoria, Location viene vacio y se recurre a la carpeta
        // Mods.
        private static string RutaDll()
        {
            try
            {
                string ruta = typeof(Actualizador).Assembly.Location;
                if (!string.IsNullOrEmpty(ruta) && File.Exists(ruta))
                {
                    return ruta;
                }
            }
            catch (Exception)
            {
            }
            try
            {
                string ruta = Path.Combine(MelonEnvironment.ModsDirectory, "CloneHeroMod.dll");
                return File.Exists(ruta) ? ruta : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Los .old de actualizaciones anteriores ya no los usa nadie.
        public static void LimpiarRestos()
        {
            try
            {
                string[] restos = Directory.GetFiles(MelonEnvironment.ModsDirectory, "*.old");
                for (int i = 0; i < restos.Length; i++)
                {
                    try { File.Delete(restos[i]); } catch (Exception) { }
                }
            }
            catch (Exception)
            {
            }
        }

        private static bool Iguales(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
