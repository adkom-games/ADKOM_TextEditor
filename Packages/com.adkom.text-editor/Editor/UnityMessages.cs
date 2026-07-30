#if UNITY_EDITOR
namespace ADKOM.TextEditor
{
    /// <summary>The Unity magic-method (message) catalog for the generator:
    /// name + full signature, grouped for the menu. Stubs insert through the
    /// snippet machinery ($END$ inside the body).</summary>
    internal static class UnityMessages
    {
        internal struct Msg
        {
            public string Group;     // menu subgroup
            public string Name;      // method name (for the already-declared check)
            public string Signature; // e.g. "void OnTriggerEnter(Collider other)"
        }

        public static string Stub(Msg m) =>
            "private " + m.Signature + "\n{\n    $END$\n}";

        public static readonly Msg[] All =
        {
            new Msg { Group = "Lifecycle", Name = "Awake",       Signature = "void Awake()" },
            new Msg { Group = "Lifecycle", Name = "OnEnable",    Signature = "void OnEnable()" },
            new Msg { Group = "Lifecycle", Name = "Start",       Signature = "void Start()" },
            new Msg { Group = "Lifecycle", Name = "Update",      Signature = "void Update()" },
            new Msg { Group = "Lifecycle", Name = "FixedUpdate", Signature = "void FixedUpdate()" },
            new Msg { Group = "Lifecycle", Name = "LateUpdate",  Signature = "void LateUpdate()" },
            new Msg { Group = "Lifecycle", Name = "OnDisable",   Signature = "void OnDisable()" },
            new Msg { Group = "Lifecycle", Name = "OnDestroy",   Signature = "void OnDestroy()" },

            new Msg { Group = "Physics", Name = "OnCollisionEnter", Signature = "void OnCollisionEnter(Collision collision)" },
            new Msg { Group = "Physics", Name = "OnCollisionStay",  Signature = "void OnCollisionStay(Collision collision)" },
            new Msg { Group = "Physics", Name = "OnCollisionExit",  Signature = "void OnCollisionExit(Collision collision)" },
            new Msg { Group = "Physics", Name = "OnTriggerEnter",   Signature = "void OnTriggerEnter(Collider other)" },
            new Msg { Group = "Physics", Name = "OnTriggerStay",    Signature = "void OnTriggerStay(Collider other)" },
            new Msg { Group = "Physics", Name = "OnTriggerExit",    Signature = "void OnTriggerExit(Collider other)" },

            new Msg { Group = "Physics 2D", Name = "OnCollisionEnter2D", Signature = "void OnCollisionEnter2D(Collision2D collision)" },
            new Msg { Group = "Physics 2D", Name = "OnCollisionStay2D",  Signature = "void OnCollisionStay2D(Collision2D collision)" },
            new Msg { Group = "Physics 2D", Name = "OnCollisionExit2D",  Signature = "void OnCollisionExit2D(Collision2D collision)" },
            new Msg { Group = "Physics 2D", Name = "OnTriggerEnter2D",   Signature = "void OnTriggerEnter2D(Collider2D other)" },
            new Msg { Group = "Physics 2D", Name = "OnTriggerStay2D",    Signature = "void OnTriggerStay2D(Collider2D other)" },
            new Msg { Group = "Physics 2D", Name = "OnTriggerExit2D",    Signature = "void OnTriggerExit2D(Collider2D other)" },

            new Msg { Group = "Mouse", Name = "OnMouseDown",  Signature = "void OnMouseDown()" },
            new Msg { Group = "Mouse", Name = "OnMouseUp",    Signature = "void OnMouseUp()" },
            new Msg { Group = "Mouse", Name = "OnMouseEnter", Signature = "void OnMouseEnter()" },
            new Msg { Group = "Mouse", Name = "OnMouseExit",  Signature = "void OnMouseExit()" },
            new Msg { Group = "Mouse", Name = "OnMouseOver",  Signature = "void OnMouseOver()" },
            new Msg { Group = "Mouse", Name = "OnMouseDrag",  Signature = "void OnMouseDrag()" },

            new Msg { Group = "Application", Name = "OnApplicationPause", Signature = "void OnApplicationPause(bool pause)" },
            new Msg { Group = "Application", Name = "OnApplicationFocus", Signature = "void OnApplicationFocus(bool focus)" },
            new Msg { Group = "Application", Name = "OnApplicationQuit",  Signature = "void OnApplicationQuit()" },

            new Msg { Group = "Rendering", Name = "OnBecameVisible",       Signature = "void OnBecameVisible()" },
            new Msg { Group = "Rendering", Name = "OnBecameInvisible",     Signature = "void OnBecameInvisible()" },
            new Msg { Group = "Rendering", Name = "OnGUI",                 Signature = "void OnGUI()" },
            new Msg { Group = "Rendering", Name = "OnDrawGizmos",          Signature = "void OnDrawGizmos()" },
            new Msg { Group = "Rendering", Name = "OnDrawGizmosSelected",  Signature = "void OnDrawGizmosSelected()" },

            new Msg { Group = "Editor", Name = "OnValidate", Signature = "void OnValidate()" },
            new Msg { Group = "Editor", Name = "Reset",      Signature = "void Reset()" },
        };
    }
}
#endif
