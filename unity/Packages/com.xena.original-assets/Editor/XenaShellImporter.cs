using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace Xena.Assets.Editor
{
    // Remaster shell baked by tools/build_remaster_shell.py: positions, normals, guide colour + occlusion,
    // four layer weights (uv1) and 32-bit triangles. A plain Mesh asset, visible and usable in the Editor.
    [ScriptedImporter(1, "xenashell")]
    public sealed class XenaShellImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            byte[] data = File.ReadAllBytes(context.assetPath);
            if (data.Length < 12 || data[0] != 'X' || data[1] != 'S' || data[2] != 'H' || data[3] != '1') throw new InvalidDataException("Not a Xena shell: " + context.assetPath);
            int vertices = BitConverter.ToInt32(data, 4), triangles = BitConverter.ToInt32(data, 8), at = 12;
            var positions = new Vector3[vertices]; var normals = new Vector3[vertices];
            for (int i = 0; i < vertices; i++, at += 12) positions[i] = new Vector3(BitConverter.ToSingle(data, at), BitConverter.ToSingle(data, at + 4), BitConverter.ToSingle(data, at + 8));
            for (int i = 0; i < vertices; i++, at += 12) normals[i] = new Vector3(BitConverter.ToSingle(data, at), BitConverter.ToSingle(data, at + 4), BitConverter.ToSingle(data, at + 8));
            var colours = new Color32[vertices]; var layers = new Vector4[vertices];
            for (int i = 0; i < vertices; i++, at += 4) colours[i] = new Color32(data[at], data[at + 1], data[at + 2], data[at + 3]);
            for (int i = 0; i < vertices; i++, at += 4) layers[i] = new Vector4(data[at], data[at + 1], data[at + 2], data[at + 3]) / 255f;
            var indices = new int[triangles * 3]; Buffer.BlockCopy(data, at, indices, 0, indices.Length * 4);
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(context.assetPath), indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(positions); mesh.SetNormals(normals); mesh.SetColors(colours); mesh.SetUVs(1, layers); mesh.SetTriangles(indices, 0, true);
            mesh.UploadMeshData(true);
            context.AddObjectToAsset("mesh", mesh); context.SetMainObject(mesh);
        }
    }
}
