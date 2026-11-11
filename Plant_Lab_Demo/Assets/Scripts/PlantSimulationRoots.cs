using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Roots (PlantSettings.roots): branches marked with rootType, kept in their own list (Roots), so everything that
/// handles the shoot (development rules, bending, contacts, self-collision, flowers, withering, phase end) never sees
/// them. They use Branch's growth (segments, sliding along obstacles); the decisions that differ are here:
/// direction (gravity instead of light, depending on substrate; away from dense roots), side roots, thickness,
/// and the root density: growth slows down toward RootSettings.maxDensity and stops there.
/// Roots grow only in the Growing phase and stay rigid. Main roots start at the plant base.
/// </summary>
public partial class PlantSimulation
{
    /// <summary>All roots, in creation order (parents before children). Empty without PlantSettings.roots.</summary>
    public readonly List<Branch> Roots = new List<Branch>();

    /// <summary>The root type the roots were created with (changes to PlantSettings.roots take effect on regrow).</summary>
    public RootSettings RootType { get; private set; }

    /// <summary>Root length per volume, filled as root pieces form (null without roots).</summary>
    public RootDensityGrid RootDensity { get; private set; }

    //seeds for main roots: derived from the plant's seed with an offset, so they never equal a shoot branch's seed
    const int MainRootSeedOffset = 7000;

    void BeginRoots()
    {
        RootType = Settings.roots;
        RootSettings rs = RootType;
        if (rs == null) return;
        RootDensity = new RootDensityGrid(rs.densityCellSize);
        //own stream for the start directions: roots never shift any shoot decision
        var r = new System.Random(unchecked(Root.seed * 486187739 + 11));
        float azimuth = (float)(r.NextDouble() * 360.0);
        for (int i = 0; i < rs.mainRootCount && Roots.Count < rs.maxRoots; i++)
        {
            azimuth += GoldenAngleDeg;
            float elev = Mathf.Clamp(rs.startAngleDeg + Gaussian(r) * rs.startAngleNoiseDeg, 0f, 90f) * Mathf.Deg2Rad;
            float az = (azimuth + Gaussian(r) * rs.startAngleNoiseDeg) * Mathf.Deg2Rad;
            Vector3 horizontal = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
            Vector3 dir = Mathf.Cos(elev) * horizontal + Mathf.Sin(elev) * Vector3.down;
            Vector3 normal = new Vector3(-Mathf.Sin(az), 0f, Mathf.Cos(az));//horizontal, sideways: perpendicular to dir
            var root = new Branch(DeriveSeed(Root.seed, MainRootSeedOffset + i), 0, null, 0, 0f, 0f,
                                  Vector3.down * rs.startDepth, dir, normal, SimTime);
            root.rootType = rs;
            root.rootMaxLength = rs.mainRootLength;
            root.nextSpawnCheck = rs.sideRootStartFraction * rs.mainRootLength;
            Roots.Add(root);
        }
    }

    /// <summary>One growth step for all roots (Growing phase only). New side roots start growing in the next step.</summary>
    void GrowRoots(float dt)
    {
        if (RootType == null) return;
        float dL = RootType.growthSpeed * dt;
        int n = Roots.Count;
        for (int i = 0; i < n; i++)
        {
            Branch b = Roots[i];
            if (b.finishTime >= 0f || b.stopped || b.blocked) continue;
            float slow = DensitySlowdown(b.TipPosition);
            if (slow > 0f) b.Grow(dL * slow, b.rootMaxLength, this);
        }
    }

    /// <summary>
    /// Can any root still grow (not at full length, not blocked, not stopped by root density)? Then the Growing phase
    /// (Automatic) waits for it, at most until growingDuration.
    /// </summary>
    bool RootsStillGrowing()
    {
        foreach (Branch b in Roots)
            if (b.finishTime < 0f && !b.stopped && !b.blocked && DensitySlowdown(b.TipPosition) > 0f) return true;
        return false;
    }

    /// <summary>Growth speed factor at a position: 1 without roots around, 0 at maxDensity and above.</summary>
    float DensitySlowdown(Vector3 p)
    {
        float u = Mathf.Clamp01(RootDensity.Density(p) / RootType.maxDensity);
        return 1f - Mathf.Pow(u, RootType.slowdownExponent);
    }

    /// <summary>Called by a root when a piece of it is formed (committed): its length counts for the density from now on.</summary>
    public void RootPieceFormed(Vector3 from, Vector3 to)
    {
        RootDensity?.Add(0.5f * (from + to), Vector3.Distance(from, to));
    }

    void UpdateRootGeometry()
    {
        foreach (Branch b in Roots) b.UpdateGeometry(this);
    }

    void StopRoots()
    {
        foreach (Branch b in Roots) b.StopGrowth(this);
    }

