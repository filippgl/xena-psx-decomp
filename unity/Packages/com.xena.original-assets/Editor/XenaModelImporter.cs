using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace Xena.Assets.Editor
{
    [ScriptedImporter(2, "xenamodel")]
    public sealed class XenaModelImporter : ScriptedImporter
    {
        private const string ShaderPath = "Packages/com.xena.original-assets/Shaders/OriginalUnlit.shader";

        public override void OnImportAsset(AssetImportContext context)
        {
            XenaDocument document = JsonUtility.FromJson<XenaDocument>(File.ReadAllText(context.assetPath));
            XenaDocumentValidator.Validate(document);
            context.DependsOnArtifact(ShaderPath);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) throw new InvalidDataException("Xena shader is unavailable");
            var originalTextures = new Texture2D[document.textures.Length];
            var upscaledTextures = new Texture2D[document.textures.Length];
            for (int i = 0; i < document.textures.Length; i++)
            {
                originalTextures[i] = ReadTexture(context, document.textures[i].original, "texture-original-" + i);
                upscaledTextures[i] = string.IsNullOrEmpty(document.textures[i].upscaled) ? originalTextures[i] :
                    ReadTexture(context, document.textures[i].upscaled, "texture-upscaled-" + i);
            }
            var originalMaterials = CreateMaterials(context, document, shader, originalTextures, "original");
            var upscaledMaterials = CreateMaterials(context, document, shader, upscaledTextures, "upscaled");
            var root = new GameObject(Path.GetFileNameWithoutExtension(context.assetPath));
            context.AddObjectToAsset("model", root);
            context.SetMainObject(root);
            var nodes = new Transform[document.nodes.Length];
            for (int i = 0; i < nodes.Length; i++) nodes[i] = new GameObject(document.nodes[i].name).transform;
            for (int i = 0; i < nodes.Length; i++)
            {
                XenaNode source = document.nodes[i];
                nodes[i].SetParent(source.parent < 0 ? root.transform : nodes[source.parent], false);
                nodes[i].localPosition = Vector(source.translation);
                nodes[i].localRotation = new Quaternion(source.rotation[0], source.rotation[1], source.rotation[2], source.rotation[3]);
                nodes[i].localScale = Vector(source.scale);
            }
            var meshes = new Mesh[document.meshes.Length];
            for (int i = 0; i < meshes.Length; i++)
            {
                meshes[i] = CreateMesh(document.meshes[i]);
                context.AddObjectToAsset("mesh-" + i, meshes[i]);
            }
            var bindings = new List<XenaMaterialBinding>();
            for (int i = 0; i < nodes.Length; i++)
            {
                int mesh = document.nodes[i].mesh;
                if (mesh < 0) continue;
                nodes[i].gameObject.AddComponent<MeshFilter>().sharedMesh = meshes[mesh];
                MeshRenderer renderer = nodes[i].gameObject.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                XenaPrimitive[] primitives = document.meshes[mesh].primitives;
                var originals = new Material[primitives.Length]; var upscaled = new Material[primitives.Length];
                for (int p = 0; p < primitives.Length; p++)
                {
                    originals[p] = originalMaterials[primitives[p].material];
                    upscaled[p] = upscaledMaterials[primitives[p].material];
                }
                renderer.sharedMaterials = upscaled;
                bindings.Add(new XenaMaterialBinding { renderer = renderer, original = originals, upscaled = upscaled });
            }
            var clips = new AnimationClip[document.clips.Length];
            var slots = new int[clips.Length]; var counts = new int[clips.Length]; var rates = new float[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                XenaClip source = document.clips[i];
                clips[i] = CreateClip(document, source);
                slots[i] = source.slot; counts[i] = source.sourceFrameCount; rates[i] = source.stepsPerSecond;
                context.AddObjectToAsset("clip-" + source.slot.ToString("X2"), clips[i]);
            }
            root.AddComponent<XenaAssetPreview>().Configure(clips, slots, counts, rates, bindings.ToArray());
            if (Array.Exists(document.materials, material => material.vertexLighting))
                root.AddComponent<XenaLevelLighting>().Configure(root.GetComponentsInChildren<Renderer>());
        }

        private static Texture2D ReadTexture(AssetImportContext context, string filename, string identifier)
        {
            string assetPath = Path.Combine(Path.GetDirectoryName(context.assetPath), filename).Replace('\\', '/');
            context.DependsOnSourceAsset(assetPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { name = Path.GetFileNameWithoutExtension(filename) };
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(assetPath), false))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw new InvalidDataException("Cannot decode texture " + filename);
            }
            texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp; texture.anisoLevel = 0;
            context.AddObjectToAsset(identifier, texture);
            return texture;
        }

        private static Material[] CreateMaterials(AssetImportContext context, XenaDocument document, Shader shader, Texture2D[] textures, string variant)
        {
            var materials = new Material[document.materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                XenaMaterial source = document.materials[i];
                var material = new Material(shader) { name = source.name + "_" + variant };
                material.SetColor("_Color", new Color(source.color[0], source.color[1], source.color[2], source.color[3]));
                material.SetFloat("_Cutoff", 0.5f);
                material.SetFloat("_UseVertexLighting", source.vertexLighting ? 1 : 0);
                material.SetFloat("_LightBias", 0);
                material.SetInt("_Cull", source.doubleSided ? (int)CullMode.Off : (int)CullMode.Back);
                if (source.texture >= 0) material.SetTexture("_MainTex", textures[source.texture]);
                materials[i] = material;
                context.AddObjectToAsset("material-" + variant + "-" + i, material);
            }
            return materials;
        }

        private static Mesh CreateMesh(XenaMesh source)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
            var colors = new List<Color32>();
            var triangles = new List<int[]>();
            foreach (XenaPrimitive primitive in source.primitives)
            {
                int start = vertices.Count;
                for (int i = 0; i < primitive.positions.Length; i += 3)
                {
                    vertices.Add(new Vector3(primitive.positions[i], primitive.positions[i + 1], primitive.positions[i + 2]));
                    bool lit = primitive.lightingRgb != null && primitive.lightingRgb.Length > 0;
                    colors.Add(lit ? new Color32((byte)primitive.lightingRgb[i], (byte)primitive.lightingRgb[i + 1], (byte)primitive.lightingRgb[i + 2], 255) : new Color32(128, 128, 128, 255));
                    normals.Add(new Vector3(primitive.normals[i], primitive.normals[i + 1], primitive.normals[i + 2]));
                    int at = i / 3 * 2;
                    uv.Add(primitive.uv.Length == 0 ? Vector2.zero : new Vector2(primitive.uv[at], primitive.uv[at + 1]));
                }
                var indices = new int[primitive.indices.Length];
                for (int i = 0; i < indices.Length; i++) indices[i] = start + primitive.indices[i];
                triangles.Add(indices);
            }
            var mesh = new Mesh { name = source.name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.subMeshCount = triangles.Count;
            for (int i = 0; i < triangles.Count; i++) mesh.SetTriangles(triangles[i], i, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static AnimationClip CreateClip(XenaDocument document, XenaClip source)
        {
            var clip = new AnimationClip { name = source.name, legacy = true, frameRate = source.stepsPerSecond, wrapMode = WrapMode.Loop };
            var bindings = new List<EditorCurveBinding>(); var curves = new List<AnimationCurve>();
            foreach (XenaChannel channel in source.channels)
            {
                string property = channel.property == "translation" ? "m_LocalPosition" : channel.property == "rotation" ? "m_LocalRotation" : "m_LocalScale";
                for (int component = 0; component < channel.components; component++)
                {
                    var keys = new Keyframe[source.times.Length];
                    for (int key = 0; key < keys.Length; key++)
                        keys[key] = new Keyframe(source.times[key], channel.values[key * channel.components + component], float.PositiveInfinity, float.PositiveInfinity);
                    var curve = new AnimationCurve(keys);
                    for (int key = 0; key < keys.Length; key++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, key, AnimationUtility.TangentMode.Constant);
                        AnimationUtility.SetKeyRightTangentMode(curve, key, AnimationUtility.TangentMode.Constant);
                    }
                    bindings.Add(EditorCurveBinding.FloatCurve(NodePath(document, channel.node), typeof(Transform), property + "." + "xyzw"[component]));
                    curves.Add(curve);
                }
            }
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            return clip;
        }

        public static string NodePath(XenaDocument document, int index)
        {
            var names = new List<string>();
            for (int at = index; at >= 0; at = document.nodes[at].parent) names.Add(document.nodes[at].name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static Vector3 Vector(float[] values) => new Vector3(values[0], values[1], values[2]);
    }
}
