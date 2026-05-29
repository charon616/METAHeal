using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Applies text, material, and particle color to a pose visual prefab instance by index.
/// </summary>
public static class PoseActiveVisualPresentation
{
    public static void ApplyIndexedVisual(
        GameObject visual,
        int index,
        IReadOnlyList<Material> icons,
        IReadOnlyList<Color> particleColors,
        IReadOnlyList<string> words)
    {
        if (visual == null)
            return;

        var text = visual.GetComponentInChildren<TextMeshPro>(true);
        if (text != null)
            text.text = index < words.Count ? words[index] ?? string.Empty : string.Empty;

        var psRenderer = visual.GetComponentInChildren<ParticleSystemRenderer>(true);
        if (psRenderer != null && index < icons.Count && icons[index] != null)
            psRenderer.material = icons[index];

        if (index >= particleColors.Count)
            return;

        var c = particleColors[index];
        foreach (var ps in visual.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(c);
        }
    }
}
