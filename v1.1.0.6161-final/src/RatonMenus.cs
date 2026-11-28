using System;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace CloneHeroMod
{
    // El raton en los menus: menu principal (y su confirmacion de Quit),
    // Settings con todas sus pantallas, Credits, la casilla del jugador 1
    // (unirse, elegir perfil y sus opciones) y la lista de canciones.
    //
    // EL JUEGO NO LO TRAE: sus menus solo escuchan al mando y al teclado
    // (BaseMenu.DoInput). Las filas son textos, no botones de Unity, asi que
    // el raton no las puede pulsar por su cuenta.
    //
    // LO QUE SE HACE es llamar a las mismas funciones que llaman los botones,
    // por su nombre real (Nativo): ConfirmPressed para el verde, CancelPressed
    // para el rojo, UpPressed/DownPressed para moverse. Son virtuales y el
    // interop las despacha al menu concreto (MainMenu, GeneralSettingsMenu...),
    // asi que cada menu hace exactamente lo que haria con la guitarra. Las
    // filas que anade el mod tambien, porque cuelgan de esas mismas funciones.
    //
    // DOS MODOS, segun el menu:
    //   sin scroll (menu principal, secciones de Settings, confirmaciones):
    //     pasar por encima resalta la fila, el clic la elige.
    //   con scroll (pantallas de ajustes, opciones del perfil, canciones):
    //     mover el raton no hace nada (si resaltara, al llegar abajo el menu
    //     se desplaza, debajo queda otra fila y en un instante esta al final);
    //     la rueda y la barra mueven, y el clic en una fila visible la resalta
    //     y la elige. En los ajustes, clic en interruptores y arrastre de
    //     barras.
    //   en todos: clic derecho = rojo.
    //
    // Solo actua cuando el raton se mueve, hace clic o gira la rueda: si se
    // queda quieto encima del menu no estorba a la guitarra.
    //
    // Respeta "Show Mouse Cursor" (Video): con el cursor oculto no hace nada.
    // Y se aparta cuando el juego no atenderia al mando: editores de rutas o
    // colores abiertos (usan el raton a su manera), menus con los controles
    // desactivados, el panel de importar o un trabajo del mod en marcha.
    public static class RatonMenus
    {
        private static bool resuelto;
        private static bool listo;
        private static MethodInfo mConfirmar;
        private static MethodInfo mCancelar;
        private static MethodInfo mSoltarConfirmar;
        private static MethodInfo mIrACancion;        // BaseMenu.m71(int, bool) en SongSelect
        private static PropertyInfo pCuentaLista;     // SongDirectory: largo de la lista visible
        private static MethodInfo mArriba;
        private static MethodInfo mAbajo;
        private static MethodInfo mSoltarArriba;
        private static MethodInfo mSoltarAbajo;
        private static MethodInfo mActualizar;
        private static MethodInfo mSalirCreditos;
        private static PropertyInfo pBarra;
        private static PropertyInfo pLista;
        private static MethodInfo mAsignar;
        private static MethodInfo mEditarPerfil;
        private static PropertyInfo pJugadorMenu;
        private static PropertyInfo pEditandoPerfil;
        private static PropertyInfo pPerfil;
        private static PropertyInfo pPosicion;
        private static PropertyInfo pSeleccion;
        private static PropertyInfo pCambiando;
        private static MethodInfo[] editores;

        private static Vector3 ultimo;
        private static bool hayUltimo;
        private static Il2Cpp.BaseMenu[] menus;
        private static float buscarEn;

        private static bool Resolver()
        {
            if (resuelto)
            {
                return listo;
            }
            resuelto = true;
            try
            {
                Type t = typeof(Il2Cpp.BaseMenu);
                mConfirmar = Nativo.Metodo(t, Real.Confirmar);
                mCancelar = Nativo.Metodo(t, Real.Cancelar);
                mSoltarConfirmar = Nativo.Metodo(t, Real.SoltarConfirmar);
                mIrACancion = Nativo.Metodo(typeof(Il2Cpp.SongSelect), Real.Seleccionar,
                                            typeof(int), typeof(bool));
                pCuentaLista = Nativo.Propiedad(
                    typeof(Il2Cpp.ObjectPublicAbstractSealedLi1SoDi2ObInLi1SoUnique), Real.CuentaLista);
                mArriba = Nativo.Metodo(t, Real.Arriba);
                mAbajo = Nativo.Metodo(t, Real.Abajo);
                mSoltarArriba = Nativo.Metodo(t, Real.SoltarArriba);
                mSoltarAbajo = Nativo.Metodo(t, Real.SoltarAbajo);
                mActualizar = Nativo.Metodo(t, Real.ActualizarMenu);
                mSalirCreditos = Nativo.Metodo(typeof(Il2Cpp.CreditsScroll), Real.SalirCreditos);
                pBarra = Nativo.Campo(t, Real.BarraMenu);
                pLista = Nativo.Campo(t, Real.ListaMenu);
                mAsignar = Nativo.Metodo(typeof(Il2Cpp.PlayerProfileMenu), Real.AsignarJugador,
                                         typeof(Il2CppRewired.Player));
                mEditarPerfil = Nativo.Metodo(typeof(Il2Cpp.PlayerProfileMenu), Real.EditarPerfil);
                pJugadorMenu = Nativo.Campo(t, Real.JugadorMenu);
                Type tj = typeof(Il2Cpp.ObjectPublicObPlInPlBoBoObBoObBoUnique);      // CHPlayer
                pEditandoPerfil = Nativo.Campo(tj, Real.EditandoPerfil);
                pPerfil = Nativo.Campo(tj, Real.PerfilJugador);
                pPosicion = Nativo.Campo(t, Real.PosicionMenu);
                pSeleccion = Nativo.Propiedad(t, Real.SeleccionActual);
                pCambiando = Nativo.Campo(t, Real.CambiandoAjuste);
                editores = new[]
                {
                    Nativo.Metodo(typeof(Il2Cpp.SongPathsManager), Real.EditorAbierto),
                    Nativo.Metodo(typeof(Il2Cpp.ColorProfileEditor), Real.EditorAbierto),
                    Nativo.Metodo(typeof(Il2Cpp.ContentPathsManager), Real.EditorAbierto)
                };
                listo = mConfirmar != null && mCancelar != null && mArriba != null && mAbajo != null
                     && pPosicion != null && pSeleccion != null;
                MelonLogger.Msg("[Raton] " + (listo ? "listo"
                    : "faltan funciones del menu: el raton no se activa"));
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Raton] " + ex);
                listo = false;
            }
            return listo;
        }

        public static void EscenaCambiada()
        {
            menus = null;
            hayUltimo = false;
            arrastre = null;
            casillas = null;
            deslizando = null;
            barraAjuste = null;
            scroll.Clear();
        }

        // Se mira una vez por menu. Con scroll: las pantallas de ajustes
        // (BaseSettingMenu: General, Video, Audio...) y cualquier menu con mas
        // opciones que filas visibles.
        //
        // Antes se miraba ademas si las filas iban dentro de un ScrollRect que
        // no cabe, y la lista de secciones de Settings (6 opciones, 6 filas)
        // salio "con scroll": el ScrollRect que encontraba era otro de la
        // pantalla, no el suyo.
        private static readonly System.Collections.Generic.Dictionary<IntPtr, bool> scroll =
            new System.Collections.Generic.Dictionary<IntPtr, bool>();

        // La casilla del jugador 1 (PlayerProfileMenu) es un mismo menu para
        // dos cosas: la lista de perfiles y, ya elegido, las opciones del
        // perfil, que si tienen scroll. Por eso esa se vuelve a mirar cada
        // vez: guardada, se quedaba con lo que vio la primera vez.
        private static bool ConScroll(Il2Cpp.BaseMenu b)
        {
            bool cambiante = b.TryCast<Il2Cpp.PlayerProfileMenu>() != null;
            bool antes;
            bool habia = scroll.TryGetValue(b.Pointer, out antes);
            if (habia && !cambiante)
            {
                return antes;
            }
            bool r = false;
            try
            {
                var textos = b.textObjects;
                var opciones = b.menuStrings;
                int visibles = 0;
                Il2CppTMPro.TextMeshProUGUI primera = null;
                if (textos != null)
                {
                    for (int j = 0; j < textos.Length; j++)
                    {
                        if (textos[j] != null)
                        {
                            visibles++;
                            if (primera == null) primera = textos[j];
                        }
                    }
                }
                r = b.TryCast<Il2Cpp.BaseSettingMenu>() != null
                    || b.TryCast<Il2Cpp.SongSelect>() != null
                    || (opciones != null && opciones.Length > visibles)
                    || Desborda(b)
                    || (cambiante && ConPerfil(b));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Raton] scroll: " + ex.Message);
            }
            scroll[b.Pointer] = r;
            return r;
        }

        public static void Tick()
        {
            try
            {
                // Una vez al principio, para que el log diga enseguida si
                // falta alguna funcion del juego.
                if (!resuelto)
                {
                    Resolver();
                }
                Vector3 m = Input.mousePosition;
                bool movio = hayUltimo && (m - ultimo).sqrMagnitude > 0.25f;
                ultimo = m;
                hayUltimo = true;
                float rueda = Input.mouseScrollDelta.y;
                bool clic = Input.GetMouseButtonDown(0);
                bool derecho = Input.GetMouseButtonDown(1);
                bool girando = rueda > 0.01f || rueda < -0.01f;

                // Arrastrando un deslizador de ajuste: igual, hasta soltar.
                if (deslizando != null)
                {
                    if (!Input.GetMouseButton(0) || !Valido(deslizando))
                    {
                        SoltarDeslizador();
                    }
                    else
                    {
                        Deslizar(m);
                        return;
                    }
                }

                // Arrastrando la barra: mientras siga el boton pulsado, nada
                // mas cuenta.
                if (arrastre != null)
                {
                    if (!Input.GetMouseButton(0) || !Valido(arrastre))
                    {
                        arrastre = null;
                    }
                    else
                    {
                        if (movio) Arrastrar(m);
                        return;
                    }
                }

                if (!movio && !clic && !derecho && !girando)
                {
                    return;
                }
                if (!Cursor.visible || ImportarCopia.BloqueaMenus || OverlayProgreso.Trabajando)
                {
                    return;
                }
                if (!Resolver() || Il2Cpp.BaseMenu.transitioning || EditorAbierto())
                {
                    return;
                }
                if ((clic || derecho) && Creditos())
                {
                    return;
                }

                // El menu que tiene el raton encima; si ninguno, el que
                // atiende (para la rueda y el clic derecho).
                Il2Cpp.BaseMenu menu = null;
                Il2Cpp.BaseMenu atiende = null;
                int fila = -1;
                Il2Cpp.BaseMenu[] lista = Menus();
                // Una ventana de confirmacion (Quit: "Are you sure...?") va
                // encima de todo, y la eleccion de perfil del jugador 1 tambien
                // (el juego no deja que ese jugador toque otro menu mientras
                // tanto): mientras esten, solo ellas atienden, o el raton
                // tocaria el menu de detras.
                int exclusiva = 0;
                for (int i = 0; i < lista.Length; i++)
                {
                    if (Valido(lista[i]) && Prioridad(lista[i]) >= PrioridadPerfil)
                    {
                        exclusiva = Math.Max(exclusiva, Prioridad(lista[i]));
                    }
                }
                bool hayConfirmacion = exclusiva > 0;

                // La casilla del jugador 1 ("Press Start"): unirse o abrir
                // las opciones del perfil.
                if (clic && exclusiva < PrioridadConfirmacion && Casilla(m))
                {
                    return;
                }

                for (int i = 0; i < lista.Length; i++)
                {
                    Il2Cpp.BaseMenu b = lista[i];
                    if (!Valido(b) || (exclusiva > 0 && Prioridad(b) != exclusiva))
                    {
                        continue;
                    }
                    if (atiende == null || Prioridad(b) > Prioridad(atiende))
                    {
                        atiende = b;
                    }
                    int j = FilaBajo(b, m);
                    if (j >= 0)
                    {
                        menu = b;
                        fila = j;
                    }
                }
                // Con las opciones del perfil abiertas, un clic fuera del panel
                // las cierra (y no llega al menu de detras).
                if (clic && fila < 0 && exclusiva == PrioridadPerfil && atiende != null)
                {
                    Il2Cpp.PlayerProfileMenu pp = atiende.TryCast<Il2Cpp.PlayerProfileMenu>();
                    if (pp != null && ConPerfil(pp) && !DentroDelPanel(pp, m))
                    {
                        CerrarPerfil(pp);
                        return;
                    }
                }
                if (menu == null)
                {
                    menu = atiende;
                }
                if (menu == null)
                {
                    return;
                }

                // Un clic en la barra de desplazamiento empieza a arrastrarla.
                if (clic && !hayConfirmacion && atiende != null && EmpezarArrastre(atiende, m))
                {
                    return;
                }

                // Dentro de una pantalla de ajustes, un clic en otra seccion de
                // la lista de la izquierda cambia a esa.
                if (clic && !hayConfirmacion && atiende != null && OtraSeccion(atiende, m))
                {
                    return;
                }

                bool editando = pCambiando != null && pCambiando.GetValue(menu) as string != null;
                if (derecho)
                {
                    mCancelar.Invoke(menu, null);
                    return;
                }
                if (girando)
                {
                    Paso(menu, rueda > 0f);
                    return;
                }
                // MENUS CON SCROLL (las pantallas de Settings): pasar el raton
                // no mueve la seleccion; eso es de la rueda y de la barra. Si
                // el paso del raton resaltara filas, al llegar a la de abajo el
                // menu se desplaza para ensenarla, debajo del raton queda
                // otra, vuelve a desplazarse... y en un instante esta al
                // final. Un CLIC en una opcion visible si la resalta y la
                // elige: es un solo movimiento, no una cadena.
                // Un clic en el interruptor o en la barra de un ajuste.
                if (clic && EnVista(menu, m) && Control(menu, fila, m, editando))
                {
                    return;
                }
                if (ConScroll(menu))
                {
                    if (!clic || fila < 0 || !EnVista(menu, m))
                    {
                        return;
                    }
                    if (fila != (int)pPosicion.GetValue(menu))
                    {
                        if (editando)
                        {
                            // Primero se cierra el ajuste abierto, como el
                            // verde; si no se cierra, no se cambia de opcion.
                            Confirmar(menu);
                            if (pCambiando.GetValue(menu) as string != null)
                            {
                                return;
                            }
                        }
                        if (!IrA(menu, fila))
                        {
                            return;      // no llego (separador o tope): no se elige otra
                        }
                    }
                    Confirmar(menu);
                    return;
                }
                if (editando)
                {
                    // Con un ajuste abierto el raton no mueve la seleccion: el
                    // clic lo cierra, como el verde.
                    if (clic)
                    {
                        Confirmar(menu);
                    }
                    return;
                }
                if (fila < 0)
                {
                    return;
                }
                IrA(menu, fila);
                if (clic)
                {
                    Confirmar(menu);
                }
            }
            catch (Exception ex)
            {
                // Una vez: si fallara en cada movimiento llenaria el log.
                if (!avisadoError)
                {
                    avisadoError = true;
                    MelonLogger.Warning("[Raton] " + ex);
                }
            }
        }

        private static bool avisadoError;

        private static Il2Cpp.BaseMenu[] Menus()
        {
            if (menus == null || Time.unscaledTime >= buscarEn)
            {
                menus = UnityEngine.Object.FindObjectsOfType<Il2Cpp.BaseMenu>();
                buscarEn = Time.unscaledTime + 0.5f;
            }
            return menus;
        }

        private static bool EditorAbierto()
        {
            for (int i = 0; i < editores.Length; i++)
            {
                if (editores[i] != null && editores[i].Invoke(null, null) is bool b && b)
                {
                    return true;
                }
            }
            return false;
        }

        // El menu principal, Settings, las confirmaciones y la eleccion de
        // perfil del jugador 1.
        private const int PrioridadPerfil = 4;
        private const int PrioridadConfirmacion = 5;

        private static int Prioridad(Il2Cpp.BaseMenu b)
        {
            if (b.TryCast<Il2Cpp.ConfirmationMenu>() != null) return PrioridadConfirmacion;
            Il2Cpp.PlayerProfileMenu pp = b.TryCast<Il2Cpp.PlayerProfileMenu>();
            if (pp != null) return EligiendoPerfil(pp) ? PrioridadPerfil : 0;
            if (b.TryCast<Il2Cpp.BaseSettingMenu>() != null) return 3;
            if (b.TryCast<Il2Cpp.SongSelect>() != null) return 3;
            if (b.TryCast<Il2Cpp.SettingsMenu>() != null) return 2;
            if (b.TryCast<Il2Cpp.MainMenu>() != null) return 1;
            return 0;
        }

        private static bool Valido(Il2Cpp.BaseMenu b)
        {
            return b != null && b.isActiveAndEnabled && !b.disableControls && Prioridad(b) > 0;
        }

        // La fila visible que hay debajo del raton, o -1. En Settings las filas
        // sin caja de fondo son separadores y no cuentan.
        private static int FilaBajo(Il2Cpp.BaseMenu b, Vector3 m)
        {
            var textos = b.textObjects;
            if (textos == null)
            {
                return -1;
            }
            var cajas = b.backgroundObjects;
            bool hayCajas = cajas != null && cajas.Length > 0;
            for (int j = 0; j < textos.Length; j++)
            {
                Il2CppTMPro.TextMeshProUGUI t = textos[j];
                if (t == null || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text))
                {
                    continue;
                }
                RectTransform rt = t.rectTransform;
                if (hayCajas)
                {
                    Image caja = j < cajas.Length ? cajas[j] : null;
                    if (caja == null)
                    {
                        continue;
                    }
                    if (caja.gameObject.activeInHierarchy)
                    {
                        rt = caja.rectTransform;
                    }
                }
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, new Vector2(m.x, m.y), Camara(t)))
                {
                    return j;
                }
            }
            return -1;
        }

        // La lista de secciones sigue a la vista mientras se esta dentro de
        // una (General, Video...), pero desactivada: el juego la vuelve a
        // activar al salir con el rojo. Asi que se sale igual (CancelPressed;
        // dos veces si habia un ajuste abierto, que el primero solo lo cierra)
        // y ya en la lista se elige la seccion como con la guitarra.
        private static bool OtraSeccion(Il2Cpp.BaseMenu actual, Vector3 m)
        {
            Il2Cpp.BaseSettingMenu sub = actual.TryCast<Il2Cpp.BaseSettingMenu>();
            Il2Cpp.BaseMenu secciones = sub != null ? sub.settingsMenu : null;
            if (secciones == null || secciones.isActiveAndEnabled)
            {
                return false;
            }
            int fila = FilaBajo(secciones, m);
            if (fila < 0)
            {
                return false;
            }
            int sel = (int)pSeleccion.GetValue(secciones);
            int pos = (int)pPosicion.GetValue(secciones);
            if (sel - pos + fila == sel)
            {
                return true;      // la seccion en la que ya se esta
            }
            for (int i = 0; i < 3 && !secciones.isActiveAndEnabled; i++)
            {
                mCancelar.Invoke(sub, null);
            }
            if (!secciones.isActiveAndEnabled)
            {
                MelonLogger.Warning("[Raton] no se pudo salir de la seccion para cambiar de una a otra");
                return true;
            }
            IrA(secciones, fila);
            Confirmar(secciones);
            return true;
        }

        // ------------------------------------------- interruptores y barras
        // Los controles de la derecha de cada ajuste (interruptores de si/no y
        // barras de valor). El juego no los deja tocar: con la guitarra se
        // abre el ajuste (verde), se cambia el valor (arriba/abajo, que con un
        // ajuste abierto son Increment/Decrement) y se cierra (verde). El
        // raton hace esos mismos pasos, asi que el ajuste se guarda y se
        // aplica como siempre.
        //
        // Las listas toggles[] y sliders[] de BaseSettingMenu van por fila
        // visible, igual que textObjects.
        private const int PasosBarraPorFotograma = 30;

        private static Il2Cpp.BaseMenu deslizando;
        private static Slider barraAjuste;
        private static FilaDeslizador filaMod;
        private static int metaCruzada = int.MinValue;

        private static bool Control(Il2Cpp.BaseMenu b, int j, Vector3 m, bool editando)
        {
            Il2Cpp.BaseSettingMenu s = b.TryCast<Il2Cpp.BaseSettingMenu>();
            if (s == null)
            {
                return false;
            }
            // Se busca en TODOS los controles el que tiene el raton encima, sin
            // dar por hecho que su posicion en la lista es la de la fila.
            int iInt = Bajo(s.toggles, m);
            int iBarra = iInt >= 0 ? -1 : Bajo(s.sliders, m);
            // Las barras de las filas del mod no estan en sliders[].
            FilaDeslizador mod = null;
            if (iInt < 0 && iBarra < 0)
            {
                for (int i = 0; i < FilaDeslizador.Todas.Count; i++)
                {
                    if (Encima(FilaDeslizador.Todas[i].Barra, m))
                    {
                        mod = FilaDeslizador.Todas[i];
                        break;
                    }
                }
                if (mod == null)
                {
                    return false;
                }
            }
            Slider interruptor = iInt >= 0 ? s.toggles[iInt] : null;
            Slider barra = iBarra >= 0 ? s.sliders[iBarra] : (mod != null ? mod.Barra : null);
            bool enInterruptor = interruptor != null;
            if (j < 0)
            {
                // El control puede quedar fuera de la caja de su fila: la
                // fila es la que contiene el centro del control.
                RectTransform rc = (interruptor ?? barra).GetComponent<RectTransform>();
                Vector2 centro = RectTransformUtility.WorldToScreenPoint(CamaraDe(rc),
                    rc.TransformPoint(rc.rect.center));
                j = FilaBajo(b, new Vector3(centro.x, centro.y, 0f));
                if (j < 0)
                {
                    j = enInterruptor ? iInt : iBarra;
                }
                if (j < 0)
                {
                    return true;
                }
            }
            if (!Abrir(b, j, editando))
            {
                return true;
            }
            if (enInterruptor)
            {
                // Encendido -> abajo (Decrement), apagado -> arriba.
                Paso(b, interruptor.value < 0.5f);
                Confirmar(b);      // cerrar
                return true;
            }
            deslizando = b;
            barraAjuste = barra;
            filaMod = mod;
            metaCruzada = int.MinValue;
            Deslizar(m);
            return true;
        }

        // Deja la fila j seleccionada y su ajuste abierto.
        private static bool Abrir(Il2Cpp.BaseMenu b, int j, bool editando)
        {
            if (j != (int)pPosicion.GetValue(b))
            {
                if (editando)
                {
                    Confirmar(b);
                    if (pCambiando.GetValue(b) as string != null)
                    {
                        return false;
                    }
                    editando = false;
                }
                if (!IrA(b, j))
                {
                    return false;
                }
            }
            if (!editando)
            {
                Confirmar(b);
            }
            return pCambiando.GetValue(b) as string != null;
        }

        // Lleva el valor hacia el que marca el raton sobre la barra, con
        // arriba/abajo y mirando la propia barra (el menu la repinta en cada
        // paso). Unos pasos por fotograma: en rangos largos se ve avanzar.
        private static void Deslizar(Vector3 m)
        {
            Slider sl = barraAjuste;
            if (sl == null)
            {
                return;
            }
            RectTransform rt = sl.GetComponent<RectTransform>();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt,
                    new Vector2(m.x, m.y), CamaraDe(rt), out Vector2 local))
            {
                return;
            }
            Rect r = rt.rect;
            float t = r.width > 0f ? Mathf.Clamp01((local.x - r.xMin) / r.width) : 0f;
            if (sl.direction == Slider.Direction.RightToLeft)
            {
                t = 1f - t;
            }
            if (filaMod != null)
            {
                filaMod.PonerFraccion(t);
                return;
            }
            int meta = Mathf.RoundToInt(sl.minValue + t * (sl.maxValue - sl.minValue));
            if (meta == metaCruzada)
            {
                return;
            }
            for (int i = 0; i < PasosBarraPorFotograma; i++)
            {
                float v = sl.value;
                if (Mathf.Abs(v - meta) < 0.5f)
                {
                    break;
                }
                bool subir = meta > v;
                Paso(deslizando, subir);      // arriba = Increment
                float nuevo = sl.value;
                if (Mathf.Abs(nuevo - v) < 0.001f)
                {
                    break;      // tope del ajuste
                }
                // Pasos de mas de 1 (de 5 en 5...): si se paso, se queda.
                if (subir ? nuevo > meta : nuevo < meta)
                {
                    metaCruzada = meta;
                    break;
                }
            }
        }

        private static void SoltarDeslizador()
        {
            Il2Cpp.BaseMenu b = deslizando;
            deslizando = null;
            barraAjuste = null;
            filaMod = null;
            try
            {
                if (b != null && pCambiando.GetValue(b) as string != null)
                {
                    Confirmar(b);      // cerrar, como el verde
                }
            }
            catch (Exception)
            {
            }
        }


        private static int Bajo(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Slider> a, Vector3 m)
        {
            if (a == null)
            {
                return -1;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (Encima(a[i], m))
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool Encima(Slider s, Vector3 m)
        {
            if (s == null || !s.gameObject.activeInHierarchy)
            {
                return false;
            }
            RectTransform rt = s.GetComponent<RectTransform>();
            return rt != null && RectTransformUtility.RectangleContainsScreenPoint(
                rt, new Vector2(m.x, m.y), CamaraDe(rt));
        }

        // ------------------------------------------------ casilla del jugador 1
        // Las cuatro casillas de abajo ("Press Start") son PlayerProfileMenu.
        // Cada una espera a que un control pulse Start y entonces se lo queda
        // (Assign). El raton no es un control para el juego, asi que el clic
        // une el TECLADO, igual que Enter. Solo la primera casilla: hay un
        // solo raton. Si ya hay alguien unido ahi (con guitarra o con
        // teclado), el clic abre sus opciones de perfil, como Start; y la
        // eleccion de perfil se maneja con el raton como cualquier menu.
        private static Il2Cpp.PlayerProfileMenu[] casillas;
        private static float buscarCasillasEn;

        private static Il2Cpp.PlayerProfileMenu Casilla1()
        {
            if (casillas == null || Time.unscaledTime >= buscarCasillasEn)
            {
                casillas = UnityEngine.Object.FindObjectsOfType<Il2Cpp.PlayerProfileMenu>();
                buscarCasillasEn = Time.unscaledTime + 0.5f;
            }
            for (int i = 0; i < casillas.Length; i++)
            {
                if (casillas[i] != null && casillas[i].playerIndex == 0)
                {
                    return casillas[i];
                }
            }
            return null;
        }

        private static bool EligiendoPerfil(Il2Cpp.PlayerProfileMenu p)
        {
            if (p.playerIndex != 0 || pJugadorMenu == null || pEditandoPerfil == null)
            {
                return false;
            }
            object j = pJugadorMenu.GetValue(p);
            return j != null && (bool)pEditandoPerfil.GetValue(j);
        }

        private static bool Casilla(Vector3 m)
        {
            if (mAsignar == null || pJugadorMenu == null)
            {
                return false;
            }
            Il2Cpp.PlayerProfileMenu p = Casilla1();
            if (p == null || !p.gameObject.activeInHierarchy)
            {
                return false;
            }
            Image fondo = p.profileBackground;
            if (fondo == null || !fondo.gameObject.activeInHierarchy)
            {
                return false;
            }
            RectTransform rt = fondo.rectTransform;
            if (!RectTransformUtility.RectangleContainsScreenPoint(rt, new Vector2(m.x, m.y), CamaraDe(rt)))
            {
                return false;
            }
            object j = pJugadorMenu.GetValue(p);
            if (j == null)
            {
                Il2CppRewired.Player teclado = TecladoLibre();
                if (teclado == null)
                {
                    MelonLogger.Msg("[Raton] casilla 1: no hay un teclado libre para unirse");
                    return true;
                }
                MelonLogger.Msg("[Raton] casilla 1: se une el teclado (jugador de Rewired "
                    + teclado.id.ToString() + ")");
                mAsignar.Invoke(p, new object[] { teclado });
                casillas = null;
                return true;
            }
            if (EligiendoPerfil(p))
            {
                // Con las opciones del perfil abiertas, un clic en el
                // recuadro del nombre las cierra.
                if (ConPerfil(p))
                {
                    CerrarPerfil(p);
                    return true;
                }
                return false;      // eligiendo perfil: lo maneja el resto
            }
            if (mEditarPerfil != null && pPerfil != null && pPerfil.GetValue(j) != null)
            {
                mEditarPerfil.Invoke(p, null);
                return true;
            }
            return false;
        }

        // Sale de las opciones del perfil con el rojo, las veces que haga
        // falta: el primero cierra un ajuste abierto o vuelve de un sub-menu,
        // y desde arriba se sale. Lo mismo que con la guitarra.
        private static void CerrarPerfil(Il2Cpp.PlayerProfileMenu p)
        {
            object j = pJugadorMenu.GetValue(p);
            for (int i = 0; i < 5 && j != null && (bool)pEditandoPerfil.GetValue(j); i++)
            {
                mCancelar.Invoke(p, null);
            }
        }

        // El panel de la casilla: el recuadro del nombre y las filas de
        // opciones, con un margen. No se usa el RectTransform del menu porque
        // no se sabe que tamano tiene.
        private static bool DentroDelPanel(Il2Cpp.PlayerProfileMenu p, Vector3 m)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            Vector3[] esquinas = new Vector3[4];
            void Sumar(RectTransform rt)
            {
                if (rt == null || !rt.gameObject.activeInHierarchy)
                {
                    return;
                }
                rt.GetWorldCorners(esquinas);
                Camera cam = CamaraDe(rt);
                for (int k = 0; k < 4; k++)
                {
                    Vector2 s = RectTransformUtility.WorldToScreenPoint(cam, esquinas[k]);
                    x0 = Math.Min(x0, s.x); y0 = Math.Min(y0, s.y);
                    x1 = Math.Max(x1, s.x); y1 = Math.Max(y1, s.y);
                }
            }
            if (p.profileBackground != null)
            {
                Sumar(p.profileBackground.rectTransform);
            }
            var textos = p.textObjects;
            if (textos != null)
            {
                for (int k = 0; k < textos.Length; k++)
                {
                    if (textos[k] != null) Sumar(textos[k].rectTransform);
                }
            }
            if (x0 > x1)
            {
                return true;      // sin medidas: mejor no cerrar
            }
            const float margen = 12f;
            return m.x >= x0 - margen && m.x <= x1 + margen && m.y >= y0 - margen && m.y <= y1 + margen;
        }

        // El jugador de Rewired que tiene el teclado y aun no esta unido.
        private static Il2CppRewired.Player TecladoLibre()
        {
            Il2CppRewired.ReInput.PlayerHelper js = Il2CppRewired.ReInput.players;
            if (js == null)
            {
                return null;
            }
            for (int i = 0; i < js.playerCount; i++)
            {
                Il2CppRewired.Player p = js.GetPlayer(i);
                if (p != null && !p.isPlaying && p.controllers != null && p.controllers.hasKeyboard)
                {
                    return p;
                }
            }
            return null;
        }

        // ------------------------------------------------------------ creditos
        // Credits (Settings) es una escena aparte que solo mira el rojo para
        // salir. Cualquier clic hace lo mismo.
        private static bool Creditos()
        {
            if (mSalirCreditos == null)
            {
                return false;
            }
            Il2Cpp.CreditsScroll c = UnityEngine.Object.FindObjectOfType<Il2Cpp.CreditsScroll>();
            if (c == null || !c.isActiveAndEnabled)
            {
                return false;
            }
            mSalirCreditos.Invoke(c, null);
            return true;
        }

        // ------------------------------------------------- barra de scroll
        // La barra de las pantallas de ajustes no se puede arrastrar: el menu
        // la coloca segun la seleccion. Asi que arrastrarla MUEVE LA SELECCION
        // a la opcion que corresponde a esa altura (arriba la primera, abajo
        // la ultima), paso a paso y con las mismas funciones que la guitarra,
        // y el menu hace su scroll solo. Unos pocos pasos por fotograma para
        // que no suene una rafaga de golpe.
        private const int PasosPorFotograma = 4;

        private static Il2Cpp.BaseMenu arrastre;
        private static RectTransform barraArrastre;
        private static int destinoCruzado = -1;

        private static bool EmpezarArrastre(Il2Cpp.BaseMenu b, Vector3 m)
        {
            Scrollbar sb = pBarra != null ? pBarra.GetValue(b) as Scrollbar : null;
            RectTransform rt = sb != null && sb.gameObject.activeInHierarchy
                ? sb.GetComponent<RectTransform>() : null;
            // La lista de canciones lleva la suya aparte (un Slider).
            Il2Cpp.SongSelect lista = b.TryCast<Il2Cpp.SongSelect>();
            if (rt == null && lista != null)
            {
                Slider sl = lista.setlistScrollBar;
                if (sl != null && sl.gameObject.activeInHierarchy)
                {
                    rt = sl.GetComponent<RectTransform>();
                }
            }
            if (rt == null)
            {
                return false;
            }
            if (rt == null || !RectTransformUtility.RectangleContainsScreenPoint(
                    rt, new Vector2(m.x, m.y), CamaraDe(rt)))
            {
                return false;
            }
            if (pCambiando != null && pCambiando.GetValue(b) as string != null)
            {
                return true;      // con un ajuste abierto no se mueve nada
            }
            arrastre = b;
            barraArrastre = rt;
            destinoCruzado = -1;
            Arrastrar(m);
            return true;
        }

        private static void Arrastrar(Vector3 m)
        {
            Il2Cpp.BaseMenu b = arrastre;
            if (barraArrastre == null)
            {
                return;
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(barraArrastre,
                    new Vector2(m.x, m.y), CamaraDe(barraArrastre), out Vector2 local))
            {
                return;
            }
            Rect r = barraArrastre.rect;
            float t = r.height > 0f ? Mathf.Clamp01((r.yMax - local.y) / r.height) : 0f;

            // La lista de canciones: miles de entradas, asi que se salta
            // directo a la que toca (la funcion con la que el mod ya empieza
            // en una cancion al azar), sin pasar por todas.
            Il2Cpp.SongSelect canciones = b.TryCast<Il2Cpp.SongSelect>();
            if (canciones != null)
            {
                if (mIrACancion == null || pCuentaLista == null)
                {
                    return;
                }
                int n = (int)pCuentaLista.GetValue(null);
                if (n < 1)
                {
                    return;
                }
                int meta = Mathf.RoundToInt(t * (n - 1));
                if (meta != (int)pSeleccion.GetValue(b))
                {
                    mIrACancion.Invoke(b, new object[] { meta, true });
                }
                return;
            }

            var opciones = b.menuStrings;
            if (opciones == null || opciones.Length < 2)
            {
                return;
            }
            int destino = Mathf.RoundToInt(t * (opciones.Length - 1));
            // Un separador no se puede seleccionar: se salta por encima y se
            // queda al otro lado. Sin esto, el siguiente fotograma volveria a
            // intentarlo en sentido contrario y la seleccion temblaria.
            if (destino == destinoCruzado)
            {
                return;
            }
            int sel = (int)pSeleccion.GetValue(b);
            for (int i = 0; i < PasosPorFotograma && sel != destino; i++)
            {
                bool arriba = destino < sel;
                Paso(b, arriba);
                int nueva = (int)pSeleccion.GetValue(b);
                if (nueva == sel)
                {
                    break;
                }
                if (arriba ? nueva < destino : nueva > destino)
                {
                    destinoCruzado = destino;
                    break;
                }
                sel = nueva;
            }
        }

        private static Camera CamaraDe(RectTransform rt)
        {
            Canvas c = rt.GetComponentInParent<Canvas>();
            if (c == null)
            {
                return null;
            }
            c = c.rootCanvas;
            return c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
        }

        private static Camera Camara(Graphic g)
        {
            Canvas c = g.canvas;
            if (c == null)
            {
                return null;
            }
            c = c.rootCanvas;
            return c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
        }

        // Lleva la seleccion a la fila visible j paso a paso, con las mismas
        // funciones que la guitarra: asi el menu hace su scroll y actualiza su
        // ajuste en curso. Cada paso se "suelta" enseguida, o el menu lo
        // tomaria por un boton mantenido y seguiria bajando solo.
        private static bool IrA(Il2Cpp.BaseMenu b, int j)
        {
            int sel = (int)pSeleccion.GetValue(b);
            int pos = (int)pPosicion.GetValue(b);
            int destino = sel - pos + j;
            int inicio = sel;
            for (int paso = 0; paso < 64 && sel != destino; paso++)
            {
                bool abajo = destino > sel;
                Paso(b, !abajo);
                int nueva = (int)pSeleccion.GetValue(b);
                if (nueva == sel)
                {
                    break;      // no se movio: tope, o el menu no deja
                }
                // Si dio la vuelta (de la ultima a la primera), se para.
                if (abajo ? nueva < sel : nueva > sel)
                {
                    break;
                }
                sel = nueva;
            }
            // El resaltado lo recoloca el menu en su Update. Si justo despues
            // se confirma y el menu se desactiva (la lista de secciones al
            // entrar en una), ese Update no llega y se queda marcada la de
            // antes. Se repinta ya.
            if (sel != inicio && mActualizar != null)
            {
                mActualizar.Invoke(b, null);
            }
            return sel == destino;
        }

        // La casilla con un perfil ya elegido muestra las opciones del perfil
        // (instrumento, velocidad, zurdo...): una lista larga con scroll.
        private static bool ConPerfil(Il2Cpp.BaseMenu b)
        {
            if (pJugadorMenu == null || pPerfil == null)
            {
                return false;
            }
            object j = pJugadorMenu.GetValue(b);
            return j != null && pPerfil.GetValue(j) != null;
        }

        // Si el contenido del area con scroll PROPIA del menu (su campo
        // scrollRect) no cabe en lo visible. La del menu, no la primera que se
        // encuentre subiendo por la jerarquia: esa salio ser otra de la
        // pantalla en la lista de secciones de Settings.
        private static bool Desborda(Il2Cpp.BaseMenu b)
        {
            ScrollRect sr = pLista != null ? pLista.GetValue(b) as ScrollRect : null;
            if (sr == null || sr.content == null || !sr.gameObject.activeInHierarchy)
            {
                return false;
            }
            RectTransform vista = sr.viewport != null ? sr.viewport : sr.GetComponent<RectTransform>();
            return vista != null && sr.content.rect.height > vista.rect.height + 1f;
        }

        // Si el punto cae dentro del area visible de la lista. Las filas que el
        // scroll deja fuera siguen ahi, solo recortadas: sin esto, un clic en
        // el encabezado elegiria la fila escondida debajo.
        private static bool EnVista(Il2Cpp.BaseMenu b, Vector3 m)
        {
            ScrollRect sr = pLista != null ? pLista.GetValue(b) as ScrollRect : null;
            if (sr == null)
            {
                return true;
            }
            RectTransform vista = sr.viewport != null ? sr.viewport : sr.GetComponent<RectTransform>();
            return vista == null || RectTransformUtility.RectangleContainsScreenPoint(
                vista, new Vector2(m.x, m.y), CamaraDe(vista));
        }

        // El verde. La casilla del jugador (PlayerProfileMenu) hace la accion al
        // SOLTAR el boton: al pulsarlo solo marca la opcion y suena. Con un
        // boton de verdad siempre llega el soltar; con el raton se manda
        // justo despues. En los demas menus ese "soltar" esta vacio, pero se
        // deja solo para el de perfil para no cambiar lo que ya funciona.
        private static void Confirmar(Il2Cpp.BaseMenu b)
        {
            mConfirmar.Invoke(b, null);
            if (mSoltarConfirmar != null && (b.TryCast<Il2Cpp.PlayerProfileMenu>() != null
                                             || b.TryCast<Il2Cpp.SongSelect>() != null))
            {
                mSoltarConfirmar.Invoke(b, null);
            }
        }

        private static void Paso(Il2Cpp.BaseMenu b, bool arriba)
        {
            if (arriba)
            {
                mArriba.Invoke(b, null);
                if (mSoltarArriba != null) mSoltarArriba.Invoke(b, null);
            }
            else
            {
                mAbajo.Invoke(b, null);
                if (mSoltarAbajo != null) mSoltarAbajo.Invoke(b, null);
            }
        }
    }
}
