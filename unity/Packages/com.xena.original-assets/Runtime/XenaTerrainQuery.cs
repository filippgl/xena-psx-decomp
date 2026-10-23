using System;
using System.IO;

namespace Xena.Assets
{
    [Serializable]
    public sealed class XenaTerrainData
    {
        public int formatVersion;
        public int[] origin;
        public int[] cellStarts;
        public int[] cellTriangles;
        public int[] vertices;
    }

    // Original func_800142EC uncached height query in PS1 integer coordinates.
    // This is a data/query component, not a Unity movement controller.
    public sealed class XenaTerrainQuery
    {
        public const int NoHeight = -16777216;
        private readonly XenaTerrainData data;

        public XenaTerrainQuery(XenaTerrainData source)
        {
            if (source == null || source.formatVersion != 1 || source.origin == null || source.origin.Length != 2 ||
                source.cellStarts == null || source.cellStarts.Length != 257 || source.cellTriangles == null ||
                source.vertices == null || source.vertices.Length % 9 != 0)
                throw new InvalidDataException("Invalid original terrain data");
            if (source.cellStarts[0] != 0 || source.cellStarts[256] != source.cellTriangles.Length)
                throw new InvalidDataException("Invalid terrain cell range");
            for (int i = 0; i < 256; i++)
                if (source.cellStarts[i] < 0 || source.cellStarts[i] > source.cellStarts[i + 1])
                    throw new InvalidDataException("Terrain cell offsets must be ordered");
            foreach (int triangle in source.cellTriangles)
                if (triangle < 0 || triangle >= source.vertices.Length / 9)
                    throw new InvalidDataException("Terrain triangle index out of range");
            foreach (int coordinate in source.vertices)
                if (coordinate < short.MinValue || coordinate > short.MaxValue)
                    throw new InvalidDataException("Terrain coordinate exceeds PS1 short range");
            data = source;
        }

        private static int Cell(int value, int origin)
        {
            int result = unchecked(value - 2048 - origin) / 4096;
            return Math.Max(0, Math.Min(14, result));
        }

        public int GetHeight(int x, int y, int z)
        {
            unchecked
            {
                int cell = Cell(z, data.origin[1]) * 16 + Cell(x, data.origin[0]);
                int best = int.MaxValue, height = NoHeight;
                for (int neighbor = 0; neighbor < 4; neighbor++)
                {
                    int index = cell + (neighbor & 1) + (neighbor >= 2 ? 16 : 0);
                    for (int item = data.cellStarts[index]; item < data.cellStarts[index + 1]; item++)
                    {
                        int at = data.cellTriangles[item] * 9;
                        int x0 = data.vertices[at] - x, x1 = data.vertices[at + 3] - x, x2 = data.vertices[at + 6] - x;
                        int z0 = data.vertices[at + 2] - z, z1 = data.vertices[at + 5] - z, z2 = data.vertices[at + 8] - z;
                        if ((x0 & x1 & x2) < 0 || (x0 | x1 | x2) > 0 || (z0 & z1 & z2) < 0 || (z0 | z1 | z2) > 0) continue;
                        if ((short)z0 * (short)(x1 - x0) - (short)x0 * (short)(z1 - z0) < 0 ||
                            (short)z1 * (short)(x2 - x1) - (short)x1 * (short)(z2 - z1) < 0 ||
                            (short)z2 * (short)(x0 - x2) - (short)x2 * (short)(z0 - z2) < 0) continue;
                        int ax = (short)(x2 - x1), ay = (short)(data.vertices[at + 7] - data.vertices[at + 4]), az = (short)(z2 - z1);
                        int bx = (short)(x0 - x2), by = (short)(data.vertices[at + 1] - data.vertices[at + 7]), bz = (short)(z0 - z2);
                        int nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
                        int denominator = ny >> 4;
                        if (denominator == 0) continue;
                        int slopeX = (nx << 8) / denominator, slopeZ = (nz << 8) / denominator;
                        int candidate = ((x0 * slopeX) >> 12) + ((z0 * slopeZ) >> 12) + data.vertices[at + 1];
                        int difference = candidate - y;
                        if (difference < 0) difference = -difference;
                        if (best < difference) continue;
                        best = difference; height = candidate;
                    }
                }
                return height;
            }
        }
    }
}
