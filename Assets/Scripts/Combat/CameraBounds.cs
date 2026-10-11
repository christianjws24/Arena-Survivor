using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Límites visibles de la cámara principal sobre el plano de juego (z = 0).
    /// Funciona tanto con cámara ortográfica como en perspectiva (la escena usa perspectiva con FOV amplio).
    /// </summary>
    public static class CameraBounds
    {
        public static bool TryGet(out Vector2 center, out Vector2 halfExtents, Camera cam = null)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null)
            {
                center = Vector2.zero;
                halfExtents = Vector2.zero;
                return false;
            }

            center = cam.transform.position;
            float halfHeight = cam.orthographic
                ? cam.orthographicSize
                : Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Abs(cam.transform.position.z);
            halfExtents = new Vector2(halfHeight * cam.aspect, halfHeight);
            return true;
        }

        /// <summary>
        /// Posición aleatoria visible dentro de la pantalla (porcentaje 'inset' del campo visual), opcionalmente lejos del jugador.
        /// </summary>
        public static Vector2 RandomVisiblePoint(float inset = 0.7f, Vector2? avoid = null, float minDistance = 0f)
        {
            if (!TryGet(out Vector2 center, out Vector2 half))
            {
                return Random.insideUnitCircle * 5f;
            }

            Vector2 point = center;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                point = center + new Vector2(Random.Range(-half.x, half.x) * inset, Random.Range(-half.y, half.y) * inset);
                if (!avoid.HasValue || (point - avoid.Value).sqrMagnitude >= minDistance * minDistance) break;
            }
            return point;
        }
    }
}
