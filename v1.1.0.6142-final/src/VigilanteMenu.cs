using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;

namespace CloneHeroMod
{
    // Detecta cuando el jugador ABRE una de nuestras filas en un menu de
    // ajustes, para conmutar su valor.
    //
    // POR QUE NO SE ENGANCHA A LOS EVENTOS DEL MENU:
    //
    // Durante mucho tiempo esto colgaba de los metodos "Select" del menu,
    // parcheados con Harmony, y salieron tres problemas distintos:
    //
    //   - En Video habia que pulsar la opcion y LUEGO MOVER HACIA ABAJO. No
    //     porque se leyera ninguna tecla —el mod no toca el teclado— sino
    //     porque el juego enruta cada direccion a un metodo distinto y solo uno
    //     estaba parcheado. Arriba no hacia nada. Y si el juego no llegaba a
    //     emitir ese evento concreto, la opcion NO SE PODIA CAMBIAR de ninguna
    //     forma desde el juego: a un usuario con teclado le paso.
    //   - En Gameplay, el metodo que se localizaba emparejando la ranura
    //     virtual con GeneralSettingsMenu resulto ser el que corre al SALIR del
    //     menu, asi que la opcion se conmutaba sola al abandonarlo.
    //   - En Video, ademas, al PULSAR la propiedad de "opcion abierta" todavia
    //     no esta puesta cuando corre el postfix, asi que hacia falta un
    //     segundo evento posterior si o si.
    //
    // Nada de eso depende de nosotros, asi que se deja de depender de ellos: se
    // vigila la propiedad de "opcion abierta" fotograma a fotograma y se
    // dispara en el FLANCO, cuando pasa a contener una de nuestras filas. Eso
    // es exactamente "el jugador abrio esta opcion", venga de donde venga la
    // pulsacion, y mantenerla abierta y moverse ya no la conmuta en bucle.
    //
    // Solo corre en los menus: durante la cancion, Diagnostico.OnUpdate sale
    // antes. El coste por fotograma es leer una propiedad string.
    public class VigilanteMenu
    {
        private readonly string[] prefijos;
        private readonly List<PropertyInfo> candidatas = new List<PropertyInfo>();
        private readonly string etiqueta;
        private string anterior = "";

        public VigilanteMenu(string etiqueta, params string[] prefijos)
        {
            this.etiqueta = etiqueta;
            this.prefijos = prefijos;
        }

        // Se llama al ABRIR el menu, en el prefix de OnEnable.
        //
        // Cual es la propiedad de "opcion abierta" no se sabe de antemano: los
        // nombres los genera Il2CppInterop. Se reconoce porque en este momento
        // esta VACIA — la de la fila resaltada nunca lo esta mientras el menu
        // se ve. Y hay que mirarlo aqui, no al primer evento: si no, la primera
        // pulsacion seria tambien la primera observacion y no conmutaria.
        public void Preparar(object menu)
        {
            try
            {
                anterior = "";      // al reabrir el menu no hay nada abierto
                if (candidatas.Count > 0 || menu == null)
                {
                    return;
                }
                PropertyInfo[] props = menu.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < props.Length; i++)
                {
                    if (!EsTexto(props[i]))
                    {
                        continue;
                    }
                    string v;
                    try { v = props[i].GetValue(menu) as string; }
                    catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(v))
                    {
                        candidatas.Add(props[i]);
                    }
                }
                if (candidatas.Count == 0)
                {
                    // Si en ese instante ninguna estaba vacia, se miran todas
                    // menos las de Unity, que nunca lo estan.
                    for (int i = 0; i < props.Length; i++)
                    {
                        if (EsTexto(props[i])
                            && props[i].Name != "name" && props[i].Name != "tag")
                        {
                            candidatas.Add(props[i]);
                        }
                    }
                }
                MelonLogger.Msg("[" + etiqueta + "] candidatas a 'opcion abierta': "
                                + candidatas.Count.ToString());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[" + etiqueta + "] preparar: " + ex.Message);
            }
        }

        private static bool EsTexto(PropertyInfo p)
        {
            return p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0;
        }

        // Devuelve el prefijo de la fila que ACABA de abrirse, o null. Se llama
        // en cada fotograma de menu.
        public string RecienAbierta(object menu)
        {
            if (menu == null || candidatas.Count == 0)
            {
                return null;
            }
            try
            {
                string abierta = "";
                string cual = null;
                for (int i = 0; i < candidatas.Count && cual == null; i++)
                {
                    string v;
                    try { v = candidatas[i].GetValue(menu) as string; }
                    catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(v))
                    {
                        continue;
                    }
                    // Solo cuentan nuestras filas: que el juego abra una opcion
                    // suya equivale, para esto, a que no haya nada abierto.
                    for (int j = 0; j < prefijos.Length; j++)
                    {
                        if (v.StartsWith(prefijos[j], StringComparison.Ordinal))
                        {
                            abierta = v;
                            cual = prefijos[j];
                            break;
                        }
                    }
                }
                if (abierta == anterior)
                {
                    return null;      // lo normal: nada ha cambiado
                }
                anterior = abierta;
                return cual;          // null si lo que hubo fue un cierre
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
