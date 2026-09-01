using UnityEngine;

namespace AdkomDemo
{
    /// <summary>
    /// Demo scene behaviour for the ADKOM Text Editor. Press Play: the cube
    /// orbits, pulses, and cycles color while these values change live.
    ///
    /// What to try in ATE while it runs:
    ///   1. Open this file (double-click it in the Project window).
    ///   2. Put the caret on "OrbitDemo" and right-click -> Inspect Symbol...
    ///   3. Watch the statics below update every frame in play mode.
    ///   4. Edit SpeedMultiplier in the inspector and watch the cube react.
    ///   5. Press Run on ReverseDirection() to flip the orbit.
    ///
    /// Nothing here is required by the editor itself - delete the Demo folder
    /// whenever you like.
    /// </summary>
    public class OrbitDemo : MonoBehaviour
    {
        // Statics: Unity's own Inspector never shows these. ATE's reflection
        // inspector polls them live, play mode included.
        public static int FramesSimulated;
        public static float TotalDegreesTraveled;
        public static float SpeedMultiplier = 1f;   // editable live in ATE
        public static string Phase = "warming up";

        // Instance state: pick OrbitDemoCube in ATE's instance dropdown.
        public float orbitSpeed = 90f;   // degrees per second
        public float radius = 3f;
        public float pulseSpeed = 2f;

        float _angle;
        float _hue;
        Renderer _renderer;
        static int _direction = 1;

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
        }

        void Update()
        {
            FramesSimulated++;

            float step = orbitSpeed * SpeedMultiplier * _direction * Time.deltaTime;
            _angle = (_angle + step + 360f) % 360f;
            TotalDegreesTraveled += Mathf.Abs(step);
            Phase = _angle < 180f ? "first half" : "second half";

            float rad = _angle * Mathf.Deg2Rad;
            transform.position = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
            transform.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(Time.time * pulseSpeed));

            _hue = (_hue + Time.deltaTime * 0.1f) % 1f;
            if (_renderer != null) _renderer.material.color = Color.HSVToRGB(_hue, 0.6f, 1f);
        }

        // Parameterless statics get a Run button in ATE's inspector.
        public static void ReverseDirection() => _direction = -_direction;

        public static void ResetCounters()
        {
            FramesSimulated = 0;
            TotalDegreesTraveled = 0f;
        }
    }
}
