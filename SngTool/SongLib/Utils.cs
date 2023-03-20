using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SongLib
{
    public static class Utils
    {
        private static int defaultThreads = Math.Max(Environment.ProcessorCount - 1, 1);
        public async static Task ForEachAsync<TSource>(IEnumerable<TSource> source, Func<TSource, CancellationToken, ValueTask> body, int threads = -1)
        {
            if (threads < 0)
            {
                threads = defaultThreads;
            }

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = threads
            };

            await Parallel.ForEachAsync(source, options, body);
        }
    }
}