using UnityEngine;

namespace Scsewell.PositionBasedDynamics
{
    struct KernelInfo
    {
        public int kernelID;
        public int threadGroupSizeX;
        public int threadGroupSizeY;
        public int threadGroupSizeZ;
    }
    
    static class GraphicsUtils
    {
        public static bool TryGetKernel(this ComputeShader shader, string name, out KernelInfo kernel)
        {
            if (!shader.HasKernel(name))
            {
                Debug.LogError($"Kernel \"{name}\" not found in compute shader \"{shader.name}\"!");
                kernel = default;
                return false;
            }
            
            kernel.kernelID = shader.FindKernel(name);
            
            shader.GetKernelThreadGroupSizes(kernel.kernelID,  out var x, out var y, out var z);
            kernel.threadGroupSizeX = (int)x;
            kernel.threadGroupSizeY = (int)y;
            kernel.threadGroupSizeZ = (int)z;
            
            return true;
        }

        public static int GetThreadGroupCount(ref this KernelInfo kernel, int count, int elementsPerThread = 1)
        {
            return GetThreadGroupCount(count, kernel.threadGroupSizeX * elementsPerThread);
        }

        public static int GetThreadGroupCount(int threads, int threadsPerGroup)
        {
            // integer divide that gives the ceil instead of floor
            return ((threads - 1) / threadsPerGroup) + 1;
        }
    }
}
