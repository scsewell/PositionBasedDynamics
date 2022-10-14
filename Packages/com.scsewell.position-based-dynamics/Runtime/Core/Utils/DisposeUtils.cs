using System;

using Unity.Collections;

using UnityEngine;
using UnityEngine.Rendering;

namespace Scsewell.PositionBasedDynamics
{
    static class DisposeUtils
    {
        public static void DisposeSafe<T>(ref T instance) where T : IDisposable 
        {
            if (typeof(T).IsClass)
            {
                if (instance != null)
                {
                    instance.Dispose();
                    instance = default;
                }
            }
            else
            {
                instance.Dispose();
                instance = default;
            }
        }
        
        public static void DisposeSafe(ref CommandBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }
        }

        public static void DisposeSafe(ref ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }
        }

        public static void DisposeSafe<T>(ref NativeArray<T> buffer) where T : struct
        {
            if (buffer.IsCreated)
            {
                buffer.Dispose();
                buffer = default;
            }
        }
    }
}
