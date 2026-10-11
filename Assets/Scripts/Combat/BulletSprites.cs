using UnityEngine;

namespace ArenaSurvivor.Combat
{
    /// <summary>
    /// Sprites de pixel art de las balas (Resources/Bullets, 30 PPU como las naves, apuntando a +X).
    /// Se generan con Tools/Art/bullets.py. Si falta alguno devuelve null y la bala usa su forma tintada de siempre.
    /// </summary>
    public static class BulletSprites
    {
        private const string Folder = "Bullets/";

        private static Sprite _player, _laserBurst, _fan, _shotgun;
        private static Sprite[] _orbHostile, _orbParried, _laserHostile, _laserParried;

        // Jugador
        public static Sprite Player => Load(ref _player, "bullet_player");
        public static Sprite LaserBurst => Load(ref _laserBurst, "bullet_laser_burst");
        public static Sprite Fan => Load(ref _fan, "bullet_fan");
        public static Sprite Shotgun => Load(ref _shotgun, "bullet_shotgun");

        // Enemigos: 2 fotogramas de parpadeo (hostil y reflejada con Parry)
        public static Sprite[] OrbHostile => LoadFrames(ref _orbHostile, "enemy_orb_hostile");
        public static Sprite[] OrbParried => LoadFrames(ref _orbParried, "enemy_orb_parried");
        public static Sprite[] LaserHostile => LoadFrames(ref _laserHostile, "enemy_laser_hostile");
        public static Sprite[] LaserParried => LoadFrames(ref _laserParried, "enemy_laser_parried");

        private static Sprite Load(ref Sprite cache, string name)
        {
            if (cache == null) cache = Resources.Load<Sprite>(Folder + name);
            return cache;
        }

        private static Sprite[] LoadFrames(ref Sprite[] cache, string baseName)
        {
            if (cache != null) return cache;
            Sprite a = Resources.Load<Sprite>(Folder + baseName + "_0");
            Sprite b = Resources.Load<Sprite>(Folder + baseName + "_1");
            if (a == null || b == null) return null;
            cache = new[] { a, b };
            return cache;
        }
    }
}
