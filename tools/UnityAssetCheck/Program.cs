using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xena.Assets;

string asset = "unity/XenaUnity/Assets/Imported/Xena/Xena.xenamodel";
var options = new JsonSerializerOptions { IncludeFields = true };
XenaDocument Load() => JsonSerializer.Deserialize<XenaDocument>(File.ReadAllText(asset), options);
void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
int syntaxFiles = 0;
foreach (string sourceFile in Directory.EnumerateFiles("unity/Packages/com.xena.original-assets", "*.cs", SearchOption.AllDirectories))
{
    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile), new CSharpParseOptions(LanguageVersion.CSharp9));
    var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
    Check(errors.Length == 0, sourceFile + ": " + string.Join("; ", errors.Select(d => d.ToString())));
    syntaxFiles++;
}
XenaDocument document = Load();
XenaValidationSummary summary = XenaDocumentValidator.Validate(document);
Check(summary.Meshes == 33 && summary.Triangles == 426 && summary.Clips == 81 && summary.SourceFrames == 1674, "Unexpected source totals");
using var gltfDocument = JsonDocument.Parse(File.ReadAllText("build/xena-preview/xena-original.gltf"));
JsonElement gltf = gltfDocument.RootElement;
byte[] binary = File.ReadAllBytes("build/xena-preview/xena.bin");
Check(Convert.ToHexString(SHA256.HashData(binary)).ToLowerInvariant() == document.sourceBufferSha256, "Source hash mismatch");
float[] Read(int index)
{
    JsonElement accessor = gltf.GetProperty("accessors")[index];
    JsonElement view = gltf.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
    int components = accessor.GetProperty("type").GetString() switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, _ => throw new Exception("Unknown accessor") };
    int count = accessor.GetProperty("count").GetInt32() * components;
    int start = view.GetProperty("byteOffset").GetInt32();
    return Enumerable.Range(0, count).Select(i => BitConverter.ToSingle(binary, start + i * 4)).ToArray();
}
long compared = 0;
void Compare(float actual, float expected, string label)
{
    Check(Math.Abs(actual - expected) <= 0.00001f, label + " coordinate mismatch"); compared++;
}
var expectedParents = Enumerable.Repeat(-1, document.nodes.Length).ToArray();
JsonElement originalNodes = gltf.GetProperty("nodes");
for (int i = 0; i < document.nodes.Length; i++)
    if (originalNodes[i].TryGetProperty("children", out JsonElement children))
        foreach (JsonElement child in children.EnumerateArray()) expectedParents[child.GetInt32()] = i;
