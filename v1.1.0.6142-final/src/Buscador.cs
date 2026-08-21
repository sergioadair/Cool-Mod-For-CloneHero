using MelonLoader;

namespace CloneHeroMod
{
    // Control de las busquedas de objetos de Unity.
    //
    // FindObjectOfType recorre TODOS los objetos cargados, asi que repetirla a
    // ritmo fijo cuesta caro cuando el objeto no existe: durante una cancion no
    // hay ni SongSelect ni EndOfSong ni MenuBackground, y aun asi se seguia
    // buscando dos veces por segundo cada uno.
    //
    // La regla aqui es: si no se encuentra, esperar cada vez mas (hasta 5 s), y
    // volver a intentarlo enseguida en cuanto cambia la escena, que es cuando
    // de verdad puede haber aparecido algo.
    public static class Buscador
    {
        public const int EsperaMinima = 30;     // medio segundo
        public const int EsperaMaxima = 300;    // cinco segundos

        // Sube en cada cambio de escena. Cada consumidor compara contra su
        // copia para saber si tiene que reiniciar su espera.
        private static int generacion;

        public static int Generacion
        {
            get { return generacion; }
        }

        // La escena de juego. Mientras esta activa el mod no tiene NADA que
        // hacer: no existen ni SongSelect ni MenuBackground ni EndOfSong, y es
        // justo el momento en que cualquier trabajo se nota como un tiron.
        public const string EscenaJuego = "Gameplay";

        private static bool enJuego;

        public static bool EnJuego
        {
            get { return enJuego; }
        }

        public static void EscenaCambiada(string nombre)
        {
            generacion++;
            enJuego = nombre == EscenaJuego;
            MelonLogger.Msg("[Buscador] escena: " + nombre
                            + (enJuego ? "  (mod en pausa)" : ""));
        }

        // Estado de una busqueda concreta.
        public class Intento
        {
            private int proximoFotograma;
            private int espera = EsperaMinima;
            private int generacionVista = -1;
            private readonly int desfase;

            // El desfase evita que todas las busquedas caigan en el mismo
            // fotograma justo despues de cargar una escena, que es cuando el
            // juego ya va apretado.
            public Intento(int desfase)
            {
                this.desfase = desfase;
            }

            // true si toca buscar ahora.
            public bool Toca()
            {
                if (generacionVista != generacion)
                {
                    generacionVista = generacion;
                    espera = EsperaMinima;
                    // Se deja pasar un poco tras cargar la escena: buscar justo
                    // en los fotogramas de carga se nota como un tiron.
                    proximoFotograma = UnityEngine.Time.frameCount + EsperaMinima + desfase;
                    return false;
                }
                return UnityEngine.Time.frameCount >= proximoFotograma;
            }

            public void Fallo()
            {
                espera = espera * 2;
                if (espera > EsperaMaxima)
                {
                    espera = EsperaMaxima;
                }
                proximoFotograma = UnityEngine.Time.frameCount + espera;
            }

            public void Exito()
            {
                espera = EsperaMinima;
                proximoFotograma = UnityEngine.Time.frameCount + EsperaMinima;
            }
        }
    }
}
