using SharpDX;
using System;

namespace CodeWalker.GameFiles
{
    public static class ExtensionTransforms
    {
        public static readonly string[] ArchetypeExtensionTypeNames =
        [
            "ParticleEffect",
            "AudioCollisionSettings",
            "AudioEmitter",
            "SpawnPoint",
            "ExplosionEffect",
            "Ladder",
            "Buoyancy",
            "Expression",
            "LightShaft",
            "WindDisturbance",
            "ProcObject",
        ];

        public static bool TryGetOffsetPosition(MetaWrapper? ext, out Vector3 pos)
        {
            pos = Vector3.Zero;
            if (ext == null) return false;

            if (ext is MCExtensionDefLightEffect le) { pos = le._Data.offsetPosition; return true; }
            if (ext is MCExtensionDefSpawnPointOverride spo) { pos = spo._Data.offsetPosition; return true; }
            if (ext is MCExtensionDefDoor door) { pos = door._Data.offsetPosition; return true; }
            if (ext is Mrage__phVerletClothCustomBounds cb)
            {
                if ((cb.CollisionData != null) && (cb.CollisionData.Length > 0))
                {
                    pos = cb.CollisionData[0].Data.Position;
                    return true;
                }
                return false;
            }
            if (ext is MCExtensionDefParticleEffect pe) { pos = pe._Data.offsetPosition; return true; }
            if (ext is MCExtensionDefAudioCollisionSettings acs) { pos = acs._Data.offsetPosition; return true; }
            if (ext is MCExtensionDefAudioEmitter ae) { pos = ae._Data.offsetPosition; return true; }
            if (ext is MCExtensionDefSpawnPoint sp) { pos = sp._Data.offsetPosition; return true; }
            if (ext is MCExtensionDefExplosionEffect ee) { pos = ee._Data.offsetPosition; return true; }
            if (ext is MCExtensionDefLadder ld) { pos = ld._Data.offsetPosition; return true; }
            if (ext is MCExtensionDefBuoyancy bu) { pos = bu._Data.offsetPosition; return true; }
            if (ext is MCExtensionDefExpression exp) { pos = exp._Data.offsetPosition; return true; }
            if (ext is MCExtensionDefLightShaft ls) { pos = ls._Data.offsetPosition; return true; }
            if (ext is MCExtensionDefWindDisturbance wd) { pos = wd._Data.offsetPosition; return true; }
            if (ext is MCExtensionDefProcObject po) { pos = po._Data.offsetPosition; return true; }

            return false;
        }

        public static bool TrySetOffsetPosition(MetaWrapper? ext, Vector3 pos)
        {
            if (ext == null) return false;

            if (ext is MCExtensionDefLightEffect le) { le._Data.offsetPosition = pos; return true; }
            if (ext is MCExtensionDefSpawnPointOverride spo) { spo._Data.offsetPosition = pos; return true; }
            if (ext is MCExtensionDefDoor door) { door._Data.offsetPosition = pos; return true; }
            if (ext is Mrage__phVerletClothCustomBounds cb)
            {
                if ((cb.CollisionData != null) && (cb.CollisionData.Length > 0))
                {
                    cb.CollisionData[0]._Data.Position = pos;
                    return true;
                }
                return false;
            }
            if (ext is MCExtensionDefParticleEffect pe) { pe._Data.offsetPosition = pos; return true; }
            if (ext is MCExtensionDefAudioCollisionSettings acs) { acs._Data.offsetPosition = pos; return true; }
            if (ext is MCExtensionDefAudioEmitter ae) { ae._Data.offsetPosition = pos; return true; }
            if (ext is MCExtensionDefSpawnPoint sp) { sp._Data.offsetPosition = pos; return true; }
            if (ext is MCExtensionDefExplosionEffect ee) { ee._Data.offsetPosition = pos; return true; }
            if (ext is MCExtensionDefLadder ld) { ld._Data.offsetPosition = pos; return true; }
            if (ext is MCExtensionDefBuoyancy bu) { bu._Data.offsetPosition = pos; return true; }
            if (ext is MCExtensionDefExpression exp) { exp._Data.offsetPosition = pos; return true; }
            if (ext is MCExtensionDefLightShaft ls) { ls._Data.offsetPosition = pos; return true; }
            if (ext is MCExtensionDefWindDisturbance wd) { wd._Data.offsetPosition = pos; return true; }
            if (ext is MCExtensionDefProcObject po) { po._Data.offsetPosition = pos; return true; }

            return false;
        }

        public static bool HasOffsetRotation(MetaWrapper? ext)
        {
            return ext is MCExtensionDefParticleEffect
                or MCExtensionDefAudioEmitter
                or MCExtensionDefSpawnPoint
                or MCExtensionDefExplosionEffect
                or MCExtensionDefWindDisturbance;
        }

        public static bool TryGetOffsetRotation(MetaWrapper? ext, out Quaternion rot)
        {
            rot = Quaternion.Identity;
            if (ext == null) return false;

            if (ext is MCExtensionDefParticleEffect pe) { rot = new Quaternion(pe._Data.offsetRotation); return true; }
            if (ext is MCExtensionDefAudioEmitter ae) { rot = new Quaternion(ae._Data.offsetRotation); return true; }
            if (ext is MCExtensionDefSpawnPoint sp) { rot = sp.Orientation; return true; }
            if (ext is MCExtensionDefExplosionEffect ee) { rot = new Quaternion(ee._Data.offsetRotation); return true; }
            if (ext is MCExtensionDefWindDisturbance wd) { rot = new Quaternion(wd._Data.offsetRotation); return true; }

            return false;
        }

