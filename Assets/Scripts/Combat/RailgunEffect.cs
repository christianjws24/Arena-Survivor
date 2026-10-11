using System.Collections.Generic;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Efecto visual del Rail Gun del jugador en pixel art (solo visual: el daño lo resuelve PlayerWeapons).
    /// - Rayo: un tramo de 32 px repetido a lo largo (SpriteRenderer en modo Tiled) con 8 fotogramas, del impacto
    ///   (3 u de ancho, núcleo blanco y doble hélice girando) a un hilo que se apaga.
    /// - Punta redondeada en la boca para que el rayo no empiece con un corte recto.
    /// - Destello de boca de 4 fotogramas y chispas que saltan a lo largo del tramo visible.
    /// Corre en tiempo real (no le afecta el hit-stop del disparo) y se recicla en un pool interno.
    /// </summary>
    public class RailgunEffect : MonoBehaviour
    {
        private static readonly Stack<RailgunEffect> Pool = new Stack<RailgunEffect>();

        private const int SortingOrder = 7;
        private const float MuzzleFrameTime = 0.05f;
        private const float FadeStart = 0.7f;      // fracción de la duración a partir de la que el rayo se desvanece
        private const int SparkCount = 50;
        private const float SparkSpanMax = 20f;    // las chispas solo cubren lo que se ve (la cámara mide ~18 u de ancho)
        private const float SparkLifetimeMax = 0.6f;

        private SpriteRenderer _beam;
        private SpriteRenderer _cap;
        private SpriteRenderer _muzzle;
        private ParticleSystem _sparks;
        private Sprite[] _beamFrames;
        private Sprite[] _capFrames;
        private Sprite[] _muzzleFrames;

        private float _length;
        private float _duration;
        private float _elapsed;
        private int _frame = -1;

        public static bool IsAvailable => FxSprites.RailBeam != null && FxSprites.RailCap != null && FxSprites.RailMuzzle != null;

        /// <summary>Muestra el disparo desde 'origin' en 'direction'. 'sparkColor' tiñe las chispas.</summary>
        public static void Show(Vector2 origin, Vector2 direction, float length, Color sparkColor, float duration)
        {
            if (!IsAvailable) return;

            RailgunEffect fx = null;
            while (Pool.Count > 0 && fx == null)
            {
                fx = Pool.Pop();
            }

            if (fx == null)
            {
                fx = new GameObject("RailgunEffect").AddComponent<RailgunEffect>();
                fx.Build();
            }

            fx.gameObject.SetActive(true);
            fx.Setup(origin, direction, length, sparkColor, duration);
        }

        private void Build()
        {
            _beamFrames = FxSprites.RailBeam;
            _capFrames = FxSprites.RailCap;
            _muzzleFrames = FxSprites.RailMuzzle;

            _beam = CreateRenderer("Beam", SortingOrder);
            _beam.drawMode = SpriteDrawMode.Tiled;
            _beam.tileMode = SpriteTileMode.Continuous;
            _cap = CreateRenderer("Cap", SortingOrder);
            _muzzle = CreateRenderer("Muzzle", SortingOrder + 2);
            _sparks = BuildSparks();
        }

        private SpriteRenderer CreateRenderer(string name, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Chispas pixeladas (la misma tira que las explosiones) que salen despedidas a los lados del rayo.</summary>
        private ParticleSystem BuildSparks()
        {
            Texture2D sheet = FxSprites.SparksSheet;
            if (sheet == null) return null;

            GameObject go = new GameObject("Sparks");
            go.transform.SetParent(transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = SparkLifetimeMax;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, SparkLifetimeMax);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.3f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.maxParticles = 128;

            var emission = ps.emission;
            emission.enabled = false; // solo ráfagas con Emit()

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.randomDirectionAmount = 0f;

            // Velocidad en ejes del rayo: algo hacia delante y despedidas a ambos lados
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-1f, 4f);
            velocity.y = new ParticleSystem.MinMaxCurve(-5f, 5f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var color = ps.colorOverLifetime;
            color.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;

            var sheetAnim = ps.textureSheetAnimation;
            sheetAnim.enabled = true;
            sheetAnim.mode = ParticleSystemAnimationMode.Grid;
            sheetAnim.numTilesX = 3;
            sheetAnim.numTilesY = 1;
            sheetAnim.cycleCount = 1;
            sheetAnim.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 0.999f));

            ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = new Material(FxSprites.SpriteMaterial) { name = "Railgun_Sparks", mainTexture = sheet };
            psr.sortingOrder = SortingOrder + 1;
            return ps;
        }

        private void Setup(Vector2 origin, Vector2 direction, float length, Color sparkColor, float duration)
        {
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
            direction.Normalize();

            _length = length;
            _duration = Mathf.Max(0.05f, duration);
            _elapsed = 0f;
            _frame = -1;

            transform.SetPositionAndRotation(origin, Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg));
            _beam.enabled = true;
            _cap.enabled = true;
            _muzzle.enabled = true;
            SetFrame(0);
            SetAlpha(1f);
            _muzzle.sprite = _muzzleFrames[0];

            if (_sparks != null)
            {
                float span = Mathf.Min(length, SparkSpanMax);
                float width = _beamFrames[0].bounds.size.y;
                _sparks.transform.localPosition = new Vector3(span * 0.5f, 0f, 0f);
                var shape = _sparks.shape;
                shape.scale = new Vector3(span, width * 0.8f, 0f);
                var main = _sparks.main;
                main.startColor = new ParticleSystem.MinMaxGradient(sparkColor, Color.white);
                _sparks.Clear();
                _sparks.Emit(SparkCount);
            }
        }

        private void SetFrame(int index)
        {
            if (index == _frame) return;
            _frame = index;

            Sprite beam = _beamFrames[index];
            _beam.sprite = beam;
            _beam.size = new Vector2(_length, beam.bounds.size.y);
            _beam.transform.localPosition = new Vector3(_length * 0.5f, 0f, 0f);

            Sprite cap = _capFrames[Mathf.Min(index, _capFrames.Length - 1)];
            _cap.sprite = cap;
            _cap.transform.localPosition = new Vector3(-cap.bounds.size.x * 0.5f, 0f, 0f);
        }

        private void SetAlpha(float alpha)
        {
            Color c = new Color(1f, 1f, 1f, alpha);
            _beam.color = c;
            _cap.color = c;
        }

        private void Update()
        {
            // Tiempo real: el hit-stop del disparo no congela el rayo
            _elapsed += Time.unscaledDeltaTime;
            float t = _elapsed / _duration;

            if (t < 1f)
            {
                SetFrame(Mathf.Min(_beamFrames.Length - 1, (int)(t * _beamFrames.Length)));
                SetAlpha(t < FadeStart ? 1f : 1f - ((t - FadeStart) / (1f - FadeStart)));
            }
            else if (_beam.enabled)
            {
                _beam.enabled = false;
                _cap.enabled = false;
            }

            int muzzleFrame = (int)(_elapsed / MuzzleFrameTime);
            if (muzzleFrame < _muzzleFrames.Length) _muzzle.sprite = _muzzleFrames[muzzleFrame];
            else if (_muzzle.enabled) _muzzle.enabled = false;

            // Se recicla cuando también han terminado las chispas
            if (_elapsed >= Mathf.Max(_duration, SparkLifetimeMax))
            {
                gameObject.SetActive(false);
                Pool.Push(this);
            }
        }

        private void OnDestroy()
        {
            // Si la escena se descarga, evitar referencias muertas en el pool estático
            Pool.Clear();
        }
    }
}
