using RateLimit.Constraints;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RateLimit.Extensions
{
    public static class ComposeExtensions
    {
        public static IResourceConstraint Compose(this IEnumerable<IResourceConstraint> constraints)
        {
            return constraints.Aggregate((agg, current) => new ComposedConstraint(agg, current));
        }
    }
}
