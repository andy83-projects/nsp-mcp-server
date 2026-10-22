using System.Reflection;
using System.Runtime.InteropServices;
using Nsp.Automation.Fallback;
using Xunit;

namespace Nsp.Mcp.Tools.Tests;

public sealed class InputLayoutTests
{
    // No user32 calls: verify the actual marshalling layout on the host architecture.
    // A keyboard-only union used to produce size 32 on ARM64, silently failing every call.
    [Fact]
    public void SendInputMatchesNativeAbi()
    {
        var input = typeof(InputSynthesizer).GetNestedType("Input", BindingFlags.NonPublic)!;
        var union = typeof(InputSynthesizer).GetNestedType("InputUnion", BindingFlags.NonPublic)!;
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf(input));
        Assert.Equal(IntPtr.Size == 8 ? 32 : 24, Marshal.SizeOf(union));
        Assert.Equal(new IntPtr(IntPtr.Size == 8 ? 8 : 4), Marshal.OffsetOf(input, "Union"));
    }
}
