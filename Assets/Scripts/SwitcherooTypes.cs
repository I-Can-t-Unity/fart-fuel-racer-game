using UnityEngine;
using UnityEngine.Rendering;

public enum CarColor { Red, Blue }

/// <summary>Shared colours, names and materials for Switcheroo.</summary>
public static class Switcheroo
{
    public static Color ToColor(CarColor c) =>
        c == CarColor.Red ? new Color(0.95f, 0.18f, 0.2f) : new Color(0.15f, 0.45f, 1f);

    public static string Name(CarColor c) => c == CarColor.Red ? "RED" : "BLUE";

    public static CarColor Other(CarColor c) => c == CarColor.Red ? CarColor.Blue : CarColor.Red;

    /// Solid colour with a soft glow (kept low so the project's bloom doesn't blow out).
    public static Material Glow(Color c, float emission = 0.6f)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (!sh) sh = Shader.Find("Standard");
        var m = new Material(sh) { name = "Switcheroo_Glow" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        return m;
    }

    /// See-through unlit colour (poof clouds, shield bubble).
    public static Material Transparent(Color c)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (!sh) sh = Shader.Find("Unlit/Color");
        var m = new Material(sh) { name = "Switcheroo_Transparent", color = c };
        if (m.HasProperty("_Surface"))
        {
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0); m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        return m;
    }
}
