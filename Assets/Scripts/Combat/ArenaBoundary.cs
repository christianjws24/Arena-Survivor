using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Delimita la arena de combate manteniendo al jugador y a las entidades dentro de un perímetro seguro.
    /// Arquitectura optimizada:
    /// - Para cientos de enemigos: Genera 4 BoxCollider2D estáticos perimetrales (procesados nativamente en C++ por Box2D).
    /// - Para el Jugador: Expone límites Vector2 y ClampPosition(pos) de evaluación inmediata.
    /// </summary>
    [DisallowMultipleComponent]
    public class ArenaBoundary : MonoBehaviour
    {
        public static ArenaBoundary Instance { get; private set; }

        [Header("Dimensiones del Área de Combate")]
        [Tooltip("Extensión total de la arena (Ancho X, Alto Y)")]
        [SerializeField] private Vector2 arenaSize = new Vector2(60f, 40f);

        [Header("Grosor de Muros Perimetrales")]
        [SerializeField] private float wallThickness = 2.0f;

        [Header("Capa de Físicas de los Muros")]
        [SerializeField] private LayerMask boundaryLayer;

        public Vector2 ArenaSize => arenaSize;
        public Bounds ArenaBounds => new Bounds(transform.position, new Vector3(arenaSize.x, arenaSize.y, 10f));

        private PhysicsMaterial2D _zeroFrictionMat;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            _zeroFrictionMat = new PhysicsMaterial2D("BoundaryZeroFriction")
            {
                friction = 0f,
                bounciness = 0f
            };

            GenerateBoundaryColliders();
        }

        /// <summary>
        /// Restringe una posición 2D dentro del perímetro de la arena (utilizado por el Jugador en FixedUpdate).
        /// </summary>
        public Vector2 ClampPosition(Vector2 position, float padding = 0.5f)
        {
            Vector2 center = transform.position;
            float halfWidth = (arenaSize.x * 0.5f) - padding;
            float halfHeight = (arenaSize.y * 0.5f) - padding;

            float clampedX = Mathf.Clamp(position.x, center.x - halfWidth, center.x + halfWidth);
            float clampedY = Mathf.Clamp(position.y, center.y - halfHeight, center.y + halfHeight);

            return new Vector2(clampedX, clampedY);
        }

        /// <summary>
        /// Crea o actualiza 4 muros físicos estáticos para contener colisiones de cientos de entidades a coste nativo.
        /// </summary>
        public void GenerateBoundaryColliders()
        {
            // Eliminar colisionadores hijos anteriores si existieran
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name.StartsWith("BoundaryWall_"))
                {
                    Destroy(child.gameObject);
                }
            }

            float halfW = arenaSize.x * 0.5f;
            float halfH = arenaSize.y * 0.5f;
            float halfThick = wallThickness * 0.5f;

            // Muro Superior
            CreateWall("BoundaryWall_Top", new Vector2(0f, halfH + halfThick), new Vector2(arenaSize.x + (wallThickness * 2f), wallThickness));
            // Muro Inferior
            CreateWall("BoundaryWall_Bottom", new Vector2(0f, -halfH - halfThick), new Vector2(arenaSize.x + (wallThickness * 2f), wallThickness));
            // Muro Izquierdo
            CreateWall("BoundaryWall_Left", new Vector2(-halfW - halfThick, 0f), new Vector2(wallThickness, arenaSize.y));
            // Muro Derecho
            CreateWall("BoundaryWall_Right", new Vector2(halfW + halfThick, 0f), new Vector2(wallThickness, arenaSize.y));
        }

        private void CreateWall(string wallName, Vector2 localPos, Vector2 size)
        {
            GameObject wallGo = new GameObject(wallName);
            wallGo.transform.SetParent(transform, false);
            wallGo.transform.localPosition = localPos;

            BoxCollider2D box = wallGo.AddComponent<BoxCollider2D>();
            box.size = size;
            if (_zeroFrictionMat != null)
            {
                box.sharedMaterial = _zeroFrictionMat;
            }

            // Asignar layer si está configurado
            if (boundaryLayer.value != 0)
            {
                wallGo.layer = GetFirstLayerFromMask(boundaryLayer);
            }
        }

        private int GetFirstLayerFromMask(LayerMask mask)
        {
            int value = mask.value;
            for (int i = 0; i < 32; i++)
            {
                if ((value & (1 << i)) != 0) return i;
            }
            return 0;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireCube(transform.position, new Vector3(arenaSize.x, arenaSize.y, 0f));

            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.25f);
            Gizmos.DrawCube(transform.position, new Vector3(arenaSize.x, arenaSize.y, 0f));
        }
    }
}