    /// <summary>
    /// Next direction of a root tip: toward straight down, by gravitropism per length (in substrate, weaker for side
    /// roots) or airGravitropism (open air: hanging), plus random wobble. Same formulas as the shoot's NextDirection.
    /// </summary>
    Vector3 RootDirection(Branch b)
    {
        RootSettings rs = b.rootType;
        Vector3 tip = b.LastNode;
        bool inSoil = Soil != null && Soil.InSoil(tip);
        float k = inSoil ? rs.gravitropism * Mathf.Pow(rs.gravitropismSideFactor, b.depth) : rs.airGravitropism;
        float turnFraction = 1f - Mathf.Exp(-k * PlantSettings.SegmentLength);
        Vector3 biased = Vector3.Slerp(b.TipDirNow, Vector3.down, turnFraction);
        if (inSoil && rs.spreadStrength > 0f)
        {
            //spreading: turn away from denser roots, the more the steeper the density rises (relative to maxDensity);
            //per length like gravitropism, weakened by the substrate's resistance
            Vector3 away = -RootDensity.Gradient(tip) / rs.maxDensity;//1/m
            float strength = rs.spreadStrength * (1f - Soil.Resistance(tip)) * away.magnitude;
            if (strength > 1e-6f)
                biased = Vector3.Slerp(biased, away.normalized, 1f - Mathf.Exp(-strength * PlantSettings.SegmentLength));
        }
        float segmentStd = rs.perturbationStd * Mathf.Sqrt(PlantSettings.SegmentLength);
        System.Random r = b.dirRng;
        Vector3 noise = new Vector3(Gaussian(r), Gaussian(r), Gaussian(r)) * segmentStd;
        Vector3 result = biased + noise;
        return result.sqrMagnitude > 1e-6f ? result.normalized : b.TipDirNow;
    }

    /// <summary>Called by a root when its tip passes a spawn check position: maybe a side root there.</summary>
    void RootSpawnCheck(Branch root, float arcPos)
    {
        RootSettings rs = root.rootType;
        System.Random r = root.spawnRng;
        //always the same draws per check, whatever the outcome
        double uSpawn = r.NextDouble();
        float angleNoise = Gaussian(r);
        if (uSpawn >= rs.sideRootProbability || root.depth >= rs.sideRootLevels || Roots.Count >= rs.maxRoots) return;
        if (DensitySlowdown(root.PointAt(root.SegmentCount, arcPos - root.committedLength)) <= 0f) return;//no room there
        float length = root.rootMaxLength * rs.sideRootLengthFraction;
        if (length < PlantSettings.SegmentLength) return;

        root.phyllotaxisAngle += GoldenAngleDeg + angleNoise * 20f;
        Vector3 outward = root.OutwardAt(root.phyllotaxisAngle);
        int seg = root.SegmentCount;
        float offset = arcPos - root.committedLength;
        float a = rs.sideRootAngleDeg * Mathf.Deg2Rad;
        Vector3 dir = Mathf.Sin(a) * outward + Mathf.Cos(a) * root.tipDir;//rest shape (roots don't bend, so = current)
        Vector3 normal = Vector3.Cross(root.tipDir, outward).normalized;
        var child = new Branch(DeriveSeed(root.seed, root.children.Count), root.depth + 1, root, seg, offset, arcPos,
                               root.PointAt(seg, offset), dir, normal, SimTime);
        child.rootType = rs;
        child.rootMaxLength = length;
        child.nextSpawnCheck = rs.sideRootStartFraction * length;
        root.children.Add(child);
        Roots.Add(child);
    }

    /// <summary>
    /// Current radius of a root point: from baseRadius (x sideRootRadiusFactor per level) at the base down to
    /// tipRadiusFraction of it at the end of its full length, thickening over thickenTime; never thicker than the
    /// parent where it's attached.
    /// </summary>
    float RootRadius(Branch b, float arcPos, float age)
    {
        RootSettings rs = b.rootType;
        float u = Mathf.Clamp01(arcPos / Mathf.Max(1e-6f, b.rootMaxLength));
        float target = rs.baseRadius * Mathf.Pow(rs.sideRootRadiusFactor, b.depth) * Mathf.Lerp(1f, rs.tipRadiusFraction, u);
        if (b.parent != null)
        {
            float pu = Mathf.Clamp01(b.attachArc / Mathf.Max(1e-6f, b.parent.rootMaxLength));
            float parentTarget = rs.baseRadius * Mathf.Pow(rs.sideRootRadiusFactor, b.parent.depth) * Mathf.Lerp(1f, rs.tipRadiusFraction, pu);
            target = Mathf.Min(target, parentTarget);
        }
        return target * (1f - Mathf.Exp(-age / rs.thickenTime));
    }
}
