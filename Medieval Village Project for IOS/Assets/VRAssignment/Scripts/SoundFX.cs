using UnityEngine;

namespace VRAssignment
{
    /// <summary>
    /// Sounds of the app, generated in code (no audio files needed):
    /// only a soft WIND loop in the background and a JUMP sound.
    /// (Footsteps, clicks, boing, whoosh and landing sounds are switched off by default;
    ///  tick them in the Inspector if you ever want them back.)
    /// Sound on/off is saved (Menu → Sound).
    /// </summary>
    public class SoundFX : MonoBehaviour
    {
        public static SoundFX Instance { get; private set; }

        [Range(0f, 1f)] public float effectsVolume = 0.8f;
        [Range(0f, 1f)] public float ambientVolume = 0.35f;
        [Tooltip("Metres between footstep sounds.")]
        public float strideLength = 0.75f;

        [Header("Which sounds play")]
        public bool wind = true;
        public bool jumpSound = true;
        public bool footsteps = false;
        public bool clicks = false;
        public bool propBoing = false;
        public bool teleportWhoosh = false;
        public bool landing = false;

        AudioSource fx, ambient;
        AudioClip click, step, jump, land, boing, whoosh, ambientLoop;
        PlayerMotor motor;
        float strideAccum, airTime;
        int stepIndex;
        const int Rate = 44100;
        const string KeySound = "vr_sound";

        public bool SoundOn
        {
            get => AudioListener.volume > 0f;
            set { AudioListener.volume = value ? 1f : 0f; PlayerPrefs.SetInt(KeySound, value ? 1 : 0); }
        }

        void Awake()
        {
            Instance = this;
            motor = GetComponentInParent<PlayerMotor>();
            fx = gameObject.AddComponent<AudioSource>();
            fx.playOnAwake = false;
            fx.spatialBlend = 0f;
            ambient = gameObject.AddComponent<AudioSource>();
            ambient.playOnAwake = false;
            ambient.loop = true;
            ambient.spatialBlend = 0f;
            ambient.volume = ambientVolume;

            var rng = new System.Random(7);
            click = Make("click", 0.05f, (t, i) => Mathf.Sin(2f * Mathf.PI * 1300f * t) * Env(t, 0.05f, 0.002f));
            step = MakeNoise("step", 0.09f, rng, 0.08f, (t) => Env(t, 0.09f, 0.004f) * 0.9f);
            land = MakeNoise("land", 0.16f, rng, 0.05f, (t) => Env(t, 0.16f, 0.003f));
            jump = Make("jump", 0.2f, (t, i) => Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(260f, 620f, t / 0.2f) * t) * Env(t, 0.2f, 0.01f) * 0.6f);
            boing = Make("boing", 0.45f, (t, i) =>
            {
                float f = Mathf.Lerp(180f, 520f, Mathf.Sqrt(t / 0.45f)) * (1f + 0.04f * Mathf.Sin(2f * Mathf.PI * 18f * t));
                return Mathf.Sin(2f * Mathf.PI * f * t) * Env(t, 0.45f, 0.01f) * 0.6f;
            });
            whoosh = MakeNoise("whoosh", 0.4f, rng, 0.25f, (t) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.4f)) * 0.7f);
            ambientLoop = MakeAmbient(rng);

            AudioListener.volume = PlayerPrefs.GetInt(KeySound, 1) == 1 ? 1f : 0f;
        }

        void OnEnable()
        {
            if (motor == null) return;
            motor.Jumped += OnJump;
            motor.Landed += OnLand;
            motor.Teleported += Whoosh;
        }

        void OnDisable()
        {
            if (motor == null) return;
            motor.Jumped -= OnJump;
            motor.Landed -= OnLand;
            motor.Teleported -= Whoosh;
        }

        void Start()
        {
            ambient.clip = ambientLoop;
            if (wind) ambient.Play();
        }

        void Update()
        {
            if (motor == null) return;
            airTime = motor.IsGrounded ? 0f : airTime + Time.deltaTime;
            if (!footsteps) return;
            // Footsteps: one every stride while walking on the ground.
            if (motor.IsGrounded && motor.HorizontalSpeed > 0.4f)
            {
                strideAccum += motor.HorizontalSpeed * Time.deltaTime;
                if (strideAccum >= strideLength)
                {
                    strideAccum = 0f;
                    fx.pitch = (stepIndex++ % 2 == 0) ? 0.95f : 1.08f; // left / right foot
                    fx.PlayOneShot(step, effectsVolume * 0.7f);
                    fx.pitch = 1f;
                }
            }
            else strideAccum = strideLength * 0.6f; // first step comes quickly
        }

        // ---------- public one-shots ----------
        public void Click() { if (clicks) Play(click, 0.5f); }
        public void Boing() { if (propBoing) Play(boing, 0.8f); }
        public void Whoosh() { if (teleportWhoosh) Play(whoosh, 0.7f); }
        void OnJump() { if (jumpSound) Play(jump, 0.7f); }
        void OnLand() { if (landing && airTime > 0.25f) Play(land, 0.9f); }

        void Play(AudioClip c, float v)
        {
            if (c != null) fx.PlayOneShot(c, v * effectsVolume);
        }

        // ---------- tiny synthesiser ----------

        /// <summary>Attack/decay envelope: quick fade-in, exponential fade-out.</summary>
        static float Env(float t, float length, float attack)
        {
            float a = Mathf.Clamp01(t / attack);
            float d = Mathf.Exp(-5f * t / length);
            return a * d;
        }

        static AudioClip Make(string name, float seconds, System.Func<float, int, float> f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = f(i / (float)Rate, i);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Filtered noise (smaller 'smooth' = duller, like a footstep on dirt).</summary>
        static AudioClip MakeNoise(string name, float seconds, System.Random rng, float smooth, System.Func<float, float> env)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                y += smooth * (white - y); // one-pole low-pass
                data[i] = y * 3f * env(i / (float)Rate);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>8-second seamless loop of soft, airy wind (no low rumble, no birds).</summary>
        static AudioClip MakeAmbient(System.Random rng)
        {
            const float seconds = 8f;
            int n = Mathf.CeilToInt(seconds * Rate);
            int fade = Rate / 4;                       // 0.25 s crossfade at the loop point
            var raw = new float[n + fade];
            float lp = 0f, hp = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float t = i / (float)Rate;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.06f * (white - lp);   // soften the hiss
                hp += 0.008f * (lp - hp);     // remove the deep "rumble"
                float airy = lp - hp;
                // gentle gusts (whole number of cycles per loop)
                float gust = 0.45f + 0.35f * Mathf.Sin(2f * Mathf.PI * 2f * t / seconds) + 0.2f * Mathf.Sin(2f * Mathf.PI * 3f * t / seconds + 1f);
                raw[i] = airy * 2.5f * Mathf.Max(0.1f, gust);
            }
            // crossfade the extra tail into the start, so the loop point is seamless
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = raw[i];
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                data[i] = raw[i] * w + raw[n + i] * (1f - w);
            }
            var clip = AudioClip.Create("ambient", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
