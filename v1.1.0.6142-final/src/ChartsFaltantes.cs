using System;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;

namespace CloneHeroMod
{
    // Las dos filas del mod al final de Song Options —la lista que sale al
    // pulsar Select sobre una cancion, debajo de "Toggle Favorite"—: generar
    // las dificultades que falten y devolver el chart original.
    //
    // Aqui solo esta el enganche con el menu; el trabajo lo hace
    // GeneradorCharts, y el algoritmo ReduccionChart.
    //
    // ---------------------------------------------------------------------
    // 1. DONDE SE ANADE
    //
    // SongOptions REDEFINE OnEnable, y ahi es donde monta la lista. El parche
    // entra en ese OnEnable —con DeclaredOnly, porque si no se coge el de
    // BaseMenu, que corre despues— y no toca menuStrings sino mainOptions, que
    // es de donde el juego copia.
    //
    // Y NO SE CLONA NINGUNA FILA. Se intento y quedo un destrozo: cada fila es
    // un contenedor de 540x50 dentro de un VerticalLayoutGroup y la etiqueta
    // cuelga del contenedor, no del layout, asi que clonar la etiqueta la metia
    // DENTRO de la ultima fila —tres textos superpuestos— y clonar el
    // contenedor anadia filas que el juego no conocia, desbordando la caja.
    //
    // Resulta que no hacia ninguna falta. Medido con el panel ya montado:
    //
    //     menuStrings=9  etiquetas=7  caja=BackgroundBox alto=380  hijos=7
    //
    // Nueve opciones sobre siete filas fisicas: el panel no crece, DESPLAZA una
    // ventana de siete filas sobre la lista (BaseMenu.scrollWholeMenu,
    // startScrollingTop/Bottom). Anadir la cadena es todo lo que hay que hacer.
    //
    // ---------------------------------------------------------------------
    // 2. QUE BOTON ES CADA METODO
    //
    // Los nombres los genera Il2CppInterop y no dicen nada, asi que se
    // instrumentaron los metodos de SongOptions y se miro cual aparecia al
    // pulsar cada cosa:
    //
    //     _8   una vez por fila al bajar          -> abajo
    //     _4   aparejado con _8 al subir          -> arriba
    //     _2   una sola vez al pulsar             -> VERDE (confirmar)
    //     _3   llamado DESDE _2                   -> CERRAR el panel
    //
    // VigilanteMenu no vale para detectar la pulsacion. En los menus de ajustes
    // detecta que una fila se ABRE, pero este panel no tiene ese estado:
    // ejecuta la accion y ya. Lo que detectaba era que la fila se RESALTA, o
    // sea que el panel se cerraba solo con bajar hasta ella. Aqui se usa solo
    // para leer cual es la fila resaltada.
    //
    // ---------------------------------------------------------------------
    // 3. POR QUE CORRE EL ORIGINAL Y POR QUE SE CIERRA LLAMANDO A _3
    //
    // Cuatro intentos, y todos ensenaron algo:
    //
    //  a) Cortar _2 con un prefix, para que el juego no despachase una opcion
    //     que no conoce. Peor: al accionar la fila se SELECCIONABA LA CANCION,
    //     como si el verde se hubiera pulsado con el panel cerrado. Ese metodo
    //     tambien da la pulsacion por consumida; saltandolo, el verde seguia
    //     vivo y acababa atendiendolo SongSelect.
    //
    //  b) Dejarlo correr y esconder el panel con gameObject.SetActive(false).
    //     Al REABRIRLO no se podia navegar ni cerrarlo; solo un Back lo
    //     revivia. Se culpo a la pila de estados de SongOptions.
    //
    //  c) Rebobinar esa pila. Midiendola: no se movia (0 -> 0, Main -> Main).
    //     Tampoco ningun bool, ni ningun int, ni ningun float del componente.
    //     El despacho del juego resuelve la opcion a un indice de accion, para
    //     la nuestra le sale -1, y NO HACE NADA.
    //
    //  d) Limpiar isActive a mano antes de apagar el objeto, y despues retrasar
    //     el apagado por si el juego esperaba a que se soltase el boton.
    //     Ninguna de las dos.
    //
    // El error de fondo era el mismo en todas: reproducir el cierre en vez de
    // llamarlo. Instrumentando entrada y salida de cada metodo con isActive
    // delante, una pulsacion sana lo enseno de una vez:
    //
    //     > _2   isActive=1 activo=1
    //       > _3   isActive=1 activo=1
    //       < _3   isActive=0 activo=0     <- el cierre entero pasa aqui dentro
    //     < _2   isActive=0 activo=0
    //
    // Asi que el original corre entero —la pulsacion se consume, la cancion no
    // se selecciona— y para cerrar se llama a _3, que es lo que el juego llama.
    // Lo que haga por dentro deja de ser asunto nuestro.
    public static class ChartsFaltantes
    {
        public const string Fila = "Generate Missing Difficulties";
        public const string FilaRestaurar = "Restore Song Chart";

        private static bool parcheado;
        private static Il2Cpp.SongOptions panel;
        private static bool accionada;
        private static string pulsada;
        private static readonly VigilanteMenu resaltado =
            new VigilanteMenu("Charts", Fila, FilaRestaurar);