for (int i = 0; i < document.nodes.Length; i++)
{
    XenaNode node = document.nodes[i]; JsonElement originalNode = originalNodes[i];
    Check(node.parent == expectedParents[i], "Hierarchy changed");
    Check(node.name == originalNode.GetProperty("name").GetString(), "Node name changed");
    Check(node.mesh == (originalNode.TryGetProperty("mesh", out JsonElement meshReference) ? meshReference.GetInt32() : -1), "Mesh binding changed");
    foreach ((string property, float[] values, float[] fallback) in new[] {
        ("translation", node.translation, new float[] {0, 0, 0}),
        ("rotation", node.rotation, new float[] {0, 0, 0, 1}),
        ("scale", node.scale, new float[] {1, 1, 1}) })
    {
        float[] original = originalNode.TryGetProperty(property, out JsonElement array) ? array.EnumerateArray().Select(n => n.GetSingle()).ToArray() : fallback;
        for (int n = 0; n < values.Length; n++)
        {
            bool reflect = property == "translation" && n == 2 || property == "rotation" && n < 2;
            Compare(values[n], reflect ? -original[n] : original[n], "Initial " + property);
        }
    }
}
for (int mesh = 0; mesh < document.meshes.Length; mesh++)
{
    JsonElement originals = gltf.GetProperty("meshes")[mesh].GetProperty("primitives");
    for (int primitive = 0; primitive < document.meshes[mesh].primitives.Length; primitive++)
    {
        XenaPrimitive target = document.meshes[mesh].primitives[primitive];
        JsonElement attributes = originals[primitive].GetProperty("attributes");
        foreach ((string name, float[] values) in new[] { ("POSITION", target.positions), ("NORMAL", target.normals) })
        {
            float[] original = Read(attributes.GetProperty(name).GetInt32());
            Check(values.Length == original.Length, "Geometry length changed");
            for (int i = 0; i < values.Length; i++) Compare(values[i], i % 3 == 2 ? -original[i] : original[i], name);
        }
        if (attributes.TryGetProperty("TEXCOORD_0", out JsonElement uvAccessor))
        {
            float[] original = Read(uvAccessor.GetInt32());
            for (int i = 0; i < original.Length; i++) Compare(target.uv[i], i % 2 == 1 ? 1 - original[i] : original[i], "UV");
        }
        for (int i = 0; i < target.indices.Length; i++) Check(target.indices[i] == i - i % 3 + new[] { 0, 2, 1 }[i % 3], "Triangle winding not reflected");
    }
}
for (int clipIndex = 0; clipIndex < document.clips.Length; clipIndex++)
{
    XenaClip clip = document.clips[clipIndex];
    JsonElement original = gltf.GetProperty("animations")[clipIndex];
    Check(clip.slot == original.GetProperty("extras").GetProperty("originalSlot").GetInt32(), "Clip order changed");
    for (int channel = 0; channel < clip.channels.Length; channel++)
    {
        XenaChannel target = clip.channels[channel];
        JsonElement originalChannel = original.GetProperty("channels")[channel];
        JsonElement sampler = original.GetProperty("samplers")[originalChannel.GetProperty("sampler").GetInt32()];
        float[] values = Read(sampler.GetProperty("output").GetInt32());
        float[] times = Read(sampler.GetProperty("input").GetInt32());
        Check(target.node == originalChannel.GetProperty("target").GetProperty("node").GetInt32(), "Wrong channel target");
        Check(target.property == originalChannel.GetProperty("target").GetProperty("path").GetString(), "Wrong channel property");
        for (int i = 0; i < times.Length; i++) Compare(clip.times[i], times[i], "Sample time");
        for (int i = 0; i < values.Length; i++)
        {
            bool reflect = target.property == "translation" && i % 3 == 2 || target.property == "rotation" && i % 4 < 2;
            Compare(target.values[i], reflect ? -values[i] : values[i], "Animation");
        }
    }
}
string directory = Path.GetDirectoryName(asset);
foreach (XenaTexture texture in document.textures)
    foreach (string filename in new[] { texture.original, texture.upscaled }.Where(s => !string.IsNullOrEmpty(s)))
        Check(File.Exists(Path.Combine(directory, filename)), "Missing texture " + filename);

var rejected = new List<string>();
void Reject(string name, Action<XenaDocument> change)
{
    XenaDocument invalid = Load(); change(invalid);
    try { XenaDocumentValidator.Validate(invalid); }
    catch (InvalidDataException) { rejected.Add(name); return; }
    throw new Exception("Invalid input accepted: " + name);
}
Reject("cyclic hierarchy", d => d.nodes[1].parent = 1);
Reject("out-of-range triangle", d => d.meshes[0].primitives[0].indices[0] = int.MaxValue);
Reject("truncated channel", d => d.clips[0].channels[0].values = new float[1]);
Reject("non-unit quaternion", d => d.nodes[0].rotation = new float[4]);
Reject("texture path traversal", d => d.textures[0].original = "../outside.png");
Reject("NaN geometry", d => d.meshes[0].primitives[0].positions[0] = float.NaN);
Reject("missing original texture", d => { d.textures[0].original = ""; d.textures[0].upscaled = ""; });
Reject("invalid lighting byte", d => { d.meshes[0].primitives[0].lightingRgb = new int[d.meshes[0].primitives[0].positions.Length]; d.meshes[0].primitives[0].lightingRgb[0] = 256; });
Reject("lit mesh without colors", d => { d.materials[d.meshes[0].primitives[0].material].vertexLighting = true; d.meshes[0].primitives[0].lightingRgb = null; });

