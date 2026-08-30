// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using System.Text.Json.Nodes;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace PG.StarWarsGame.LSP.Assets.Models;

/// <summary>
///     Writes a parsed ALO model, and any animations for it, as a glTF binary.
/// </summary>
/// <remarks>
///     <para>
///         glTF rather than a bespoke geometry payload because three.js's loader builds the
///         <c>SkinnedMesh</c>, the skeleton, the inverse bind matrices and the animation clips for us -
///         the parts that are fiddly and easy to get subtly wrong by hand. It also means a model can be
///         dropped into any external glTF viewer, so a geometry bug is diagnosable without involving
///         the renderer at all.
///     </para>
///     <para>
///         Alamo's own material model does not survive the trip: a sub-mesh names a <c>.fx</c> shader
///         and carries arbitrary parameters, and glTF has no equivalent. Both ride in the material's
///         <c>Extras</c> and the client swaps in its own materials after load, so nothing here tries to
///         approximate a shader.
///     </para>
/// </remarks>
public static class ModelGlbExporter
{
    /// <summary>
    ///     Alamo is Z-up right-handed; glTF is Y-up. One rotation on the root covers the whole scene.
    /// </summary>
    private static readonly Matrix4x4 ZUpToYUp =
        Matrix4x4.CreateRotationX(-MathF.PI / 2f);

    /// <summary>
    ///     Vertex-format substring marking the tangent/binormal pair. Only these formats bind them;
    ///     the rest leave the stored fields zero, and writing a zero tangent produces a degenerate
    ///     TBN that a glTF validator rejects and a normal map renders black through.
    /// </summary>
    private const string TangentFormatMarker = "U3U3";

    /// <summary>Where a sub-mesh's position in the file is recorded on its material.</summary>
    /// <remarks>
    ///     Named rather than repeated as a literal because the restore pass reads them back to pair
    ///     a primitive with the sub-mesh it came from, and a typo there would silently restore
    ///     nothing at all.
    /// </remarks>
    private const string MeshIndexExtra = "alamoMeshIndex";

    private const string SubMeshIndexExtra = "alamoSubMeshIndex";

    /// <summary>A file vertex the mesh builder never told us the position of.</summary>
    private const int Unmapped = -1;

