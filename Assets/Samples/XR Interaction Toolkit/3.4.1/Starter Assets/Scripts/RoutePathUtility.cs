using System.Collections.Generic;
using UnityEngine;

public static class RoutePathUtility
{
    public static List<Vector3> BuildSampledPath(Vector3 startPosition, Transform destinationPoint, List<Transform> controlPoints, int samplesPerSegment)
    {
        List<Vector3> anchors = new List<Vector3>();
        anchors.Add(startPosition);

        if (destinationPoint == null)
            return anchors;

        if (controlPoints != null)
        {
            for (int i = 0; i < controlPoints.Count; i++)
            {
                if (controlPoints[i] != null)
                    anchors.Add(controlPoints[i].position);
            }
        }

        anchors.Add(destinationPoint.position);

        if (anchors.Count < 2)
            return anchors;

        if (anchors.Count == 2)
            return anchors;

        int samples = Mathf.Max(2, samplesPerSegment);
        List<Vector3> sampled = new List<Vector3>();

        for (int i = 0; i < anchors.Count - 1; i++)
        {
            Vector3 p0 = i == 0 ? anchors[i] : anchors[i - 1];
            Vector3 p1 = anchors[i];
            Vector3 p2 = anchors[i + 1];
            Vector3 p3 = i + 2 < anchors.Count ? anchors[i + 2] : anchors[i + 1];

            for (int s = 0; s < samples; s++)
            {
                if (i > 0 && s == 0)
                    continue;

                float t = s / (float)samples;
                sampled.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        sampled.Add(anchors[anchors.Count - 1]);
        return sampled;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f *
            ((2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }
}