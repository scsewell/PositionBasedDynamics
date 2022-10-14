using System;

using Unity.Profiling;

using UnityEngine.Profiling;

namespace Scsewell.PositionBasedDynamics
{
#if DEVELOPMENT_BUILD || UNITY_EDITOR
    [IgnoredByDeepProfiler]
    struct ProfilerScope : IDisposable
    {
        public ProfilerScope(string name)
        {
            Profiler.BeginSample(name);
        }

        public void Dispose()
        {
            Profiler.EndSample();
        }
    }
#else
    struct ProfilerScope : IDisposable
    {
        public ProfilerScope(string name)
        {
        }

        public void Dispose()
        {
        }
    }
#endif
}
