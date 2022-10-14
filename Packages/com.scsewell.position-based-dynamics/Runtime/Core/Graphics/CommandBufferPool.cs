using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Scsewell.PositionBasedDynamics
{
    static class CommandBufferPool
    {
        static ObjectPool<CommandBuffer> s_bufferPool = new ObjectPool<CommandBuffer>(null, x => x.Clear(), x => x.Release());

        public static CommandBuffer Get(string name)
        {
            var cmd = s_bufferPool.Get();
            cmd.name = name;
            return cmd;
        }

        public static void Release(CommandBuffer buffer)
        {
            s_bufferPool.Release(buffer);
        }
    }
}
