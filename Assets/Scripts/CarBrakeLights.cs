using System;
using UnityEngine;

public class CarBrakeLights : MonoBehaviour
{
    [Serializable]
    public class Surface
    {
        public Renderer renderer;
        [Tooltip("Zero-based index in Mesh Renderer > Materials: Element 0 = slot 0.")]
        [Min(0)] public int materialSlot;
    }

    [SerializeField] private CarDriver source;
    [SerializeField] private Material taillightOff;
    [SerializeField] private Material taillightOn;
    [Tooltip("Add each taillight material slot. Two lights sharing one slot need only one entry.")]
    [SerializeField] private Surface[] surfaces = new Surface[0];
    private bool initialized;
    private bool lightsOn;

    public bool LightsOn => initialized && lightsOn;

    public void Configure(CarDriver driver, Material off, Material on, Surface[] targets)
    {
        source = driver;
        taillightOff = off;
        taillightOn = on;
        surfaces = targets;
    }

    private void OnEnable()
    {
        if (source == null)
            source = GetComponentInParent<CarDriver>();
        initialized = source != null && taillightOff != null && taillightOn != null && surfaces.Length > 0;
        if (!initialized)
            return;
        foreach (Surface surface in surfaces)
        {
            if (surface == null || surface.renderer == null || surface.materialSlot < 0 ||
                surface.materialSlot >= surface.renderer.sharedMaterials.Length)
            {
                initialized = false;
                Debug.LogWarning("Check the renderer and material slot in Car Brake Lights.", this);
                return;
            }
        }
        Apply(false);
    }

    private void LateUpdate()
    {
        if (!initialized)
            return;
        bool braking = source != null && source.isActiveAndEnabled && source.IsBraking;
        if (braking != lightsOn)
            Apply(braking);
    }

    private void Apply(bool braking)
    {
        Material material = braking ? taillightOn : taillightOff;
        foreach (Surface surface in surfaces)
        {
            if (surface.renderer == null)
                continue;
            // Replace only the designated slot. Never edit the shared material
            // asset, and preserve body/window materials and other light slots.
            Material[] materials = surface.renderer.sharedMaterials;
            if (surface.materialSlot >= materials.Length)
                continue;
            materials[surface.materialSlot] = material;
            surface.renderer.sharedMaterials = materials;
        }
        lightsOn = braking;
    }

    private void OnDisable()
    {
        if (initialized)
            Apply(false);
        initialized = false;
    }
}
