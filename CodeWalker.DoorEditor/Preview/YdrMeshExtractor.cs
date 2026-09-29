using System;
using System.Collections.Generic;
using System.IO;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor.Preview
{
    public readonly struct MeshTri
    {
        public readonly float Ax, Ay, Az;
        public readonly float Bx, By, Bz;
        public readonly float Cx, Cy, Cz;

        public MeshTri(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz)
        {
            Ax = ax; Ay = ay; Az = az;
            Bx = bx; By = by; Bz = bz;
            Cx = cx; Cy = cy; Cz = cz;
        }
    }

    public sealed class YdrMeshData
    {
        public string Name { get; init; } = "";
        public List<MeshTri> Triangles { get; } = new();
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

        public float SizeX => MaxX - MinX;
        public float SizeY => MaxY - MinY;
        public float SizeZ => MaxZ - MinZ;
        public float CenterX => (MinX + MaxX) * 0.5f;
        public float CenterY => (MinY + MaxY) * 0.5f;
        public float CenterZ => (MinZ + MaxZ) * 0.5f;
        public float MaxDim => Math.Max(SizeX, Math.Max(SizeY, SizeZ));
    }

    public static class YdrMeshExtractor
    {
        public static YdrMeshData LoadFromFile(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var ydr = new YdrFile();
            ydr.Load(bytes);
            ydr.Name = Path.GetFileName(path);
            return FromYdr(ydr);
        }

        public static YdrMeshData FromYdr(YdrFile ydr)
        {
            var data = new YdrMeshData { Name = ydr.Name ?? "door" };
            var drawable = ydr.Drawable;
            if (drawable == null) return data;

            var models = drawable.DrawableModels?.High
                ?? drawable.AllModels;
            if (models == null || models.Length == 0) return data;

            bool first = true;
            foreach (var model in models)
            {
                if (model?.Geometries == null) continue;
                foreach (var geom in model.Geometries)
                {
                    if (geom?.VertexData?.Data == null || geom.IndexBuffer?.Indices == null)
                        continue;

                    var vdata = geom.VertexData;
                    var indices = geom.IndexBuffer.Indices;
                    int stride = vdata.Stride;
                    int vcount = vdata.VertexCount;
                    var bytes = vdata.Data;
                    if (stride < 12 || indices.Length < 3) continue;

                    for (int t = 0; t + 2 < indices.Length; t += 3)
                    {
                        int i0 = indices[t];
                        int i1 = indices[t + 1];
                        int i2 = indices[t + 2];
                        if (i0 >= vcount || i1 >= vcount || i2 >= vcount) continue;

                        ReadPos(bytes, i0 * stride, out float ax, out float ay, out float az);
                        ReadPos(bytes, i1 * stride, out float bx, out float by, out float bz);
                        ReadPos(bytes, i2 * stride, out float cx, out float cy, out float cz);

                        data.Triangles.Add(new MeshTri(ax, ay, az, bx, by, bz, cx, cy, cz));

                        if (first)
                        {
                            data.MinX = data.MaxX = ax;
                            data.MinY = data.MaxY = ay;
                            data.MinZ = data.MaxZ = az;
                            first = false;
                        }
                        Expand(ref data.MinX, ref data.MaxX, ax, bx, cx);
                        Expand(ref data.MinY, ref data.MaxY, ay, by, cy);
                        Expand(ref data.MinZ, ref data.MaxZ, az, bz, cz);
                    }
                }
            }

            return data;
        }

        private static void ReadPos(byte[] bytes, int offset, out float x, out float y, out float z)
        {
            x = BitConverter.ToSingle(bytes, offset);
            y = BitConverter.ToSingle(bytes, offset + 4);
            z = BitConverter.ToSingle(bytes, offset + 8);
        }

        private static void Expand(ref float min, ref float max, float a, float b, float c)
        {
            if (a < min) min = a; if (a > max) max = a;
            if (b < min) min = b; if (b > max) max = b;
            if (c < min) min = c; if (c > max) max = c;
        }
    }
}
