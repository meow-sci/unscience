using System;
using System.Collections.Generic;
using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using KSA;
using MeowSci.PebblesLib;
using RenderCore;

namespace MeowSci.SphinxLib;

internal sealed unsafe partial class StaticModelResources
{
    /// <summary>Replace only vertex buffers; textures, materials and indices remain loaded.</summary>
    public void UpdateTextureMapping(TextureMapping mapping)
    {
        if (_disposed || !ReferenceEquals(Owner, Program.GetRenderer()))
            throw new InvalidOperationException("The renderer changed; remove and re-place this static.");
        mapping.Validate();
        var replacements = new List<BufferEx>();
        try
        {
            using (var upload = new AssetUploadSubmission(Owner))
            {
                foreach (var draw in _draws)
                {
                    var vertices = mapping.Apply(draw.OriginalVertices);
                    var buffer = Buffer("Sphinx remapped vertices", ByteSize.Of<StaticVertex>(vertices.Length), VkBufferUsageFlags.VertexBufferBit);
                    replacements.Add(buffer);
                    VkUtils.StageAndUploadToBuffer(upload.Staging, buffer.VkBuffer, buffer.BindOffset, vertices.AsSpan(), upload.Command);
                    var barrier = KSA.Rendering.Utils.CreateBarrier(buffer.VkBuffer, VkAccessFlags.TransferWriteBit,
                        VkAccessFlags.VertexAttributeReadBit, VK.WHOLE_SIZE, ByteSize.Zero);
                    upload.Command.PipelineBarrier(VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.VertexInputBit,
                        VkDependencyFlags.None, default(ReadOnlySpan<VkMemoryBarrier>),
                        new ReadOnlySpan<VkBufferMemoryBarrier>(ref barrier), default(ReadOnlySpan<VkImageMemoryBarrier>));
                }
                upload.SubmitAndWait();
            }
            // Keep every old buffer alive until all viewports have finished using it.
            // Validation/upload failure above leaves the entire visible mesh unchanged.
            Owner.Device.WaitIdle();
            for (int i = 0; i < _draws.Count; i++)
            {
                var previous = _draws[i].Vertices;
                _draws[i].Vertices = replacements[i];
                replacements[i] = previous!.Value;
            }
        }
        finally
        {
            foreach (var buffer in replacements) buffer.Dispose();
        }
    }
}
