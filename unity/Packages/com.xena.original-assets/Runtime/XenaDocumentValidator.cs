using System;
using System.Collections.Generic;
using System.IO;

namespace Xena.Assets
{
    public sealed class XenaValidationSummary
    {
        public int Nodes, Meshes, Triangles, Clips, SourceFrames;
    }

    public static class XenaDocumentValidator
    {
        private static void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidDataException(message);
        }

        private static void Floats(float[] values, int length, string name)
        {
            Require(values != null && values.Length == length, name + " length");
            foreach (float value in values)
                Require(!float.IsNaN(value) && !float.IsInfinity(value), name + " nonfinite value");
        }

        private static void Quaternion(float[] values, int start)
        {
            double sum = 0;
            for (int i = 0; i < 4; i++) sum += values[start + i] * values[start + i];
            Require(Math.Abs(sum - 1) < 0.0001, "Non-unit quaternion");
        }

        public static XenaValidationSummary Validate(XenaDocument document)
        {
            Require(document != null && document.formatVersion == 1, "Unsupported Xena document version");
            Require(document.unitsPerMeter > 0 && !float.IsInfinity(document.unitsPerMeter), "Invalid unit conversion");
            Require(document.nodes != null && document.nodes.Length > 0 && document.nodes.Length <= 1024, "Missing/excessive nodes");
            Require(document.meshes != null && document.materials != null && document.textures != null && document.clips != null, "Missing arrays");
            var result = new XenaValidationSummary { Nodes = document.nodes.Length, Meshes = document.meshes.Length, Clips = document.clips.Length };
            var names = new HashSet<string>(StringComparer.Ordinal);
            int roots = 0;
            for (int i = 0; i < document.nodes.Length; i++)
            {
                XenaNode node = document.nodes[i];
                Require(node != null && !string.IsNullOrEmpty(node.name) && node.name.IndexOfAny(new[] { '/', '\\' }) < 0, "Invalid node name");
                Require(names.Add(node.name), "Duplicate node name");
                Require(node.parent >= -1 && node.parent < document.nodes.Length && node.parent != i, "Invalid parent");
                Require(node.mesh >= -1 && node.mesh < document.meshes.Length, "Invalid mesh reference");
                Floats(node.translation, 3, "Translation"); Floats(node.rotation, 4, "Rotation"); Floats(node.scale, 3, "Scale");
                Quaternion(node.rotation, 0);
                if (node.parent == -1) roots++;
                var visited = new HashSet<int>();
                for (int at = i; at != -1; at = document.nodes[at].parent)
                {
                    Require(at >= 0 && at < document.nodes.Length && document.nodes[at] != null, "Invalid ancestor");
                    Require(visited.Add(at), "Cyclic node hierarchy");
                }
            }
            Require(roots == 1, "Expected one root node");
            foreach (XenaTexture texture in document.textures)
            {
                Require(texture != null, "Null texture");
                Require(!string.IsNullOrEmpty(texture.original), "Missing original texture");
                foreach (string file in new[] { texture.original, texture.upscaled, texture.semiTransparentMask })
                {
                    if (string.IsNullOrEmpty(file) && file != texture.original) continue;
                    Require(!string.IsNullOrEmpty(file) && Path.GetFileName(file) == file && file.EndsWith(".png", StringComparison.OrdinalIgnoreCase), "Unsafe texture filename");
                    Require(file.IndexOfAny(new[] { '/', '\\', ':' }) < 0, "Unsafe texture path");
                }
            }
            foreach (XenaMaterial material in document.materials)
            {
                Require(material != null && material.texture >= -1 && material.texture < document.textures.Length, "Invalid material texture");
                Require(material.blendMode >= 0 && material.blendMode <= 3, "Invalid original blend mode");
                if (material.semiTransparent) Require(material.texture >= 0 && !string.IsNullOrEmpty(document.textures[material.texture].semiTransparentMask), "Semi-transparent material has no original STP mask");
                Floats(material.color, 4, "Material colour");
            }
            foreach (XenaMesh mesh in document.meshes)
            {
                Require(mesh != null && mesh.primitives != null && mesh.primitives.Length > 0, "Empty mesh");
                foreach (XenaPrimitive primitive in mesh.primitives)
                {
                    Require(primitive != null && primitive.positions != null && primitive.positions.Length > 0 && primitive.positions.Length % 3 == 0, "Invalid vertex array");
                    Floats(primitive.positions, primitive.positions.Length, "Positions");
                    Floats(primitive.normals, primitive.positions.Length, "Normals");
                    Require(primitive.uv != null && (primitive.uv.Length == 0 || primitive.uv.Length == primitive.positions.Length / 3 * 2), "Invalid UV array");
                    Floats(primitive.uv, primitive.uv.Length, "UV");
                    Require(primitive.material >= 0 && primitive.material < document.materials.Length, "Invalid material reference");
                    if (primitive.lightingRgb != null && primitive.lightingRgb.Length > 0)
                    {
                        Require(primitive.lightingRgb.Length == primitive.positions.Length, "Invalid lighting RGB length");
                        foreach (int value in primitive.lightingRgb) Require(value >= 0 && value <= 255, "Invalid lighting RGB byte");
                    }
                    if (document.materials[primitive.material].vertexLighting)
                        Require(primitive.lightingRgb != null && primitive.lightingRgb.Length == primitive.positions.Length, "Lit primitive has no lighting RGB");
                    if (document.materials[primitive.material].texture >= 0)
                        Require(primitive.uv.Length == primitive.positions.Length / 3 * 2, "Textured primitive has no UV");
                    Require(primitive.indices != null && primitive.indices.Length % 3 == 0, "Invalid triangle indices");
                    foreach (int index in primitive.indices) Require(index >= 0 && index < primitive.positions.Length / 3, "Triangle index out of range");
                    result.Triangles += primitive.indices.Length / 3;
                }
            }
            var slots = new HashSet<int>();
            foreach (XenaClip clip in document.clips)
            {
                Require(clip != null && !string.IsNullOrEmpty(clip.name) && slots.Add(clip.slot), "Missing/duplicate clip");
                Require(clip.sourceFrameCount > 0 && clip.sourceFrameCount <= 100000 && clip.times != null && clip.times.Length == clip.sourceFrameCount + 1, "Invalid frame count");
                Require(clip.stepsPerSecond > 0 && !float.IsInfinity(clip.stepsPerSecond), "Invalid playback rate");
                Floats(clip.times, clip.times.Length, "Sample times");
                Require(clip.times[0] == 0, "First sample must be at zero");
                for (int i = 1; i < clip.times.Length; i++) Require(clip.times[i] > clip.times[i - 1], "Non-increasing samples");
                Require(clip.channels != null && clip.channels.Length > 0, "Missing channels");
                var targets = new HashSet<string>();
                foreach (XenaChannel channel in clip.channels)
                {
                    Require(channel != null && channel.node >= 0 && channel.node < document.nodes.Length, "Invalid animation target");
                    int expected = channel.property == "rotation" ? 4 : channel.property == "translation" || channel.property == "scale" ? 3 : 0;
                    Require(expected != 0 && channel.components == expected, "Unsupported animation property");
                    Require(targets.Add(channel.node + ":" + channel.property), "Duplicate animation channel");
                    Floats(channel.values, clip.times.Length * channel.components, "Channel samples");
                    if (channel.property == "rotation")
                        for (int i = 0; i < channel.values.Length; i += 4) Quaternion(channel.values, i);
                }
                result.SourceFrames += clip.sourceFrameCount;
            }
            return result;
        }
    }
}
