using System.Runtime.InteropServices;

namespace HLProOptimizer.Infrastructure.Interop;

/// <summary>Verifica se o token do processo atual é de administrador.</summary>
internal static class ElevationHelper
{
    /// <summary>
    /// Retorna <c>true</c> quando o processo roda com privilégios elevados
    /// (token pertencente ao grupo Administrators).
    /// </summary>
    public static bool IsProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var token = IntPtr.Zero;

        try
        {
            if (!NativeMethods.OpenProcessToken(
                    System.Diagnostics.Process.GetCurrentProcess().Handle,
                    NativeMethods.TokenQuery,
                    out token))
            {
                return false;
            }

            var size = Marshal.SizeOf<int>();
            var buffer = Marshal.AllocHGlobal(size);

            try
            {
                if (NativeMethods.GetTokenInformation(
                        token,
                        NativeMethods.TokenElevation,
                        buffer,
                        size,
                        out _))
                {
                    return Marshal.ReadInt32(buffer) != 0;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(token);
            }
        }
    }
}
