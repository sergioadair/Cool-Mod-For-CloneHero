using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using MelonLoader;
using UnityEngine;

namespace CloneHeroMod
{
    // "Restore a Backup", dentro del panel de importar: deja esta copia como
    // estaba antes de una importacion.
    //
    // Cada importacion deja en Import Backups\<fecha>:
    //   - PlayerData\, GameData\, Game Folder\: lo que reemplazo o retiro;
    //   - import.txt: lo que CREO (no habia nada que respaldar) y lo que
    //     anadio a Custom y Setlists.
    // Restaurar devuelve lo primero y borra lo segundo. Los respaldos de antes
    // de que existiera import.txt solo pueden devolver lo reemplazado.
    //
    // Lo que esta puesto ahora NO se respalda antes de restaurar: es lo que
    // se importo, y se puede volver a importar. El aviso de la pantalla dice
    // que se pierde lo que haya cambiado desde entonces. El respaldo usado se
    // borra al terminar bien: ya esta aplicado.
    //
    // Mismo cuidado que al importar: primero se copia todo a temporales; si
    // eso falla no se ha tocado nada. Y se reinicia matando el proceso.
    //
    // TODO EL TEXTO EN INGLES: se ve en pantalla.
    public static partial class ImportarCopia
    {
        private sealed class Respaldo
        {
            public string Carpeta;
            public string Que;
            public string Desde;
            public bool ConManifiesto;
            public int Devolver;      // archivos guardados
            public int Quitar;        // archivos que la importacion creo o anadio
        }

        private static List<Respaldo> respaldos = new List<Respaldo>();
        private static int cursorR;
        private static int inicioR;
        private static bool confirmandoR;
        private const int VisiblesR = 5;

        private static readonly Base[] Bases = { Base.Datos, Base.DatosJuego, Base.Raiz };

        private static string RaizDe(CopiaJuego c, Base b)
        {
            return b == Base.Datos ? c.Datos : b == Base.DatosJuego ? c.DatosJuego : c.Raiz;
        }

        private static bool BaseDeNombre(string n, out Base b)
        {
            for (int i = 0; i < Bases.Length; i++)
            {
                if (NombreBase(Bases[i]) == n)
                {
                    b = Bases[i];
                    return true;
                }
            }
            b = Base.Datos;
            return false;
        }

        private static void AbrirRespaldos()
        {
            respaldos = ListarRespaldos();
            cursorR = 0;
            inicioR = 0;
            confirmandoR = false;
            mensaje = null;
            estado = Estado.Respaldos;
            frameAbierto = Time.frameCount;
        }

        private static void Volver()
        {
            confirmandoR = false;
            mensaje = null;
            estado = Estado.Eligiendo;
            frameAbierto = Time.frameCount;
        }

