using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CloneHeroMod
{
    // Lectura y escritura de .sng, el formato de cancion en un solo archivo que
    // Clone Hero tambien admite: dentro van el chart, los audios, la caratula y
    // el fondo, cifrados con un XOR.
    //
    // El formato se saco mirando el binario, no de ninguna documentacion:
    //
    //     "SNGPKG"                6 bytes
    //     uint32                  version (1)
    //     byte[16]                mascara XOR
    //     uint64                  largo de los metadatos (SE INCLUYE el uint64
    //                             del recuento que viene detras)
    //     uint64                  cuantos metadatos
    //       uint32 + clave, uint32 + valor      por cada uno
    //     uint64                  largo del indice (idem, se incluye a si mismo)
    //     uint64                  cuantos archivos
    //       uint8 + nombre, uint64 largo, uint64 posicion ABSOLUTA
    //     uint64                  largo del bloque de datos
    //     ...                     los archivos, uno detras de otro
    //
    // EL CIFRADO. Cada byte va con:
    //
    //     claro[i] = crudo[i] ^ mascara[i % 16] ^ (i & 0xFF)
    //
    // donde i cuenta desde el principio DE CADA ARCHIVO, no del contenedor. Al
    // principio se probo con (i % 16) en vez de (i & 0xFF) y descifraba bien los
    // primeros 16 bytes y basura a partir de ahi — el desfase era justo 0x10.
    //
    // Que el indice sea relativo a cada archivo tiene una consecuencia muy
    // practica: mover un archivo dentro del contenedor NO obliga a recifrarlo.
    // Por eso al reescribir se copian tal cual los que no se tocan, y solo se
    // cifra el que cambia. Asi un .sng de 200 MB no hay que pasarlo por memoria.
    public class ArchivoSng
    {
        public class Entrada
        {
            public string nombre;
            public long largo;
            public long inicio;      // absoluta dentro del contenedor
        }

        private const int Cabecera = 6;
        private static readonly byte[] Magia =
        { (byte)'S', (byte)'N', (byte)'G', (byte)'P', (byte)'K', (byte)'G' };

        public string ruta;
        public int version = 1;
        public byte[] mascara = new byte[16];
        public List<string[]> metadatos = new List<string[]>();
        public List<Entrada> archivos = new List<Entrada>();

        public static bool EsSng(string ruta)
        {
            return !string.IsNullOrEmpty(ruta)
                && ruta.EndsWith(".sng", StringComparison.OrdinalIgnoreCase);
        }

        // Solo la cabecera: los datos se quedan en disco.
        public static ArchivoSng Leer(string ruta)
        {
            ArchivoSng s = new ArchivoSng();
            s.ruta = ruta;
            using (FileStream f = File.OpenRead(ruta))
            {
                byte[] magia = Bytes(f, Cabecera);
                for (int i = 0; i < Cabecera; i++)
                {
                    if (magia[i] != Magia[i])
                    {
                        throw new InvalidDataException("no es un .sng");
                    }
                }
                s.version = (int)U32(f);
                s.mascara = Bytes(f, 16);

                long largoMeta = (long)U64(f);
                long cuantos = (long)U64(f);
                for (long i = 0; i < cuantos; i++)
                {
                    string clave = Texto(f, (int)U32(f));
                    string valor = Texto(f, (int)U32(f));
                    s.metadatos.Add(new[] { clave, valor });
                }

                long largoIndice = (long)U64(f);
                long nArchivos = (long)U64(f);
                for (long i = 0; i < nArchivos; i++)
                {
                    Entrada e = new Entrada();
                    e.nombre = Texto(f, f.ReadByte());
                    e.largo = (long)U64(f);
                    e.inicio = (long)U64(f);
                    s.archivos.Add(e);
                }
            }
            return s;
        }

        public Entrada Buscar(string nombre)
        {
            for (int i = 0; i < archivos.Count; i++)
            {
                if (string.Equals(archivos[i].nombre, nombre,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return archivos[i];
                }
            }
            return null;
        }

        // El chart que lleva dentro, sea cual sea su nombre.
        public Entrada BuscarChart(out bool esMidi)
        {
            esMidi = false;
            for (int i = 0; i < archivos.Count; i++)
            {
                string n = archivos[i].nombre;
                if (n.EndsWith(".mid", StringComparison.OrdinalIgnoreCase)
                    || n.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                {
                    esMidi = true;
                    return archivos[i];
                }
            }
            for (int i = 0; i < archivos.Count; i++)
            {
                if (archivos[i].nombre.EndsWith(".chart",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return archivos[i];
                }
            }
            return null;
        }

        public byte[] LeerArchivo(Entrada e)
        {
            byte[] d = new byte[e.largo];
            using (FileStream f = File.OpenRead(ruta))
            {
                f.Seek(e.inicio, SeekOrigin.Begin);
                Llenar(f, d, 0, d.Length);
            }
            Cifrar(d, mascara, 0);
            return d;
        }

        // El cifrado es simetrico: la misma pasada cifra y descifra.
        public static void Cifrar(byte[] d, byte[] mascara, long desde)
        {
            for (int i = 0; i < d.Length; i++)
            {
                long j = desde + i;
                d[i] = (byte)(d[i] ^ mascara[j % 16] ^ (byte)(j & 0xFF));
            }
        }

        // Sufijo del que se queda esperando cuando el juego no suelta el
        // archivo. Ver Intercambiar.
        public const string Pendiente = ".coolmod.new";

        // Reescribe el .sng cambiando UN archivo. Se escribe a un temporal y
        // solo al final se reemplaza el original: si algo se tuerce a medias,
        // la cancion del jugador sigue entera.
        //
        // Devuelve false si el archivo nuevo quedo esperando —ver Intercambiar—
        // en vez de haber ocupado ya su sitio.
        public bool Escribir(string destino, string nombre, byte[] contenido)
        {
            Entrada objetivo = Buscar(nombre);
            if (objetivo == null)
            {
                throw new FileNotFoundException("no esta en el .sng: " + nombre);
            }

            // 1. tamano de la cabecera nueva, para saber donde empiezan los datos
            long largoMeta = 8;
            for (int i = 0; i < metadatos.Count; i++)
            {
                largoMeta += 4 + Encoding.UTF8.GetByteCount(metadatos[i][0])
                           + 4 + Encoding.UTF8.GetByteCount(metadatos[i][1]);
            }
            long largoIndice = 8;
            for (int i = 0; i < archivos.Count; i++)
            {
                largoIndice += 1 + Encoding.UTF8.GetByteCount(archivos[i].nombre) + 8 + 8;
            }
            long inicioDatos = Cabecera + 4 + 16 + 8 + largoMeta + 8 + largoIndice + 8;

            // 2. posiciones nuevas, conservando el orden
            long[] nuevoInicio = new long[archivos.Count];
            long[] nuevoLargo = new long[archivos.Count];
            long cursor = inicioDatos;
            for (int i = 0; i < archivos.Count; i++)
            {
                nuevoLargo[i] = archivos[i] == objetivo ? contenido.Length : archivos[i].largo;
                nuevoInicio[i] = cursor;
                cursor += nuevoLargo[i];
            }
            long largoDatos = cursor - inicioDatos;

            // Si algo se tuerce a mitad de escribir, el temporal no se queda
            // ocupando disco: son diez o doscientos megas por cancion.
            string temporal = destino + ".tmp";
            try
            {
                EscribirTemporal(temporal, nuevoLargo, nuevoInicio, largoMeta,
                                 largoIndice, largoDatos, objetivo, contenido);
            }
            catch (Exception)
            {
                try { File.Delete(temporal); } catch (Exception) { }
                throw;
            }

            if (!Intercambiar(temporal, destino))
            {
                return false;      // se quedo esperando; el objeto no cambia
            }

            for (int i = 0; i < archivos.Count; i++)
            {
                archivos[i].largo = nuevoLargo[i];
                archivos[i].inicio = nuevoInicio[i];
            }
            ruta = destino;
            return true;
        }

        private void EscribirTemporal(string temporal, long[] nuevoLargo,
            long[] nuevoInicio, long largoMeta, long largoIndice, long largoDatos,
            Entrada objetivo, byte[] contenido)
        {
            using (FileStream o = File.Create(temporal))
            {
                o.Write(Magia, 0, Cabecera);
                EscribirU32(o, (uint)version);
                o.Write(mascara, 0, 16);

                EscribirU64(o, (ulong)largoMeta);
                EscribirU64(o, (ulong)metadatos.Count);
                for (int i = 0; i < metadatos.Count; i++)
                {
                    EscribirTexto32(o, metadatos[i][0]);
                    EscribirTexto32(o, metadatos[i][1]);
                }

                EscribirU64(o, (ulong)largoIndice);
                EscribirU64(o, (ulong)archivos.Count);
                for (int i = 0; i < archivos.Count; i++)
                {
                    byte[] n = Encoding.UTF8.GetBytes(archivos[i].nombre);
                    o.WriteByte((byte)n.Length);
                    o.Write(n, 0, n.Length);
                    EscribirU64(o, (ulong)nuevoLargo[i]);
                    EscribirU64(o, (ulong)nuevoInicio[i]);
                }
                EscribirU64(o, (ulong)largoDatos);

                // 3. los datos. Los que no cambian se copian CIFRADOS tal cual:
                //    como la mascara va con el indice dentro de cada archivo, y
                //    ese no cambia, siguen siendo validos donde caigan.
                using (FileStream f = File.OpenRead(ruta))
                {
                    byte[] buf = new byte[81920];
                    for (int i = 0; i < archivos.Count; i++)
                    {
                        if (archivos[i] == objetivo)
                        {
                            byte[] c = (byte[])contenido.Clone();
                            Cifrar(c, mascara, 0);
                            o.Write(c, 0, c.Length);
                            continue;
                        }
                        f.Seek(archivos[i].inicio, SeekOrigin.Begin);
                        long quedan = archivos[i].largo;
                        while (quedan > 0)
                        {
                            int n = f.Read(buf, 0,
                                (int)Math.Min(buf.Length, quedan));
                            if (n <= 0)
                            {
                                throw new EndOfStreamException(archivos[i].nombre);
                            }
                            o.Write(buf, 0, n);
                            quedan -= n;
                        }
                    }
                }
            }

        }

        // Poner el archivo nuevo en el sitio del viejo, que es la parte que de
        // verdad cuesta: EL JUEGO TIENE EL .sng ABIERTO mientras esta en la
        // lista de canciones, porque de ahi saca el audio de la vista previa.
        // Un chart suelto no da este problema; un .sng si.
        //
        // Tres intentos, de menos a mas aparatoso:
        //
        //   1. borrar y mover, lo normal;
        //   2. apartar el viejo a .old y mover el nuevo encima. Windows deja
        //      RENOMBRAR un archivo abierto si quien lo abrio lo permitio, y
        //      este es el mismo truco con el que el mod se actualiza a si mismo
        //      estando cargado;
        //   3. si tampoco, se deja el nuevo con el sufijo .coolmod.new y lo
        //      coloca el mod en el siguiente arranque, antes de que el juego
        //      abra nada.
        public static bool Intercambiar(string temporal, string destino)
        {
            try
            {
                if (File.Exists(destino))
                {
                    File.Delete(destino);
                }
                File.Move(temporal, destino);
                return true;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            string apartado = destino + "." + DateTime.Now.Ticks.ToString() + ".old";
            try
            {
                File.Move(destino, apartado);
                File.Move(temporal, destino);
                try { File.Delete(apartado); } catch (Exception) { }
                return true;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            try
            {
                if (File.Exists(apartado) && !File.Exists(destino))
                {
                    File.Move(apartado, destino);      // deshacer a medias
                }
            }
            catch (Exception)
            {
            }

            string espera = destino + Pendiente;
            try
            {
                if (File.Exists(espera))
                {
                    File.Delete(espera);
                }
                File.Move(temporal, espera);
            }
            catch (Exception)
            {
                try { File.Delete(temporal); } catch (Exception) { }
                throw;
            }
            return false;
        }

        // ---------------------------------------------------------- ayudas --
        private static void Llenar(Stream s, byte[] d, int i, int n)
        {
            while (n > 0)
            {
                int leidos = s.Read(d, i, n);
                if (leidos <= 0)
                {
                    throw new EndOfStreamException();
                }
                i += leidos;
                n -= leidos;
            }
        }

        private static byte[] Bytes(Stream s, int n)
        {
            byte[] d = new byte[n];
            Llenar(s, d, 0, n);
            return d;
        }

        private static string Texto(Stream s, int n)
        {
            return n <= 0 ? "" : Encoding.UTF8.GetString(Bytes(s, n));
        }

        private static uint U32(Stream s)
        {
            byte[] d = Bytes(s, 4);
            return (uint)(d[0] | (d[1] << 8) | (d[2] << 16) | (d[3] << 24));
        }

        private static ulong U64(Stream s)
        {
            byte[] d = Bytes(s, 8);
            ulong v = 0;
            for (int i = 7; i >= 0; i--)
            {
                v = (v << 8) | d[i];
            }
            return v;
        }

        private static void EscribirU32(Stream s, uint v)
        {
            for (int i = 0; i < 4; i++)
            {
                s.WriteByte((byte)(v >> (8 * i)));
            }
        }

        private static void EscribirU64(Stream s, ulong v)
        {
            for (int i = 0; i < 8; i++)
            {
                s.WriteByte((byte)(v >> (8 * i)));
            }
        }

        private static void EscribirTexto32(Stream s, string t)
        {
            byte[] d = Encoding.UTF8.GetBytes(t ?? "");
            EscribirU32(s, (uint)d.Length);
            s.Write(d, 0, d.Length);
        }
    }
}
