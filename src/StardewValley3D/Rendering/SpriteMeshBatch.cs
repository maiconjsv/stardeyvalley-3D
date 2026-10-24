using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace StardewValley3D.Rendering;

/// <summary>Uma instancia de sprite volumetrico posicionada no mundo.</summary>
public struct SpriteInstance
{
    public SpriteMesh Mesh;
    /// <summary>Base do sprite, em unidades de mundo (onde ele encosta no chao).</summary>
    public Vector3 Anchor;
    /// <summary>Rotacao em torno de Y, em radianos.</summary>
    public float Yaw;
    public Color Tint;
    public bool FlipHorizontal;
}

/// <summary>
/// Desenha os sprites volumetricos. Usa alpha test (nao alpha blend): o recorte do sprite
/// e resolvido no shader e o depth buffer cuida da ordenacao, que e o que permite um
/// personagem passar corretamente atras de uma parede ou dentro de um arbusto.
/// </summary>
public sealed class SpriteMeshBatch
{
    private readonly List<SpriteInstance> instances = new(512);
    private AlphaTestEffect effect = null!;

    public int LastInstanceCount { get; private set; }
    public int LastTriangleCount { get; private set; }

    public void Initialize(GraphicsDevice device)
    {
        this.effect = new AlphaTestEffect(device)
        {
            AlphaFunction = CompareFunction.Greater,
            ReferenceAlpha = 128,
            VertexColorEnabled = false
        };
    }

    public void Begin() => this.instances.Clear();

    public void Add(in SpriteInstance instance)
    {
        if (instance.Mesh is not null)
            this.instances.Add(instance);
    }

    public void End(GraphicsDevice device, Camera3D camera, Color ambient)
    {
        this.LastInstanceCount = this.instances.Count;
        this.LastTriangleCount = 0;

        if (this.instances.Count == 0)
            return;

        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        device.SamplerStates[0] = SamplerState.PointClamp;
        device.RasterizerState = RasterizerState.CullNone;

        this.effect.View = camera.View;
        this.effect.Projection = camera.Projection;

        // Agrupa por malha para trocar o vertex buffer o minimo possivel.
        // TODO Fase 8: instancing por hardware, uma draw call por malha.
        this.instances.Sort(static (a, b) => a.Mesh.GetHashCode().CompareTo(b.Mesh.GetHashCode()));

        SpriteMesh? current = null;
        foreach (SpriteInstance instance in this.instances)
        {
            if (!ReferenceEquals(instance.Mesh, current))
            {
                current = instance.Mesh;
                device.SetVertexBuffer(current.Buffer);
                this.effect.Texture = current.Texture;
            }

            Matrix world = Matrix.CreateRotationY(instance.Yaw)
                * Matrix.CreateTranslation(instance.Anchor);

            if (instance.FlipHorizontal)
                world = Matrix.CreateScale(-1f, 1f, 1f) * world;

            this.effect.World = world;
            this.effect.DiffuseColor = instance.Tint.ToVector3() * ambient.ToVector3();
            this.effect.Alpha = instance.Tint.A / 255f;

            foreach (EffectPass pass in this.effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawPrimitives(PrimitiveType.TriangleList, 0, current!.TriangleCount);
            }

            this.LastTriangleCount += current!.TriangleCount;
        }

        device.SetVertexBuffer(null);
    }
}
