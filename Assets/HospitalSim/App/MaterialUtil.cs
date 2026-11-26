using UnityEngine;
using UnityEngine.Rendering;

namespace HospitalSim
{
    //materialy tworzone w trakcie dzialania programu
    //w buildzie shader find widzi tylko shadery ktore trafily do buildu dlatego mamy kilka zapasowych
    public static class MaterialUtil
    {
        static Material vertexColorBase;
        //wzorce podpiete w inspektorze hospitalapp pilnuja zeby shadery trafily do buildu
        public static Material LitTemplate, UnlitTemplate;

        public static Material Make(Color c, bool unlit, bool instancing, bool doubleSided = false)
        {
            var tpl = unlit ? UnlitTemplate : LitTemplate;
            if (tpl != null)
            {
                var mt = new Material(tpl);
                Tint(mt, c, doubleSided, instancing);
                return mt;
            }
            Shader sh = null;
            string[] names = unlit
                ? new[] { "Universal Render Pipeline/Unlit", "Unlit/Color" }
                : new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Standard" };
            foreach (var n in names) { sh = Shader.Find(n); if (sh != null) break; }

            Material m;
            if (sh != null) m = new Material(sh);
            else
            {
                //domyslny material potoku renderowania zawsze jest w buildzie
                var rp = GraphicsSettings.currentRenderPipeline;
                if (rp != null && rp.defaultMaterial != null) m = new Material(rp.defaultMaterial);
                else m = new Material(Shader.Find("Sprites/Default"));
            }

            Tint(m, c, doubleSided, instancing);
            return m;
        }

        static void Tint(Material m, Color c, bool doubleSided, bool instancing)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (doubleSided && m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            m.enableInstancing = instancing;
        }

        //material z kolorami wierzcholkow i przezroczystoscia do siatki w edytorze
        //sprites default zawsze jest w buildzie wiec na nim polegamy
        public static Material VertexColor()
        {
            if (vertexColorBase == null)
            {
                var sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("UI/Default");
                vertexColorBase = new Material(sh);
            }
            var m = new Material(vertexColorBase) { renderQueue = 3100 };
            return m;
        }
    }
}
