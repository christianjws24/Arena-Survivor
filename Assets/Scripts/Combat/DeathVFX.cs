using System.Collections;
using ArenaSurvivor.Pool;
using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Efecto de partículas reutilizable para la destrucción de entidades (enemigos o jugador).
    /// Optimizado mediante Object Pooling: no usa Instantiate ni Destroy.
    /// Modifica dinámicamente el color y tamaño de las partículas según quién muera.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class DeathVFX : MonoBehaviour, IPoolable
    {
        [SerializeField] private ParticleSystem particleSys;
        private Coroutine _returnCoroutine;

        // Pixel art: chispas de 3 fotogramas (cruz -> rombo -> punto) y un destello central de 4 fotogramas
        private const float FlashFrameTime = 0.045f;
        private const float FlashSize = 1.3f;
        private const int SortingOrder = 8;
        private SpriteRenderer _flash;
        private Sprite[] _flashFrames;
        private Coroutine _flashCoroutine;

        private void Awake()
        {
            if (particleSys == null) particleSys = GetComponent<ParticleSystem>();
            ApplyPixelArt();
        }

        private void ApplyPixelArt()
        {
            Texture2D sheet = FxSprites.SparksSheet;
            if (sheet != null && particleSys != null && TryGetComponent(out ParticleSystemRenderer psr))
            {
                var mat = new Material(FxSprites.SpriteMaterial) { name = "DeathVFX_Sparks", mainTexture = sheet };
                psr.sharedMaterial = mat;
                psr.sortingOrder = SortingOrder;

                var sheetAnim = particleSys.textureSheetAnimation;
                sheetAnim.enabled = true;
                sheetAnim.mode = ParticleSystemAnimationMode.Grid;
                sheetAnim.numTilesX = 3;
                sheetAnim.numTilesY = 1;
                sheetAnim.cycleCount = 1;
                sheetAnim.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 0.999f)); // 1.0 volvería al primer fotograma

                // Los fotogramas ya encogen la chispa; sin giro para no deformar los píxeles
                var sizeOverLifetime = particleSys.sizeOverLifetime;
                sizeOverLifetime.enabled = false;
                var rotation = particleSys.rotationOverLifetime;
                rotation.enabled = false;
                var main = particleSys.main;
                main.startRotation = 0f;
            }

            _flashFrames = FxSprites.Flash;
            if (_flashFrames != null)
            {
                var go = new GameObject("Flash");
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * FlashSize;
                _flash = go.AddComponent<SpriteRenderer>();
                _flash.sortingOrder = SortingOrder + 1;
                _flash.enabled = false;
            }
        }

        private IEnumerator FlashRoutine(Color color)
        {
            _flash.color = Color.Lerp(color, Color.white, 0.55f);
            _flash.enabled = true;
            for (int i = 0; i < _flashFrames.Length; i++)
            {
                _flash.sprite = _flashFrames[i];
                yield return new WaitForSeconds(FlashFrameTime);
            }
            _flash.enabled = false;
            _flashCoroutine = null;
        }

        private void StopFlash()
        {
            if (_flashCoroutine != null)
            {
                StopCoroutine(_flashCoroutine);
                _flashCoroutine = null;
            }
            if (_flash != null) _flash.enabled = false;
        }

        public void OnSpawnFromPool()
        {
            if (particleSys == null) particleSys = GetComponent<ParticleSystem>();
            particleSys.Clear();
            particleSys.Stop();
        }

        public void OnReturnToPool()
        {
            if (_returnCoroutine != null)
            {
                StopCoroutine(_returnCoroutine);
                _returnCoroutine = null;
            }
            StopFlash();

            if (particleSys != null)
            {
                particleSys.Stop();
                particleSys.Clear();
            }
        }

        /// <summary>
        /// Dispara la explosión de partículas con el color del enemigo o jugador destruido.
        /// </summary>
        public void PlayEffect(Color color, int particleCount = 25, float scale = 1f)
        {
            if (particleSys == null) particleSys = GetComponent<ParticleSystem>();

            var main = particleSys.main;
            main.startColor = color;
            main.startSize = 0.25f * scale;

            transform.localScale = Vector3.one * scale;

            particleSys.Clear();
            particleSys.Emit(particleCount);

            if (_flash != null)
            {
                StopFlash();
                _flashCoroutine = StartCoroutine(FlashRoutine(color));
            }

            if (_returnCoroutine != null) StopCoroutine(_returnCoroutine);
            _returnCoroutine = StartCoroutine(ReturnAfterDuration(main.startLifetime.constantMax > 0 ? main.startLifetime.constantMax + 0.1f : 0.8f));
        }

        private IEnumerator ReturnAfterDuration(float duration)
        {
            yield return new WaitForSeconds(duration);

            if (ObjectPooler.Instance != null)
            {
                ObjectPooler.Instance.ReturnToPool(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
