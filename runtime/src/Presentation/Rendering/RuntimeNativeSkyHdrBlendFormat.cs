using System.Runtime.InteropServices;
using Godot;

namespace OpenNV.Runtime.Presentation.Rendering;

// Public graphics API capability queries on Godot's actual render-thread
// device. No renderer-name, pipeline allocation or logical texture substitutes
// for the native post-pixel blending feature bit.
internal static class RuntimeNativeSkyHdrBlendFormat
{
    [StructLayout(LayoutKind.Sequential)]
    private struct D3D12FormatSupport { internal uint Format, Support1, Support2; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CheckFeatureSupport(nint device, uint feature, ref D3D12FormatSupport data, uint bytes);
    [StructLayout(LayoutKind.Sequential)]
    private struct VulkanFormatProperties { internal uint LinearTiling, OptimalTiling, Buffer; }
    [DllImport("vulkan-1.dll", EntryPoint = "vkGetPhysicalDeviceFormatProperties", CallingConvention = CallingConvention.Winapi)]
    private static extern void VulkanFormatQuery(nint physicalDevice, uint format, out VulkanFormatProperties properties);
    internal static bool Query(RenderingDevice device)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new NotSupportedException("Actual host HDR blend-feature query ABI is unowned on this platform.");
        var driver = RenderingServer.GetCurrentRenderingDriverName();
        if (driver == "d3d12")
        {
            var pointer = unchecked((nint)device.GetDriverResource(RenderingDevice.DriverResource.LogicalDevice, default, 0));
            if (pointer == 0) throw new InvalidOperationException("Godot did not return its actual ID3D12Device.");
            // ID3D12Device's public COM vtable: IUnknown, ID3D12Object,
            // GetNodeCount and five creation methods precede CheckFeatureSupport.
            var table = Marshal.ReadIntPtr(pointer);
            if (table == 0) throw new InvalidOperationException("Actual D3D12 feature-query vtable is absent.");
            var function = Marshal.ReadIntPtr(table, 13 * IntPtr.Size);
            if (function == 0) throw new InvalidOperationException("Actual D3D12 feature-query entry is absent.");
            var query = Marshal.GetDelegateForFunctionPointer<CheckFeatureSupport>(function);
            var support = new D3D12FormatSupport { Format = 10 }; // DXGI_FORMAT_R16G16B16A16_FLOAT.
            var status = query(pointer, 3, ref support, 12); // D3D12_FEATURE_FORMAT_SUPPORT.
            if (status < 0) Marshal.ThrowExceptionForHR(status);
            if (support.Format != 10) throw new InvalidDataException("D3D12 changed the queried source half-float format.");
            return (support.Support1 & 0x8000) != 0; // D3D12_FORMAT_SUPPORT1_BLENDABLE.
        }
        if (driver == "vulkan")
        {
            var pointer = unchecked((nint)device.GetDriverResource(RenderingDevice.DriverResource.PhysicalDevice, default, 0));
            if (pointer == 0) throw new InvalidOperationException("Godot did not return its actual VkPhysicalDevice.");
            VulkanFormatQuery(pointer, 97, out var support); // VK_FORMAT_R16G16B16A16_SFLOAT.
            return (support.OptimalTiling & 0x100) != 0; // VK_FORMAT_FEATURE_COLOR_ATTACHMENT_BLEND_BIT.
        }
        throw new NotSupportedException("Actual HDR blend-feature query has no admitted driver ABI: " + driver);
    }
}
