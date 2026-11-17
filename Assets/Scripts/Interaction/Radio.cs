using BodgeSquad.Art;
using BodgeSquad.Job;
using UnityEngine;

namespace BodgeSquad.Interaction
{
    // A cheap radio on a crate in the corner. Switch it on and the inspector drops everything to
    // storm over and switch it off: the classic distraction. Built the same on every machine;
    // whether it's playing lives on the inspector (host).
    public class Radio : MonoBehaviour, IPressable
    {
        Inspector _inspector;
        Transform _set;
        Renderer _lamp;
        AudioSource _audio;
        Material _lampOff, _lampOn;
        bool _wasOn;

        public string PressPrompt
        {
            get
            {
                var job = JobManager.Instance;
                if (job == null || !job.IsSpawned || job.Phase != JobPhase.Renovation || _inspector == null) return null;
                return _inspector.RadioOn ? "turn the radio off" : "turn the radio on (the inspector hates it)";
            }
        }

        public void Press(int playerIndex)
        {
            if (_inspector != null && _inspector.IsSpawned) _inspector.ToggleRadioRpc(playerIndex);
        }

        public static Radio Build(Vector3 floorSpot, Inspector inspector)
        {
            var src = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
            Material M(Color c, float smooth = 0.2f)
            {
                var m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                return m;
            }

            var root = new GameObject("Radio");
            root.transform.position = floorSpot;
            root.transform.rotation = Quaternion.Euler(0, 40f, 0); // angled out of the corner
            var r = root.AddComponent<Radio>();
            r._inspector = inspector;

            Transform Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = name;
                Destroy(b.GetComponent<Collider>());
                b.transform.SetParent(parent, false);
                b.transform.localPosition = pos;
                b.transform.localScale = size;
                b.GetComponent<Renderer>().sharedMaterial = mat;
                return b.transform;
            }

            Box("Crate", root.transform, new Vector3(0, 0.25f, 0), new Vector3(0.55f, 0.5f, 0.45f), M(Palette.Wood, 0.05f));
            var set = new GameObject("Set").transform;
            set.SetParent(root.transform, false);
            set.localPosition = new Vector3(0, 0.5f, 0);
            r._set = set;
            Box("Case", set, new Vector3(0, 0.14f, 0), new Vector3(0.46f, 0.28f, 0.18f), M(Palette.Terracotta, 0.35f));
            Box("Grille", set, new Vector3(-0.09f, 0.14f, -0.092f), new Vector3(0.2f, 0.2f, 0.01f), M(Palette.UiInk, 0.1f));
            Box("Dial", set, new Vector3(0.12f, 0.17f, -0.092f), new Vector3(0.14f, 0.07f, 0.01f), M(Palette.Cream, 0.4f));
            Box("Handle", set, new Vector3(0, 0.31f, 0), new Vector3(0.34f, 0.03f, 0.05f), M(Palette.UiInk, 0.3f));
            var ant = Box("Antenna", set, new Vector3(0.17f, 0.48f, 0.04f), new Vector3(0.015f, 0.42f, 0.015f), M(new Color(0.75f, 0.75f, 0.78f), 0.8f));
            ant.localRotation = Quaternion.Euler(0, 0, -18f);
            r._lampOff = M(new Color(0.25f, 0.18f, 0.12f));
            r._lampOn = M(Palette.LampLight);
            r._lampOn.EnableKeyword("_EMISSION");
            r._lampOn.SetColor("_EmissionColor", Palette.LampLight * 2.5f);
            r._lamp = Box("Lamp", set, new Vector3(0.12f, 0.07f, -0.092f), new Vector3(0.04f, 0.04f, 0.012f), r._lampOff).GetComponent<Renderer>();

            // one collider over crate + radio: what the crosshair hits
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.4f, 0);
            col.size = new Vector3(0.55f, 0.8f, 0.45f);

            r._audio = root.AddComponent<AudioSource>();
            r._audio.clip = Jingle();
            r._audio.loop = true;
            r._audio.playOnAwake = false;
            r._audio.spatialBlend = 0.85f;
            r._audio.minDistance = 2f;
            r._audio.maxDistance = 25f;
            r._audio.volume = 0.35f;
            return r;
        }

        void Update()
        {
            bool on = _inspector != null && _inspector.RadioOn;
            if (on != _wasOn)
            {
                _wasOn = on;
                _lamp.sharedMaterial = on ? _lampOn : _lampOff;
                if (on) _audio.Play(); else _audio.Stop();
            }
            // it bops along while it plays
            float beat = on ? Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI * 2.2f)) : 0f;
            _set.localScale = new Vector3(1f + beat * 0.05f, 1f - beat * 0.04f, 1f + beat * 0.05f);
            _set.localRotation = Quaternion.Euler(0, 0, on ? Mathf.Sin(Time.time * Mathf.PI * 1.1f) * 4f : 0f);
        }

        // A cheesy little 4-bar square-wave loop, made on the spot (no audio files needed)
        static AudioClip Jingle()
        {
            const int rate = 22050;
            const float bpm = 132f;
            float beat = 60f / bpm;
            // semitones from A4, -99 = rest; eighth notes
            int[] melody = { 3, 7, 10, 7, 12, 10, 7, -99, 5, 8, 12, 8, 15, 12, 8, -99, 3, 7, 10, 7, 12, 10, 7, 5, 3, -99, 3, -99, 10, 7, 3, -99 };
            int[] bass = { -21, -21, -16, -16, -19, -19, -14, -14 };
            float eighth = beat / 2f;
            int total = Mathf.RoundToInt(melody.Length * eighth * rate);
            var data = new float[total];
            for (int i = 0; i < total; i++)
            {
                float t = i / (float)rate;
                int step = Mathf.Min(melody.Length - 1, (int)(t / eighth));
                float inStep = t - step * eighth;
                float s = 0f;
                if (melody[step] != -99)
                {
                    float f = 440f * Mathf.Pow(2f, melody[step] / 12f);
                    float env = Mathf.Exp(-inStep * 9f);
                    s += (Mathf.Repeat(t * f, 1f) < 0.5f ? 0.22f : -0.22f) * env;
                }
                int bstep = (int)(t / (beat * 2f)) % bass.Length;
                float bf = 440f * Mathf.Pow(2f, bass[bstep] / 12f);
                float bt = t - (int)(t / beat) * beat;
                s += Mathf.Sin(2f * Mathf.PI * bf * t) * 0.28f * Mathf.Exp(-bt * 4f);
                // a tick on every beat, like a cheap drum machine
                float kt = t - (int)(t / beat) * beat;
                s += (Mathf.PerlinNoise(t * 4000f, 0f) - 0.5f) * 0.25f * Mathf.Exp(-kt * 60f);
                data[i] = Mathf.Clamp(s, -1f, 1f);
            }
            var clip = AudioClip.Create("RadioJingle", total, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
