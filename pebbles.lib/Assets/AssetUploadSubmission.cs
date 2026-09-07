using System;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;

namespace MeowSci.PebblesLib;

/// <summary>
/// A cancellable private command buffer. StagingPool owns upload memory only: unlike its native
/// command list, this buffer is never automatically submitted from Dispose after a recording error.
/// </summary>
public sealed class AssetUploadSubmission : IDisposable
{
    private readonly Renderer _renderer;
    private VkCommandPool _pool;
    private VkFence _fence;
    private StagingPool? _staging;
    private bool _submitted, _completed;
    public CommandBuffer Command { get; private set; }
    public StagingPool Staging => _staging ?? throw new ObjectDisposedException(nameof(AssetUploadSubmission));

    public AssetUploadSubmission(Renderer renderer)
    {
        _renderer = renderer;
        try
        {
            _staging = renderer.Allocator.CreateStagingPool(renderer.Graphics, 1);
            var info = new VkCommandPoolCreateInfo
            {
                QueueFamilyIndex = renderer.Graphics.Index,
                Flags = VkCommandPoolCreateFlags.TransientBit
            };
            _pool = renderer.Device.CreateCommandPool(in info, null);
            Command = renderer.Device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo
            {
                CommandPool = _pool, Level = VkCommandBufferLevel.Primary
            });
            var fenceInfo = new VkFenceCreateInfo();
            _fence = renderer.Device.CreateFence(in fenceInfo, null);
            Command.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        }
        catch { Dispose(); throw; }
    }

    public void SubmitAndWait()
    {
        Command.End();
        CommandBuffer command = Command;
        _submitted = true;
        _renderer.Graphics.Submit(default(Span<VkSemaphore>), default(Span<VkPipelineStageFlags>),
            new Span<CommandBuffer>(ref command), default(Span<VkSemaphore>), _fence);
        var result = _renderer.Device.WaitForFence(_fence, -1);
        if (result != VkResult.Success) throw new InvalidOperationException($"Asset upload completion failed: {result}.");
        _completed = true;
    }

    public void Dispose()
    {
        if (_submitted && !_completed) _renderer.Device.WaitIdle();
        if (!_pool.IsNull()) { _renderer.Device.DestroyCommandPool(_pool, null); _pool = default; }
        _staging?.Dispose(); _staging = null;
        if (!_fence.IsNull()) { _renderer.Device.DestroyFence(_fence, null); _fence = default; }
    }
}
