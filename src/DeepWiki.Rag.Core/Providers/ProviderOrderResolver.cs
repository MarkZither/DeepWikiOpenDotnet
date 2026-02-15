using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepWiki.Rag.Core.Providers
{
    public static class ProviderOrderResolver
    {
        public static IList<IModelProvider> ResolveOrder(IEnumerable<IModelProvider> providers, IConfiguration cfg)
        {
            var list = providers?.ToList() ?? new List<IModelProvider>();
            var configured = cfg.GetSection("Generation:Providers").Get<string[]>() ?? Array.Empty<string>();
            if (configured.Length == 0) return list;

            var set = new HashSet<string>(configured, StringComparer.OrdinalIgnoreCase);
            var ordered = new List<IModelProvider>();
            foreach (var name in configured)
            {
                var match = list.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    ordered.Add(match);
            }

            // append any providers not specified in config (preserve their registration order)
            ordered.AddRange(list.Where(p => !set.Contains(p.Name)));
            return ordered;
        }
    }
}
