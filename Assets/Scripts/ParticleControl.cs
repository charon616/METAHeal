using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Picks a random ParticleSystem from a registered list and plays it (with optional burst count).
/// Wire <see cref="ParticlePlay"/> to UnityEvents from reaction or count milestones.
/// </summary>
public class ParticleControl : MonoBehaviour
{
    [Header("Sources")]
    [Tooltip("Register multiple scene ParticleSystems; ParticlePlay picks one at random each call.")]
    [SerializeField] List<ParticleSystem> particleSystems = new List<ParticleSystem>();

    [Header("Optional")]
    [Tooltip("How many random pick+Play cycles per ParticlePlay call (for burst effects).")]
    [Min(1)]
    [SerializeField] int playsPerCall = 1;

    /// <summary>Picks a random index and calls <see cref="ParticleSystem.Play(bool)"/> (includes child emitters).</summary>
    public void ParticlePlay()
    {
        if (particleSystems == null || particleSystems.Count == 0)
            return;

        int picks = Mathf.Max(1, playsPerCall);
        for (var p = 0; p < picks; p++)
        {
            ParticleSystem ps = PickRandomNonNull();
            if (ps == null)
                return;

            ps.Play(true);
        }
    }

    ParticleSystem PickRandomNonNull()
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            int i = Random.Range(0, particleSystems.Count);
            if (particleSystems[i] != null)
                return particleSystems[i];
        }

        foreach (ParticleSystem ps in particleSystems)
        {
            if (ps != null)
                return ps;
        }

        return null;
    }
}
