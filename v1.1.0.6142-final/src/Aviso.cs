using System;

namespace CloneHeroMod
{
    // Un mensaje en pantalla que se va solo. Lo pinta OverlayProgreso, que ya
    // tiene el cartel montado; aqui solo se guarda el texto.
    //
    // Existe para que lo pueda llamar el hilo de generacion, que no puede tocar
    // nada de Unity. Guardar dos cadenas si puede, y el hilo principal las
    // recoge en su Tick.
    //
    // TODO EL TEXTO EN INGLES: se ve en pantalla.
    public static class Aviso
    {
        // Cortos a proposito. Un cartel que se queda mucho rato en pantalla
        // parece que algo ha ido mal, aunque diga que todo fue bien.
        private const double Segundos = 4.5;

        private static volatile string titulo;
        private static volatile string cuerpo;
        private static DateTime hasta;

        public static void Mostrar(string tit, string texto)
        {
            titulo = tit;
            cuerpo = texto;
            hasta = DateTime.UtcNow.AddSeconds(Segundos);
        }

        public static void Ocultar()
        {
            titulo = null;
            cuerpo = null;
        }

        public static bool Activo
        {
            get { return titulo != null && DateTime.UtcNow < hasta; }
        }

        public static string Texto
        {
            get
            {
                string t = titulo;
                string c = cuerpo;
                if (t == null)
                {
                    return "";
                }
                return t + "\n\n" + (c ?? "");
            }
        }
    }
}