    /// <summary>Exports <paramref name="model" /> to GLB bytes.</summary>
    /// <param name="animations">
    ///     Animations to include, each with the clip name to expose. Their bone indices are the model's;
    ///     a bone an animation does not drive simply keeps its rest pose.
    /// </param>
    public static byte[] Export(
        AlamoModelContent model,
        IReadOnlyList<(string Name, AlamoAnimationContent Animation)>? animations = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var scene = new SceneBuilder();
        var nodes = BuildSkeleton(model, animations ?? []);
        var faces = new List<SubMeshFaces>();

        foreach (var mesh in model.Meshes)
        foreach (var (subMesh, index) in mesh.SubMeshes.Select((s, i) => (s, i)))
            AddSubMesh(scene, model, nodes, mesh, subMesh, index, faces);

        var gltf = scene.ToGltf2();
        RestoreDroppedFaces(gltf, faces);
        AddBillboardExtras(gltf, model);
        AddVisibilityExtras(gltf, model, animations ?? []);

        using var buffer = new MemoryStream();
        gltf.WriteGLB(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    ///     Every face of one sub-mesh, in the glTF's own vertex numbering.
    /// </summary>
    /// <param name="Indices">
    ///     The file's whole index list, remapped through the welding the mesh builder did, so it is
    ///     usable as an index accessor as it stands.
    /// </param>
    private sealed record SubMeshFaces(int MeshIndex, int SubMeshIndex, IReadOnlyList<int> Indices);

    /// <summary>
    ///     Puts back the faces SharpGLTF's mesh builder refuses to carry.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>PrimitiveBuilder.AddTriangle</c> discards any triangle with zero area - it is
    ///         documented to return <c>(-1, -1, -1)</c> for one - and a shadow volume is mostly made
    ///         of exactly those. The tool pipeline preprocesses a volume mesh by splitting its verts
    ///         and laying a DEGENERATE QUAD along every edge; when the shader pushes the vertices
    ///         facing away from the light, one half of each quad moves and the other stays, and the
    ///         quad opens into the side wall that closes the volume.
    ///     </para>
    ///     <para>
    ///         Measured on <c>EV_LambdaShuttle.ALO</c>: its body volume is 717 faces, of which 505
    ///         are those quads, and every one was being dropped. The volume then tore open along
    ///         every silhouette edge the moment it extruded, the stencil count never closed, and the
    ///         model read as sitting inside its own shadow while the ground under it stayed lit. The
    ///         inspector, which reads the file directly, reported 717 faces for a mesh the viewport
    ///         drew 212 of.
    ///     </para>
    ///     <para>
    ///         Restored for every sub-mesh, not only for shadow volumes. What the file holds is what
    ///         the export should carry; deciding per shader which faces are worth keeping is how the
    ///         two came to disagree in the first place.
    ///     </para>
    /// </remarks>
    private static void RestoreDroppedFaces(ModelRoot gltf, IReadOnlyList<SubMeshFaces> faces)
    {
        var bySubMesh = faces.ToDictionary(f => (f.MeshIndex, f.SubMeshIndex));

        foreach (var primitive in gltf.LogicalMeshes.SelectMany(m => m.Primitives))
        {
            if (primitive.Material?.Extras is not JsonObject extras)
                continue;

            var meshIndex = extras[MeshIndexExtra]?.GetValue<int>();
            var subMeshIndex = extras[SubMeshIndexExtra]?.GetValue<int>();

            if (meshIndex is null || subMeshIndex is null
                || !bySubMesh.TryGetValue((meshIndex.Value, subMeshIndex.Value), out var whole))
                continue;

            // Only when something actually went missing: rewriting an accessor that already holds
            // the same triples would churn the buffer for nothing.
            if (primitive.IndexAccessor?.Count == whole.Indices.Count)
                continue;

            primitive.WithIndicesAccessor(PrimitiveType.TRIANGLES, whole.Indices);
        }
    }

    /// <summary>
    ///     Records each billboarded bone's mode on its glTF node.
    /// </summary>
    /// <remarks>
    ///     After <c>ToGltf2</c> rather than during the build, because <c>NodeBuilder</c> has no
    ///     extras of its own. Matching by name is exact - the <c>#index</c> suffix is what makes
    ///     these unique in the first place.
    ///     <para>
    ///         Only the bones that actually billboard are written. Across the shipped models 22598
    ///         of 22866 bones are <see cref="AlamoBillboardType.Disable" />, so writing the default
    ///         would put an extras object on nearly every node of every file to say nothing.
    ///     </para>
    /// </remarks>
    private static void AddBillboardExtras(ModelRoot gltf, AlamoModelContent model)
    {
        var byName = gltf.LogicalNodes
            .Where(node => !string.IsNullOrEmpty(node.Name))
            .GroupBy(node => node.Name)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        for (var i = 0; i < model.Bones.Count; i++)
        {
            var bone = model.Bones[i];
            if (bone.Billboard == AlamoBillboardType.Disable)
                continue;

            if (byName.TryGetValue($"{bone.Name}#{i}", out var node))
                node.Extras = new JsonObject
                {
                    ["alamoBillboard"] = bone.Billboard.ToString()
                };
        }
    }

    /// <summary>
    ///     Records, per clip, which bones the animation hides and on which frames.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         glTF 2.0 has no visibility channel, so this rides as extras on the animation. Every
    ///         reader that does not know about it still gets a valid file with correct motion; the
    ///         preview reads it and gates the bone.
    ///     </para>
    ///     <para>
    ///         Not optional data. 662 of the 1363 shipped animations hide at least one bone, across
    ///         4052 bone tracks, and 2285 of those tracks are particle proxies - so the dominant use
    ///         is timing an effect to the motion. The rancor's death explosion is attached to
    ///         <c>P_ATST_Die</c>, hidden for all 61 frames of <c>attack_00</c> and visible for 35 of
    ///         the 61 frames of <c>die_00</c>. Dropping the track fires every one of them from frame
    ///         zero of every clip.
    ///     </para>
    ///     <para>
    ///         One character per frame rather than a JSON array of booleans: 4052 tracks of up to a
    ///         few hundred frames each, and <c>[true,false,...]</c> costs six times what <c>"10"</c>
    ///         does for the same information.
    ///     </para>
    ///     <para>
    ///         Only bones that actually hide are written, and a clip with none gets no extras at
    ///         all - 701 of the shipped clips are in that position.
    ///     </para>
    /// </remarks>
    private static void AddVisibilityExtras(
        ModelRoot gltf,
        AlamoModelContent model,
        IReadOnlyList<(string Name, AlamoAnimationContent Animation)> animations)
    {
        var byName = gltf.LogicalAnimations
            .Where(clip => !string.IsNullOrEmpty(clip.Name))
            .GroupBy(clip => clip.Name)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var (name, animation) in animations)
        {
            if (!byName.TryGetValue(name, out var clip))
                continue;

            var bones = new JsonObject();

            foreach (var bone in animation.Bones)
            {
                if (bone.BoneIndex < 0 || bone.BoneIndex >= model.Bones.Count)
                    continue;

                if (bone.Frames.All(frame => frame.Visible))
                    continue;

                // Keyed on the MODEL's name for that bone, never the clip's. These keys are glTF
                // NODE names, and the nodes are named after the skeleton being exported - so where a
                // borrowed clip disagrees about what bone 12 is called, the clip's spelling would
                // name no node at all and the track would be silently unreadable.
                bones[$"{model.Bones[bone.BoneIndex].Name}#{bone.BoneIndex}"] =
                    string.Concat(bone.Frames.Select(frame => frame.Visible ? '1' : '0'));
            }

            if (bones.Count == 0)
                continue;

            var extras = clip.Extras as JsonObject ?? [];
            extras["alamoVisibility"] = new JsonObject
            {
                ["fps"] = animation.Fps,
                ["bones"] = bones
            };
            clip.Extras = extras;
        }
    }

    // ── skeleton ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     One <see cref="NodeBuilder" /> per bone, parented as the file describes and carrying every
    ///     animation's track for that bone.
    /// </summary>
    /// <remarks>
    ///     The Z-up correction goes on a single synthetic root rather than into each bone's transform:
    ///     baking it per bone would have to be undone before composing children, and would corrupt the
    ///     animation tracks, which are bone-local.
    /// </remarks>
    private static NodeBuilder[] BuildSkeleton(
        AlamoModelContent model,
        IReadOnlyList<(string Name, AlamoAnimationContent Animation)> animations)
    {
        var root = new NodeBuilder("AlamoRoot");
        root.LocalMatrix = ZUpToYUp;

        var nodes = new NodeBuilder[model.Bones.Count];

        for (var i = 0; i < model.Bones.Count; i++)
        {
            var bone = model.Bones[i];
            var parent = bone.ParentIndex >= 0 ? nodes[bone.ParentIndex] : root;

            // Names repeat in shipped skeletons - a hull carries many bones called
            // p_hp_imperial_damage - so the index is appended to keep glTF node names unique
            // without losing the name the XML actually references.
            nodes[i] = parent.CreateNode($"{bone.Name}#{i}");
            nodes[i].LocalMatrix = bone.RelativeTransform;
        }

        foreach (var (name, animation) in animations)
            AddAnimation(nodes, model, name, animation);

        return nodes;
    }

    private static void AddAnimation(
        NodeBuilder[] nodes, AlamoModelContent model, string name, AlamoAnimationContent animation)
    {
        if (animation.FrameCount <= 0 || animation.Fps <= 0)
            return;

        foreach (var bone in animation.Bones)
        {
            // The index is a hard limit - there is no node to drive past the end of the array - but
            // the NAME is not. A track whose name disagrees with the model's bone used to be skipped
            // here, on the reasoning that it belongs to a different skeleton. The user reported that
            // the units doing this animate perfectly well in the base game, so the engine plainly
            // binds by index and the strict rule was costing real clips their motion. It binds by
            // index here too now; a disagreement is reported against the OBJECT, where a reader can
            // act on it, rather than silently freezing the bone.
            if (bone.BoneIndex < 0 || bone.BoneIndex >= nodes.Length)
                continue;

            var node = nodes[bone.BoneIndex];
            var translation = node.UseTranslation(name);
            var rotation = node.UseRotation(name);
            var scale = node.UseScale(name);

            for (var frame = 0; frame < bone.Frames.Count; frame++)
            {
                // The stored final frame duplicates the first, which is what makes the glTF loop
                // seamless, so every sample is written including that duplicate.
                var time = frame / animation.Fps;
                var pose = bone.Frames[frame];

                translation.SetPoint(time, pose.Translation);
                rotation.SetPoint(time, Normalize(pose.Rotation));
                scale.SetPoint(time, pose.Scale);
            }
        }
    }

    /// <summary>
    ///     glTF requires unit quaternions; the packed 16-bit samples denormalise slightly.
    /// </summary>
    private static Quaternion Normalize(Quaternion q)
    {
        return q.LengthSquared() > float.Epsilon ? Quaternion.Normalize(q) : Quaternion.Identity;
    }

    // ── geometry ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     Adds one sub-mesh as its own glTF mesh.
    /// </summary>
    /// <remarks>
    ///     Per sub-mesh rather than per mesh because sub-meshes of one mesh may differ in whether they
    ///     carry tangents and in how they are skinned, and a mesh builder is one vertex type
    ///     throughout. It also keeps materials one-to-one with the shader bindings the file declares.
    /// </remarks>
    private static void AddSubMesh(
        SceneBuilder scene,
        AlamoModelContent model,
        NodeBuilder[] nodes,
        AlamoMesh mesh,
        AlamoSubMesh subMesh,
        int index,
        List<SubMeshFaces> faces)
    {
        if (subMesh.Vertices.Count == 0 || subMesh.Indices.Count < 3)
            return;

        var material = BuildMaterial(mesh, subMesh, index);
        var name = $"{mesh.Name}#{index}";
        var hasTangents = subMesh.VertexFormat.Contains(
            TangentFormatMarker, StringComparison.OrdinalIgnoreCase);

        var joints = SkinJoints(model, nodes, subMesh);

        if (joints is null)
        {
            // Rigid: the sub-mesh rides its mesh's attachment bone, so the bone node carries it and
            // no skin is needed at all.
            var node = mesh.BoneIndex >= 0 && mesh.BoneIndex < nodes.Length
                ? nodes[mesh.BoneIndex]
                : nodes.FirstOrDefault() ?? new NodeBuilder(name);

            scene.AddRigidMesh(hasTangents
                ? BuildRigid<VertexPositionNormalTangent>(subMesh, material, name, out var welded)
                : BuildRigid<VertexPositionNormal>(subMesh, material, name, out welded), node);

            Collect(faces, mesh.Index, index, subMesh, welded);
            return;
        }

        // The world matrix here is BIND space - where the vertices are taken to live when the
        // inverse bind matrices are computed - not an extra transform stacked on the result. The
        // vertices are stored in Alamo's Z-up space while every joint's world transform already
        // carries the Y-up correction from the root, so binding against identity leaves the skinned
        // mesh rotated away from the skeleton driving it. Verified on Ai_rancor.alo, whose mesh sat
        // clear of its own bones until this matched.
        scene.AddSkinnedMesh(hasTangents
                ? BuildSkinned<VertexPositionNormalTangent>(subMesh, material, name, out var skin)
                : BuildSkinned<VertexPositionNormal>(subMesh, material, name, out skin),
            ZUpToYUp, joints);

        Collect(faces, mesh.Index, index, subMesh, skin);
    }

    private static void Collect(
        List<SubMeshFaces> faces, int meshIndex, int subMeshIndex, AlamoSubMesh subMesh,
        IReadOnlyList<int> welded)
    {
        var whole = Faces(subMesh, welded);

        if (whole is not null)
            faces.Add(new SubMeshFaces(meshIndex, subMeshIndex, whole));
    }

    /// <summary>A file vertex index to builder vertex index map, all of it still unknown.</summary>
    private static int[] WeldMap(AlamoSubMesh subMesh)
    {
        var map = new int[subMesh.Vertices.Count];
        Array.Fill(map, Unmapped);
        return map;
    }

    /// <summary>
    ///     Learns where three of the file's vertices ended up, from the triangle that used them.
    /// </summary>
    /// <remarks>
    ///     <c>AddTriangle</c> returns the welded indices it used, which is the only way to observe
    ///     the mesh builder's welding from outside - <c>UseVertex</c> is not public. A refused
    ///     triangle answers <c>(-1, -1, -1)</c> and teaches us nothing, which is exactly the case
    ///     the restore has to cover; every vertex of a degenerate edge quad is also a corner of one
    ///     of the two real faces the quad joins, so a kept triangle names it sooner or later.
    /// </remarks>
    private static void Record(int[] map, (int A, int B, int C) file, (int A, int B, int C) built)
    {
        if (built.A < 0)
            return;

        map[file.A] = built.A;
        map[file.B] = built.B;
        map[file.C] = built.C;
    }

    /// <summary>
    ///     The file's index list in the builder's numbering, minus any face naming a vertex that
    ///     never turned up in a triangle the builder kept, or <see langword="null" /> when that
    ///     leaves nothing at all.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A face is skipped rather than guessed at. Emitting one that names a vertex we cannot
    ///         place would draw a triangle to whatever happens to sit at index zero, which is far
    ///         worse than not drawing it.
    ///     </para>
    ///     <para>
    ///         But skipping is PER FACE. This used to give up on the whole sub-mesh at the first
    ///         unplaceable vertex, on the reasoning that every vertex of a degenerate edge quad is
    ///         also a corner of one of the two real faces the quad joins - true of most models and
    ///         not of all. Measured across the shipped trees: <b>222 sub-meshes in 105 models</b>
    ///         carry a vertex used by nothing but zero-area faces, always just a handful of them,
    ///         and the all-or-nothing rule threw away <b>183940 faces</b> to keep them company.
    ///         <c>Ub_palace.alo</c> lost 4435 of its shadow volume's 6075 faces to SIX such
    ///         vertices, so the volume tore open along every silhouette edge.
    ///     </para>
    /// </remarks>
    private static IReadOnlyList<int>? Faces(AlamoSubMesh subMesh, IReadOnlyList<int> welded)
    {
        var indices = new List<int>(subMesh.Indices.Count);

        foreach (var (a, b, c) in Triangles(subMesh))
        {
            if (welded[a] == Unmapped || welded[b] == Unmapped || welded[c] == Unmapped)
                continue;

            indices.AddRange([welded[a], welded[b], welded[c]]);
        }

        return indices.Count == 0 ? null : indices;
    }

    private static IMeshBuilder<MaterialBuilder> BuildRigid<TGeometry>(
        AlamoSubMesh subMesh, MaterialBuilder material, string name, out IReadOnlyList<int> welded)
        where TGeometry : unmanaged, IVertexGeometry
    {
        var builder = new MeshBuilder<TGeometry, VertexColor1Texture2, VertexEmpty>(name);
        var primitive = builder.UsePrimitive(material);

        var map = WeldMap(subMesh);

        foreach (var (a, b, c) in Triangles(subMesh))
        {
            var used = primitive.AddTriangle(
                RigidVertex<TGeometry>(subMesh.Vertices[a]),
                RigidVertex<TGeometry>(subMesh.Vertices[b]),
                RigidVertex<TGeometry>(subMesh.Vertices[c]));

            Record(map, (a, b, c), used);
        }

        welded = map;
        return builder;
    }

    private static IMeshBuilder<MaterialBuilder> BuildSkinned<TGeometry>(
        AlamoSubMesh subMesh, MaterialBuilder material, string name, out IReadOnlyList<int> welded)
        where TGeometry : unmanaged, IVertexGeometry
    {
        var builder = new MeshBuilder<TGeometry, VertexColor1Texture2, VertexJoints4>(name);
        var primitive = builder.UsePrimitive(material);

        var map = WeldMap(subMesh);

        foreach (var (a, b, c) in Triangles(subMesh))
        {
            var used = primitive.AddTriangle(
                SkinnedVertex<TGeometry>(subMesh.Vertices[a]),
                SkinnedVertex<TGeometry>(subMesh.Vertices[b]),
                SkinnedVertex<TGeometry>(subMesh.Vertices[c]));

            Record(map, (a, b, c), used);
        }

        welded = map;
        return builder;
    }

    /// <summary>Index triples, skipping any that point outside the vertex buffer.</summary>
    private static IEnumerable<(int A, int B, int C)> Triangles(AlamoSubMesh subMesh)
    {
        var count = subMesh.Vertices.Count;
        for (var i = 0; i + 2 < subMesh.Indices.Count; i += 3)
        {
            int a = subMesh.Indices[i], b = subMesh.Indices[i + 1], c = subMesh.Indices[i + 2];
            if (a < count && b < count && c < count)
                yield return (a, b, c);
        }
    }

    private static VertexBuilder<TGeometry, VertexColor1Texture2, VertexEmpty> RigidVertex<TGeometry>(
        AlamoVertex vertex)
        where TGeometry : unmanaged, IVertexGeometry
    {
        return new VertexBuilder<TGeometry, VertexColor1Texture2, VertexEmpty>(
            Geometry<TGeometry>(vertex), Material(vertex));
    }

    private static VertexBuilder<TGeometry, VertexColor1Texture2, VertexJoints4> SkinnedVertex<TGeometry>(
        AlamoVertex vertex)
        where TGeometry : unmanaged, IVertexGeometry
    {
        var w = vertex.BoneWeights;
        var b = vertex.BoneIndices;

        // Local slots, not model bone indices - the skin's joint list is what maps them, and it is
        // built from the sub-mesh's own remap table.
        var joints = new VertexJoints4(
            ((int)b.I0, w.X), ((int)b.I1, w.Y), ((int)b.I2, w.Z), ((int)b.I3, w.W));

        return new VertexBuilder<TGeometry, VertexColor1Texture2, VertexJoints4>(
            Geometry<TGeometry>(vertex), Material(vertex), joints);
    }

    private static TGeometry Geometry<TGeometry>(AlamoVertex vertex)
        where TGeometry : unmanaged, IVertexGeometry
    {
        var geometry = default(TGeometry);
        geometry.SetPosition(vertex.Position);
        geometry.SetNormal(SafeNormal(vertex.Normal));

        // The handedness component is not stored, and the shipped binormal is not reliably the cross
        // product of normal and tangent, so it is not derived from one: +1 is the glTF default and
        // the value every Alamo bump shader assumes.
        geometry.SetTangent(new Vector4(SafeNormal(vertex.Tangent), 1f));

        return geometry;
    }

    private static VertexColor1Texture2 Material(AlamoVertex vertex)
    {
        return new VertexColor1Texture2(vertex.Color, vertex.TexCoord0, vertex.TexCoord1);
    }

    /// <summary>
    ///     A unit normal, substituting up for a degenerate one.
    /// </summary>
    /// <remarks>
    ///     Shipped models do contain zero normals on meshes whose vertex format never binds them
    ///     (collision hulls and shadow volumes). glTF requires unit-length normals, so a zero would
    ///     make the whole file fail validation over geometry that is not even drawn.
    /// </remarks>
    private static Vector3 SafeNormal(Vector3 v)
    {
        return v.LengthSquared() > float.Epsilon ? Vector3.Normalize(v) : Vector3.UnitZ;
    }

    /// <summary>
    ///     The joint nodes a skinned sub-mesh binds to, or null when it is rigid.
    /// </summary>
    private static NodeBuilder[]? SkinJoints(
        AlamoModelContent model, NodeBuilder[] nodes, AlamoSubMesh subMesh)
    {
        if (subMesh.Skinning == AlamoSkinningMode.Static || subMesh.SkinBones.Count == 0)
            return null;

        var joints = new NodeBuilder[subMesh.SkinBones.Count];
        for (var i = 0; i < subMesh.SkinBones.Count; i++)
        {
            var bone = subMesh.SkinBones[i];

            // A remap entry outside the skeleton would index past the joint array at draw time; the
            // root is a harmless stand-in and keeps the rest of the mesh intact.
            joints[i] = bone >= 0 && bone < nodes.Length ? nodes[bone] : nodes[0];
        }

        return joints;
    }

    // ── materials ─────────────────────────────────────────────────────────────

    /// <summary>
    ///     A placeholder material carrying the Alamo shader binding in its extras.
    /// </summary>
    /// <remarks>
    ///     Nothing here tries to approximate the shader. The client resolves the real material from the
    ///     shader name and parameters, so what matters is that both survive the round trip intact -
    ///     including parameters we do not understand, since a mod's own shader may read them.
    /// </remarks>
    private static MaterialBuilder BuildMaterial(
        AlamoMesh mesh, AlamoSubMesh subMesh, int subMeshIndex)
    {
        var material = new MaterialBuilder($"{mesh.Name}#{subMesh.Shader}")
            .WithDoubleSide(false)
            .WithMetallicRoughnessShader()
            .WithMetallicRoughness(0f, 1f);

        var extras = new JsonObject
        {
            ["alamoShader"] = subMesh.Shader,
            ["alamoVertexFormat"] = subMesh.VertexFormat,
            ["alamoSkinning"] = subMesh.Skinning.ToString(),
            ["alamoMesh"] = mesh.Name,

            // The mesh's position in the file, so the client can join back to what the server knows
            // about it - the bounding box, above all. The NAME cannot do that job: nothing stops a
            // model carrying two meshes called the same thing, and the join would fail silently by
            // showing one mesh's box against another.
            [MeshIndexExtra] = mesh.Index,

            // Which sub-mesh of that mesh. The pair names this geometry in the file exactly, which
            // is what the vertex and face tables are fetched by.
            [SubMeshIndexExtra] = subMeshIndex
        };

        // ALT and LOD gate visibility at runtime and are encoded in the mesh NAME, so they would be
        // lost entirely if they did not travel here.
        if (mesh.Alt is { } alt) extras["alamoAlt"] = alt;
        if (mesh.Lod is { } lod) extras["alamoLod"] = lod;
        if (!mesh.IsVisible) extras["alamoHidden"] = true;
        if (mesh.IsCollidable) extras["alamoCollidable"] = true;

        // Namespaced so a parameter can never collide with one of the keys above - a mod is free to
        // name a shader parameter "alamoShader".
        foreach (var parameter in subMesh.Parameters)
            extras[$"param:{parameter.Name}"] = ParameterValue(parameter);

        material.Extras = extras;
        return material;
    }

    private static JsonNode ParameterValue(AlamoShaderParameter parameter)
    {
        return parameter.Type switch
        {
            AlamoShaderParameterType.Int => JsonValue.Create(parameter.Int),
            AlamoShaderParameterType.Float => JsonValue.Create(parameter.Float),
            AlamoShaderParameterType.Float3 => new JsonArray(
                parameter.Float3.X, parameter.Float3.Y, parameter.Float3.Z),
            AlamoShaderParameterType.Float4 => new JsonArray(
                parameter.Float4.X, parameter.Float4.Y, parameter.Float4.Z, parameter.Float4.W),
            _ => JsonValue.Create(parameter.Texture)
        };
    }
}
