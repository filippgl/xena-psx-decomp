using System;

namespace Xena.Assets
{
    // Public fields intentionally follow JsonUtility's serialization contract.
    // This file has no Unity dependency and is also compiled by the .NET checker.
    [Serializable] public sealed class XenaDocument
    {
        public int formatVersion;
        public string source;
        public string sourceBufferSha256;
        public float unitsPerMeter;
        public string conventions;
        public XenaNode[] nodes;
        public XenaMesh[] meshes;
        public XenaMaterial[] materials;
        public XenaTexture[] textures;
        public XenaClip[] clips;
    }

    [Serializable] public sealed class XenaNode
    {
        public string name;
        public int parent;
        public int mesh;
        public float[] translation;
        public float[] rotation;
        public float[] scale;
    }

    [Serializable] public sealed class XenaMesh
    {
        public string name;
        public XenaPrimitive[] primitives;
    }

    [Serializable] public sealed class XenaPrimitive
    {
        public int material;
        public float[] positions;
        public float[] normals;
        public float[] uv;
        public int[] indices;
        public int[] lightingRgb;
    }

    [Serializable] public sealed class XenaMaterial
    {
        public string name;
        public bool doubleSided;
        public bool vertexLighting;
        public int texture;
        public float[] color;
    }

    [Serializable] public sealed class XenaTexture
    {
        public string original;
        public string upscaled;
    }

    [Serializable] public sealed class XenaClip
    {
        public string name;
        public int slot;
        public int sourceFrameCount;
        public float stepsPerSecond;
        public float[] times;
        public XenaChannel[] channels;
    }

    [Serializable] public sealed class XenaChannel
    {
        public int node;
        public string property;
        public int components;
        public float[] values;
    }
}
