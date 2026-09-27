using UnityEngine;

/// <summary>
/// The plant component: connects the simulation (PlantSimulation), the visuals (PlantVisuals)
/// and the parameters (a PlantSettings asset).
/// - Fixed simulation time step (settings.deltaT), decoupled from render framerate.
/// - Seeded: same settings + same seed => same plant.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PlantGrowth : MonoBehaviour// inheritance makes this class a component, name must match script name
{
    [Tooltip("Asset with all growth parameters. Create via Create -> Plant -> Plant Settings.")]
    public PlantSettings settings;
    public int seed = 42;
    [Tooltip("Growth is biased toward this point. If empty, biased straight up.")]
    public Transform lightSource;//scene object -> can't live in the settings asset
    [Tooltip("Sim seconds per real second (0 = paused).")]
    [Range(0f, 20f)] public float simSpeed = 1f;//speed of overall simulation relative to real time
    [Tooltip("Tick to restart growth from scratch with the current seed.")]
    public bool regrow;//immediately set back to false when clicked -> acts like toggle for reset
    const int MaxStepsPerFrame = 200;

    PlantSimulation sim;
    PlantVisuals visuals;
    float accumulator;       // unconsumed real time * simSpeed
    bool meshDirty;          // flag, that indicates if the mesh is out of date and needs rebuilding
    int settingsVersion = -1;// last seen PlantSettings.Version, to notice slider changes in the asset

    /// <summary>The current simulation (read-only access for other scripts, e.g. logging).</summary>
    public PlantSimulation Simulation => sim;

    void Awake()
    {
        visuals = new PlantVisuals(transform, GetComponent<MeshFilter>());
        ResetPlant();
    }

    [ContextMenu("Regrow")]
    public void ResetPlant()
    {
        if (visuals == null) return;//not in Play mode yet
        visuals.Clear();
        accumulator = 0f;
        meshDirty = true;
        sim = settings != null ? new PlantSimulation(settings, seed) : null;
        if (settings == null) Debug.LogWarning("PlantGrowth: no PlantSettings asset assigned.", this);
    }

    //called by the editor whenever a value on this component is changed in the inspector
    void OnValidate()
    {
        meshDirty = true;
    }

    void Update()
    {
        if (regrow) { regrow = false; ResetPlant(); }//immediately reset to false after regrow was pressed
        //settings assigned or swapped for another asset -> start over with the new parameters
        if (settings != null && (sim == null || sim.Settings != settings)) ResetPlant();
        if (sim == null) return;

        //inverseTransform transforms the light source's global position to local relative to plant origin
        sim.LightTarget = lightSource != null ? transform.InverseTransformPoint(lightSource.position) : (Vector3?)null;

        //deltaTime is the actual time since last frame -> can vary depending on actual FPS
        //simulated time, that has passed in the real world, but wasn't simulated yet
        accumulator += Time.deltaTime * simSpeed;//avoids plant growing faster on fast PC
        float dt = settings.deltaT;
        int steps = 0;
        //multiple steps are done in one frame if that frame takes too long to process on PC
        while (accumulator >= dt && steps < MaxStepsPerFrame)
        {
            if (sim.Step(dt)) meshDirty = true;
            accumulator -= dt;
            steps++;
        }
        //if the frames are so slow that many steps have to be done within (making the frame even slower), the game can freeze
        //capping maximum steps avoids this, even if step timing isn't accurate anymore
        if (steps == MaxStepsPerFrame) accumulator = 0f;

        //a slider in the settings asset was moved -> shape sliders take effect immediately, even while paused
        if (settings.Version != settingsVersion) { settingsVersion = settings.Version; meshDirty = true; }

        if (meshDirty) { visuals.RebuildMesh(sim); meshDirty = false; }//updating how everything looks like after all the steps taken
        visuals.UpdateLeaves(sim);//cheap, so done every frame: leaf sizes and positions follow sliders immediately
    }

    void OnDestroy()
    {
        visuals?.Dispose();
    }
}
