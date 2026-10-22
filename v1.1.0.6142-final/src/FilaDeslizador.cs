using System;
using MelonLoader;

namespace CloneHeroMod
{
    // Una fila nuestra con barra, que se mueve igual que las del juego.
    //
    // Al abrir una fila, el menu guarda su nombre en changedSetting y busca su
    // GameSetting en currentSetting; arriba y abajo hacen Increment/Decrement
    // sobre el y el amarillo, Reset. Para una fila que no conoce,
    // currentSetting queda vacio y no pasa nada. Aqui se le pone uno nuestro
    // mientras la fila esta abierta, y el juego hace el resto: pasos, topes y
    // vuelta al valor por defecto.
    //
    // Ese GameSetting vive en Settings.NoSection, la seccion de archivo "" que
    // el juego usa para valores de paso (song_speed) y que su Commit se salta:
    // nunca va a disco. El valor de verdad sigue en settings.ini, [mods].
    //
    // Un valor del .ini fuera de los pasos o del rango de la barra se respeta:
    // la barra marca el mas cercano y el numero el real, hasta que se mueva.
    public class FilaDeslizador
    {
        public readonly string Nombre;
        private readonly int min;
        private readonly int max;
        private readonly int paso;
        private readonly int porDefecto;
        private readonly Func<float> leer;          // lo guardado, en unidades de la barra
        private readonly Action<int> guardar;
        private readonly Func<float, string> formato;

        private Il2Cpp.Object2PublicBoSiInSiDoStSiStStUnique ajuste;
        private bool abierta;
        private int visto;
        private UnityEngine.UI.Slider barra;
        private Il2CppTMPro.TextMeshProUGUI numero;
        private bool avisado;

        // Al cerrar la fila (se confirma o se sale), haya cambiado o no.
        public Action AlCerrar;

        public FilaDeslizador(string nombre, int min, int max, int paso, int porDefecto,
            Func<float> leer, Action<int> guardar, Func<float, string> formato)
        {
            Nombre = nombre;
            this.min = min;
            this.max = max;
            this.paso = paso;
            this.porDefecto = porDefecto;
            this.leer = leer;
            this.guardar = guardar;
            this.formato = formato;
        }

        // En el prefix de OnEnable del menu, como las demas filas.
        public void Anadir(object menu)
        {
            UnityEngine.UI.Slider b;
            Il2CppTMPro.TextMeshProUGUI n;
            if (FilasMenu.AnadirDeslizador(menu, Nombre, out b, out n))
            {
                barra = b;
                numero = n;
            }
            abierta = false;
            Pintar();
        }

        // Cada fotograma de menu.
        public void Tick(object menu)
        {
            try
            {
                Il2Cpp.BaseSettingMenu m = menu as Il2Cpp.BaseSettingMenu;
                if (m == null)
                {
                    return;
                }
                string actual = m.field_Protected_String_0;      // changedSetting
                if (actual == null || !actual.StartsWith(Nombre, StringComparison.OrdinalIgnoreCase))
                {
                    if (abierta)
                    {
                        abierta = false;
                        if (AlCerrar != null)
                        {
                            AlCerrar();
                        }
                    }
                    return;
                }
                if (ajuste == null)
                {
                    ajuste = new Il2Cpp.Object2PublicBoSiInSiDoStSiStStUnique(
                        Il2Cpp.ObjectPublicAbstractSealedCo2Ob1CoStOb2BoStUnique
                            .field_Public_Static_ValueTypePublicSealedStStUnique_0,
                        "coolmod_" + Nombre, porDefecto, min, max, paso);
                }
                if (!abierta)
                {
                    abierta = true;
                    ajuste.prop_T_0 = Encajar(leer());
                    visto = ajuste.prop_T_0;
                }
                var puesto = m.field_Protected_Object2PublicBoSiInSiDoStSiStStUnique_0;  // currentSetting
                if (puesto == null || puesto.Pointer != ajuste.Pointer)
                {
                    m.field_Protected_Object2PublicBoSiInSiDoStSiStStUnique_0 = ajuste;
                }
                int valor = ajuste.prop_T_0;
                if (valor != visto)
                {
                    visto = valor;
                    guardar(valor);
                    Pintar();
                }
            }
            catch (Exception ex)
            {
                if (!avisado)
                {
                    avisado = true;
                    MelonLogger.Warning("[Filas] " + Nombre + ": " + ex);
                }
            }
        }

        private int Encajar(float v)
        {
            int s = min + (int)Math.Round((v - min) / paso) * paso;
            return Math.Max(min, Math.Min(max, s));
        }

        // .value a secas, como hace el juego con las suyas (SetValueWithoutNotify
        // no viene en su binario).
        private void Pintar()
        {
            try
            {
                float v = leer();
                if (barra != null)
                {
                    barra.minValue = min;
                    barra.maxValue = max;
                    barra.value = Math.Max(min, Math.Min(max, v));
                }
                if (numero != null)
                {
                    numero.text = formato(v);
                }
            }
            catch (Exception ex)
            {
                if (!avisado)
                {
                    avisado = true;
                    MelonLogger.Warning("[Filas] " + Nombre + " (pintar): " + ex);
                }
            }
        }
    }
}