        public static bool TrySetOffsetRotation(MetaWrapper? ext, Quaternion rot)
        {
            if (ext == null) return false;
            rot = Quaternion.Normalize(rot);
            var v = rot.ToVector4();

            if (ext is MCExtensionDefParticleEffect pe) { pe._Data.offsetRotation = v; return true; }
            if (ext is MCExtensionDefAudioEmitter ae) { ae._Data.offsetRotation = v; return true; }
            if (ext is MCExtensionDefSpawnPoint sp) { sp.Orientation = rot; return true; }
            if (ext is MCExtensionDefExplosionEffect ee) { ee._Data.offsetRotation = v; return true; }
            if (ext is MCExtensionDefWindDisturbance wd) { wd._Data.offsetRotation = v; return true; }

            return false;
        }

        public static float GetExtensionBoxSize(MetaWrapper? ext)
        {
            if (ext is MCExtensionDefSpawnPointOverride spo)
                return Math.Max(0.1f, spo._Data.Radius);
            return 0.5f;
        }

        public static Vector3 LocalToWorld(YmapEntityDef? entity, Vector3 local)
        {
            if (entity == null) return local;
            return entity.Position + entity.Orientation.Multiply(local);
        }

        public static Vector3 WorldToLocal(YmapEntityDef? entity, Vector3 world)
        {
            if (entity == null) return world;
            return Quaternion.Invert(entity.Orientation).Multiply(world - entity.Position);
        }

        public static Quaternion LocalToWorld(YmapEntityDef? entity, Quaternion local)
        {
            if (entity == null) return local;
            return Quaternion.Normalize(local * entity.Orientation);
        }

        public static Quaternion WorldToLocal(YmapEntityDef? entity, Quaternion world)
        {
            if (entity == null) return world;
            return Quaternion.Normalize(Quaternion.Invert(entity.Orientation) * world);
        }

        public static MetaWrapper? CreateArchetypeExtension(string typeName, MetaHash defaultName = default)
        {
            MetaWrapper? ext = typeName switch
            {
                "ParticleEffect" => new MCExtensionDefParticleEffect(),
                "AudioCollisionSettings" => new MCExtensionDefAudioCollisionSettings(),
                "AudioEmitter" => new MCExtensionDefAudioEmitter(),
                "SpawnPoint" => new MCExtensionDefSpawnPoint(),
                "ExplosionEffect" => new MCExtensionDefExplosionEffect(),
                "Ladder" => new MCExtensionDefLadder(),
                "Buoyancy" => new MCExtensionDefBuoyancy(),
                "Expression" => new MCExtensionDefExpression(),
                "LightShaft" => new MCExtensionDefLightShaft(),
                "WindDisturbance" => new MCExtensionDefWindDisturbance(),
                "ProcObject" => new MCExtensionDefProcObject(),
                _ => null,
            };

            if (ext == null) return null;

            TrySetOffsetPosition(ext, Vector3.Zero);
            if (HasOffsetRotation(ext))
                TrySetOffsetRotation(ext, Quaternion.Identity);

            SetExtensionNameHash(ext, defaultName);

            if (ext is MCExtensionDefParticleEffect pe)
            {
                pe.fxName = string.Empty;
                pe._Data.scale = 1.0f;
                pe._Data.probability = 100;
                pe._Data.color = 0xFFFFFFFF;
            }
            else if (ext is MCExtensionDefExplosionEffect ee)
            {
                ee.explosionName = string.Empty;
            }
            else if (ext is MCExtensionDefSpawnPoint sp)
            {
                sp.Probability = 1.0f;
                sp.StartTime = 0;
                sp.EndTime = 24;
            }
            else if (ext is MCExtensionDefLadder ld)
            {
                ld._Data.bottom = new Vector3(0, 0, 0);
                ld._Data.top = new Vector3(0, 0, 2);
                ld._Data.normal = new Vector3(0, 1, 0);
            }

            return ext;
        }

        private static void SetExtensionNameHash(MetaWrapper ext, MetaHash name)
        {
            if (ext is MCExtensionDefParticleEffect pe) pe._Data.name = name;
            else if (ext is MCExtensionDefAudioCollisionSettings acs) acs._Data.name = name;
            else if (ext is MCExtensionDefAudioEmitter ae) ae._Data.name = name;
            else if (ext is MCExtensionDefSpawnPoint sp) sp._Data.name = name;
            else if (ext is MCExtensionDefExplosionEffect ee) ee._Data.name = name;
            else if (ext is MCExtensionDefLadder ld) ld._Data.name = name;
            else if (ext is MCExtensionDefBuoyancy bu) bu._Data.name = name;
            else if (ext is MCExtensionDefExpression exp) exp._Data.name = name;
            else if (ext is MCExtensionDefLightShaft ls) ls._Data.name = name;
            else if (ext is MCExtensionDefWindDisturbance wd) wd._Data.name = name;
            else if (ext is MCExtensionDefProcObject po) po._Data.name = name;
        }
    }
}