        public static void InstalarParches(HarmonyLib.Harmony harmony)
        {
            if (parcheado)
            {
                return;
            }
            parcheado = true;
            try
            {
                Type t = typeof(Il2Cpp.SongOptions);
                BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.DeclaredOnly;

                MethodInfo onEnable = t.GetMethod("OnEnable", f);
                MethodInfo confirmar = t.GetMethod("Method_Public_Virtual_Void_2", f);
                MethodInfo pre = typeof(ChartsFaltantes).GetMethod("PreOnEnable",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo preConf = typeof(ChartsFaltantes).GetMethod("PreConfirmar",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo postConf = typeof(ChartsFaltantes).GetMethod("PostConfirmar",
                    BindingFlags.NonPublic | BindingFlags.Static);

                // Cualquiera de los cuatro a null y no se parchea nada. Paso
                // una vez —un registro apuntando a un metodo propio que ya no
                // existia—: HarmonyMethod(null) lanza, el catch se lo tragaba y
                // quedaba la fila puesta pero muerta, sin una linea en el log.
                if (onEnable == null || confirmar == null || pre == null
                    || preConf == null || postConf == null)
                {
                    MelonLogger.Warning("[Charts] no se resolvieron los metodos a"
                        + " parchear; la fila no se anade");
                    return;
                }

                harmony.Patch(onEnable, new HarmonyMethod(pre), null);
                harmony.Patch(confirmar, new HarmonyMethod(preConf),
                                         new HarmonyMethod(postConf));
                MelonLogger.Msg("[Charts] instalado");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Charts] " + ex);
            }
        }

        private static void PreOnEnable(Il2CppSystem.Object __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }
                Il2Cpp.SongOptions o = __instance.TryCast<Il2Cpp.SongOptions>();
                if (o == null)
                {
                    return;
                }
                panel = o;
                resaltado.Preparar(o);
                Anadir(o);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Charts] PreOnEnable: " + ex);
            }
        }

        // mainOptions conserva su nombre de verdad —el ofuscador respeta los
        // campos serializados de Unity—, asi que no hay que ir a buscarlo.
        private static void Anadir(Il2Cpp.SongOptions o)
        {
            Il2CppStringArray filas = o.mainOptions;
            if (filas == null)
            {
                MelonLogger.Warning("[Charts] mainOptions vacio");
                return;
            }
            for (int i = 0; i < filas.Length; i++)
            {
                if (filas[i] == Fila)
                {
                    return;      // ya estaban: el panel se reabre muchas veces
                }
            }

            Il2CppStringArray nuevas = new Il2CppStringArray(filas.Length + 2);
            for (int i = 0; i < filas.Length; i++)
            {
                nuevas[i] = filas[i];
            }
            nuevas[filas.Length] = Fila;
            nuevas[filas.Length + 1] = FilaRestaurar;
            o.mainOptions = nuevas;
            MelonLogger.Msg("[Charts] filas anadidas a mainOptions ("
                + filas.Length.ToString() + " -> " + nuevas.Length.ToString() + ")");
        }

        // Corre para CUALQUIER opcion que se pulse, asi que lo primero es
        // comprobar que la resaltada es la nuestra. No se corta la llamada: el
        // original tiene que correr entero, ver el punto 3.
        private static void PreConfirmar()
        {
            try
            {
                if (panel == null)
                {
                    return;
                }
                string sobre = resaltado.Actual(panel);
                if (sobre == null)
                {
                    return;
                }
                if (sobre.StartsWith(Fila, StringComparison.Ordinal)
                    || sobre.StartsWith(FilaRestaurar, StringComparison.Ordinal))
                {
                    pulsada = sobre;
                    accionada = true;
                }
            }
            catch (Exception)
            {
            }
        }

        // El cierre se llama AQUI, nada mas volver el original, y no un
        // fotograma despues desde el Tick. Se probo lo segundo y _3 no hacia
        // nada: ni cerraba ni lanzaba. En la traza sana _3 corre dentro de _2,
        // asi que algo del contexto de esa llamada le hace falta; llamandolo
        // desde el postfix estamos justo donde el juego lo llamaria.
        private static void PostConfirmar()
        {
            if (!accionada)
            {
                return;
            }
            accionada = false;
            try
            {
                if (panel == null)
                {
                    return;
                }
                panel.Method_Public_Virtual_Void_3();
                Actuar(pulsada);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Charts] cerrar: " + ex.Message);
            }
        }

        // La ruta del chart la da EtiquetaDificultad, que ya vigila cual es la
        // cancion resaltada — y que ademas descarta las cifradas y los .sng,
        // donde no hay archivo suelto que copiar ni reescribir.
        private static void Actuar(string fila)
        {
            string ruta;
            bool esMidi;
            bool esSng;
            if (!EtiquetaDificultad.ArchivoActual(out ruta, out esMidi, out esSng))
            {
                Aviso.Mostrar(fila ?? Fila,
                    "Encrypted song - its chart cannot be edited.");
                return;
            }
            if (fila != null && fila.StartsWith(FilaRestaurar, StringComparison.Ordinal))
            {
                MelonLogger.Msg("[Charts] restaurar " + ruta);
                GeneradorCharts.Restaurar(ruta);
                return;
            }
            MelonLogger.Msg("[Charts] generar sobre " + ruta);
            GeneradorCharts.Lanzar(ruta, esMidi, esSng);
        }

        public static void Tick()
        {
        }
    }
}