int terrainQueries = 0;
long levelValues = 0;
long lightingValues = 0;
string levelPath = "unity/XenaUnity/Assets/Imported/Level01/Level01.xenamodel";
if (File.Exists(levelPath))
{
    var level = JsonSerializer.Deserialize<XenaDocument>(File.ReadAllText(levelPath), options);
    var levelSummary = XenaDocumentValidator.Validate(level);
    Check(levelSummary.Meshes == 74 && levelSummary.Nodes == 75 && levelSummary.Triangles == 3995 && levelSummary.Clips == 0, "Incorrect level totals");
    using var levelGltf = JsonDocument.Parse(File.ReadAllText("build/level-assets/01/level-assembled.gltf"));
    JsonElement sourceLevel = levelGltf.RootElement;
    byte[] levelBin = File.ReadAllBytes("build/level-assets/01/level-assembled.bin");
    Check(Convert.ToHexString(SHA256.HashData(levelBin)).ToLowerInvariant() == level.sourceBufferSha256, "Level source hash mismatch");
    float[] Values(int index, int dimension)
    {
        JsonElement a = sourceLevel.GetProperty("accessors")[index], view = sourceLevel.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
        int start = view.GetProperty("byteOffset").GetInt32(), count = a.GetProperty("count").GetInt32() * dimension;
        return Enumerable.Range(0, count).Select(i => BitConverter.ToSingle(levelBin, start + i * 4)).ToArray();
    }
    void Equal(float actual, float expected)
    {
        Check(Math.Abs(actual - expected) < 0.00001f, "Level Unity coordinate conversion differs"); levelValues++;
    }
    for (int mesh = 0; mesh < level.meshes.Length; mesh++)
    {
        JsonElement sourcePrimitives = sourceLevel.GetProperty("meshes")[mesh].GetProperty("primitives");
        Check(level.meshes[mesh].primitives.Length == sourcePrimitives.GetArrayLength(), "Level submesh count differs");
        for (int p = 0; p < level.meshes[mesh].primitives.Length; p++)
        {
            XenaPrimitive target = level.meshes[mesh].primitives[p];
            JsonElement attributes = sourcePrimitives[p].GetProperty("attributes");
            float[] positions = Values(attributes.GetProperty("POSITION").GetInt32(), 3);
            float[] normals = Values(attributes.GetProperty("NORMAL").GetInt32(), 3);
            float[] uv = Values(attributes.GetProperty("TEXCOORD_0").GetInt32(), 2);
            float[] lighting = Values(attributes.GetProperty("_XENA_LGT").GetInt32(), 3);
            Check(target.lightingRgb.Length == lighting.Length, "Level lighting count differs");
            for (int i = 0; i < lighting.Length; i++) { Check(target.lightingRgb[i] == lighting[i], "Level lighting byte differs"); lightingValues++; }
            for (int i = 0; i < positions.Length; i++) { Equal(target.positions[i], i % 3 == 2 ? -positions[i] : positions[i]); Equal(target.normals[i], i % 3 == 2 ? -normals[i] : normals[i]); }
            for (int i = 0; i < uv.Length; i++) Equal(target.uv[i], i % 2 == 1 ? 1 - uv[i] : uv[i]);
            for (int i = 0; i < target.indices.Length; i++) Check(target.indices[i] == i - i % 3 + new[] { 0, 2, 1 }[i % 3], "Level triangle winding differs");
        }
    }
}
string terrainPath = "build/level-assets/01/terrain-unity.json";
if (File.Exists(terrainPath))
{
    var terrain = JsonSerializer.Deserialize<XenaTerrainData>(File.ReadAllText(terrainPath), options);
    var query = new XenaTerrainQuery(terrain);
    using var samples = JsonDocument.Parse(File.ReadAllText("build/level-assets/01/terrain-query-samples.json"));
    foreach (JsonElement sample in samples.RootElement.EnumerateArray())
    {
        int[] point = sample.GetProperty("point").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        Check(query.GetHeight(point[0], point[1], point[2]) == sample.GetProperty("height").GetInt32(), "C# terrain query differs from original MIPS at " + string.Join(",", point));
        terrainQueries++;
    }
    terrain.cellTriangles[0] = int.MaxValue;
    bool rejectedTerrain = false;
    try { new XenaTerrainQuery(terrain); } catch (InvalidDataException) { rejectedTerrain = true; }
    Check(rejectedTerrain, "Invalid terrain index accepted");
}

var report = new { result = "PASS", nodes = summary.Nodes, meshes = summary.Meshes, triangles = summary.Triangles,
    clips = summary.Clips, frames = summary.SourceFrames, convertedValuesCompared = compared, csharpSyntaxFiles = syntaxFiles,
    rejectedMalformedInputs = rejected, terrainQueriesComparedToOriginalMips = terrainQueries,
    levelConvertedValuesCompared = levelValues, levelLightingValuesCompared = lightingValues,
    compiled = "Shared C# document schema, validator and original terrain query using .NET 9",
    unityEditorCompiled = false, shaderCompiled = false,
    scope = "C# deserialization, shared validation, full geometry/UV/winding/channel conversion against verified glTF; Unity source syntax only. Does not replace an Editor import test." };
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory("build/unity-import"); File.WriteAllText("build/unity-import/dotnet-verification.json", json + "\n");
Console.WriteLine(json);