        // El mas nuevo primero: el nombre de la carpeta es la fecha.
        private static List<Respaldo> ListarRespaldos()
        {
            List<Respaldo> l = new List<Respaldo>();
            try
            {
                string dir = CarpetaRespaldos(actual);
                if (!Directory.Exists(dir))
                {
                    return l;
                }
                string[] ds = Directory.GetDirectories(dir);
                Array.Sort(ds, StringComparer.OrdinalIgnoreCase);
                Array.Reverse(ds);
                for (int i = 0; i < ds.Length; i++)
                {
                    Respaldo r = Leer(ds[i]);
                    if (r.Devolver > 0 || r.Quitar > 0)
                    {
                        l.Add(r);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Importar] listar respaldos: " + ex.Message);
            }
            return l;
        }

        private static Respaldo Leer(string carpeta)
        {
            Respaldo r = new Respaldo();
            r.Carpeta = carpeta;
            for (int i = 0; i < Bases.Length; i++)
            {
                string d = Path.Combine(carpeta, NombreBase(Bases[i]));
                if (!Directory.Exists(d))
                {
                    continue;
                }
                foreach (string f in Directory.EnumerateFiles(d, "*", Opciones))
                {
                    r.Devolver++;
                }
            }
            string m = Path.Combine(carpeta, Manifiesto);
            if (!File.Exists(m))
            {
                return r;
            }
            r.ConManifiesto = true;
            foreach (string linea in File.ReadLines(m))
            {
                string[] p = linea.Split('\t');
                if (p.Length < 2)
                {
                    continue;
                }
                if (p[0] == "from") r.Desde = p[1];
                else if (p[0] == "what") r.Que = p[1];
                else if ((p[0] == "created" || p[0] == "added") && p.Length >= 3) r.Quitar++;
            }
            return r;
        }

        public static string FechaDe(string carpeta)
        {
            string n = Path.GetFileName(carpeta ?? "");
            if (DateTime.TryParseExact(n, "yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out DateTime f))
            {
                return f.ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture);
            }
            return n;
        }

        // ----------------------------------------------------------- entrada
        private static void EntradaRespaldos()
        {
            if (Raton(true))
            {
                return;
            }
            if (!LeerBotones(out bool arriba, out bool abajo, out bool verde, out bool rojo))
            {
                return;
            }
            if (arriba) MoverR(-1);
            else if (abajo) MoverR(1);
            else if (rojo) AtrasR();
            else if (verde) ActivarR();
        }

        private static void MoverR(int d)
        {
            int filas = respaldos.Count + 1;      // + "Back"
            cursorR = (cursorR + d + filas) % filas;
            confirmandoR = false;
            mensaje = null;
            if (cursorR < respaldos.Count)
            {
                if (cursorR < inicioR) inicioR = cursorR;
                if (cursorR >= inicioR + VisiblesR) inicioR = cursorR - VisiblesR + 1;
            }
        }

        private static void AtrasR()
        {
            if (confirmandoR)
            {
                confirmandoR = false;
            }
            else
            {
                Volver();
            }
        }

        private static void ActivarR()
        {
            if (cursorR >= respaldos.Count)
            {
                Volver();
                return;
            }
            if (!confirmandoR)
            {
                confirmandoR = true;
                mensaje = null;
                return;
            }
            confirmandoR = false;
            Restaurar(respaldos[cursorR]);
        }

        // ------------------------------------------------------------ trabajo
        private static void Restaurar(Respaldo r)
        {
            MelonLogger.Msg("[Importar] restaurando " + r.Carpeta);
            restaurando = true;
            carpetaRespaldo = r.Carpeta;
            fase = "Checking the backup...";
            archivoActual = null;
            Interlocked.Exchange(ref bytesHechos, 0);
            Interlocked.Exchange(ref bytesTotal, 0);
            terminado = false;
            errorHilo = null;
            sinCambios = false;
            resumen = null;
            estado = Estado.Importando;

            CopiaJuego a = actual;
            string carpeta = r.Carpeta;
            Thread t = new Thread(() => TrabajoRestaurar(carpeta, a));
            t.IsBackground = true;
            t.Start();
        }

        private struct Quitar
        {
            public string Archivo;
            public string Tope;      // carpetas vacias se borran hasta aqui, sin incluirla
        }

        private static void TrabajoRestaurar(string carpeta, CopiaJuego a)
        {
            List<Reemplazo> devolver = new List<Reemplazo>();
            bool tocado = false;
            try
            {
                // 1. Plan: lo guardado vuelve a su sitio...
                HashSet<string> destinos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                for (int i = 0; i < Bases.Length; i++)
                {
                    string dirB = Path.Combine(carpeta, NombreBase(Bases[i]));
                    string baseDst = RaizDe(a, Bases[i]);
                    if (!Directory.Exists(dirB) || string.IsNullOrEmpty(baseDst))
                    {
                        continue;
                    }
                    foreach (string f in Directory.EnumerateFiles(dirB, "*", Opciones))
                    {
                        Reemplazo r = new Reemplazo();
                        r.Origen = f;
                        r.Destino = Path.Combine(baseDst, Path.GetRelativePath(dirB, f));
                        devolver.Add(r);
                        destinos.Add(r.Destino);
                        total += new FileInfo(f).Length;
                    }
                }

                // ...y lo que trajo la importacion se va.
                List<Quitar> quitar = new List<Quitar>();
                string m = Path.Combine(carpeta, Manifiesto);
                if (File.Exists(m))
                {
                    foreach (string linea in File.ReadLines(m))
                    {
                        string[] p = linea.Split('\t');
                        if (p.Length < 3 || (p[0] != "created" && p[0] != "added")
                            || !BaseDeNombre(p[1], out Base b))
                        {
                            continue;
                        }
                        string baseDst = RaizDe(a, b);
                        if (string.IsNullOrEmpty(baseDst))
                        {
                            continue;
                        }
                        string dst = Path.Combine(baseDst, p[2]);
                        if (destinos.Contains(dst) || !File.Exists(dst))
                        {
                            continue;
                        }
                        int corte = p[2].IndexOfAny(new[] { '\\', '/' });
                        Quitar q;
                        q.Archivo = dst;
                        q.Tope = corte > 0 ? Path.Combine(baseDst, p[2].Substring(0, corte)) : null;
                        quitar.Add(q);
                    }
                }
                Interlocked.Exchange(ref bytesTotal, total);
                MelonLogger.Msg("[Importar] restaurar: " + devolver.Count.ToString() + " a devolver, "
                    + quitar.Count.ToString() + " a quitar");

                if (devolver.Count == 0 && quitar.Count == 0)
                {
                    sinCambios = true;
                    BorrarCarpeta(carpeta);
                    return;
                }

                // 2. Copias temporales junto a su destino.
                fase = "Copying the saved files...";
                for (int i = 0; i < devolver.Count; i++)
                {
                    Reemplazo r = devolver[i];
                    archivoActual = Path.GetFileName(r.Destino);
                    Directory.CreateDirectory(Path.GetDirectoryName(r.Destino));
                    r.Temp = r.Destino + Temporal;
                    Copiar(r.Origen, r.Temp);
                }

                // 3. Fuera lo importado.
                tocado = true;
                for (int i = 0; i < quitar.Count; i++)
                {
                    fase = "Removing what the import added...  " + (i + 1).ToString("N0")
                         + " of " + quitar.Count.ToString("N0") + " files";
                    archivoActual = Path.GetFileName(quitar[i].Archivo);
                    File.Delete(quitar[i].Archivo);
                    LimpiarVacias(Path.GetDirectoryName(quitar[i].Archivo), quitar[i].Tope);
                }

                // 4. Lo guardado, a su sitio.
                fase = "Saving...";
                archivoActual = null;
                for (int i = 0; i < devolver.Count; i++)
                {
                    File.Move(devolver[i].Temp, devolver[i].Destino, true);
                    devolver[i].Puesto = true;
                }

                // Ya esta aplicado: el respaldo sobra.
                BorrarCarpeta(carpeta);

                string t = devolver.Count > 0
                    ? "Put back " + devolver.Count.ToString("N0") + (devolver.Count == 1 ? " file" : " files")
                    : "";
                if (quitar.Count > 0)
                {
                    t += (t.Length > 0 ? " and removed " : "Removed ") + quitar.Count.ToString("N0")
                       + (quitar.Count == 1 ? " file" : " files") + " the import had added";
                }
                resumen = t + ". This copy is back the way it was before that import.";
                MelonLogger.Msg("[Importar] restaurado: " + devolver.Count.ToString() + " devueltos, "
                    + quitar.Count.ToString() + " quitados");
            }
            catch (Exception ex)
            {
                Limpiar(devolver);
                MelonLogger.Error("[Importar] restaurar: " + ex);
                errorHilo = tocado
                    ? "The restore stopped halfway (" + ex.Message + "). The backup is still there:"
                      + " try again."
                    : "Couldn't restore that backup (" + ex.Message + "). Nothing was changed.";
            }
            finally
            {
                terminado = true;
            }
        }

        private static void LimpiarVacias(string dir, string tope)
        {
            if (tope == null)
            {
                return;
            }
            try
            {
                while (!string.IsNullOrEmpty(dir)
                       && dir.Length > tope.Length
                       && dir.StartsWith(tope, StringComparison.OrdinalIgnoreCase)
                       && Directory.Exists(dir)
                       && !Directory.EnumerateFileSystemEntries(dir).GetEnumerator().MoveNext())
                {
                    Directory.Delete(dir);
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void BorrarCarpeta(string carpeta)
        {
            try
            {
                Directory.Delete(carpeta, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Importar] no se pudo borrar el respaldo " + carpeta + ": " + ex.Message);
            }
        }

        private static void TerminarRestauracion()
        {
            restaurando = errorHilo == null && !sinCambios;
            if (errorHilo != null || sinCambios)
            {
                string e = errorHilo;
                AbrirRespaldos();
                if (e != null)
                {
                    Decir(e, true);
                }
                else
                {
                    Decir("That backup had nothing left to put back.", false);
                }
                return;
            }
            estado = Estado.Listo;
            reiniciarEn = Time.unscaledTime + 3f;
        }

        // -------------------------------------------------------------- panel
        private static string FilaR(int i, string t)
        {
            return Enlace(i, i == cursorR ? Tinta(Oro, "> " + t) : "   " + t);
        }

        private static string TextoRespaldos()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("<size=40><b>Restore a Backup</b></size>\n\n");
            sb.Append(Tinta(Gris, "Every import saved what it replaced. Restoring one puts this copy"
                + " back the way it was before that import.")).Append("\n\n");

            if (respaldos.Count == 0)
            {
                sb.Append("   ").Append(Tinta(Gris, "No backups yet.")).Append("\n\n");
            }
            else
            {
                if (inicioR > 0)
                {
                    sb.Append("   ").Append(Tinta(Gris, "...")).Append('\n');
                }
                int fin = Math.Min(respaldos.Count, inicioR + VisiblesR);
                for (int i = inicioR; i < fin; i++)
                {
                    Respaldo r = respaldos[i];
                    string titulo = FechaDe(r.Carpeta) + "   " + Literal(r.Que ?? "Import");
                    if (i == cursorR && confirmandoR)
                    {
                        titulo = "Restore " + FechaDe(r.Carpeta) + "? Press again to confirm";
                    }
                    sb.Append(FilaR(i, titulo)).Append('\n');

                    string detalle = r.ConManifiesto
                        ? "from " + Literal(r.Desde ?? "?")
                        : "older backup: only puts back the replaced files";
                    if (r.Quitar > 0)
                    {
                        detalle += "  -  also removes " + r.Quitar.ToString("N0")
                                 + (r.Quitar == 1 ? " file" : " files") + " the import added";
                    }
                    sb.Append(Enlace(i, "      " + Tinta(Gris, "<size=80%>" + detalle + "</size>"))).Append('\n');
                }
                if (fin < respaldos.Count)
                {
                    sb.Append("   ").Append(Tinta(Gris, "...")).Append('\n');
                }
                sb.Append('\n');
            }
            sb.Append(FilaR(respaldos.Count, "Back")).Append('\n');
            if (!string.IsNullOrEmpty(mensaje))
            {
                sb.Append("\n   ").Append(Tinta(mensajeError ? Rojo : Oro, Literal(mensaje))).Append('\n');
            }

            sb.Append("\n<size=80%>").Append(Tinta(Gris,
                "Anything that changed after that import (new scores, settings...) will be lost.\n"
                + "If you imported more than once, restore the newest one first.\n"
                + "The game restarts by itself when it's done."))
              .Append("</size>");
            return sb.ToString();
        }
    }
}
