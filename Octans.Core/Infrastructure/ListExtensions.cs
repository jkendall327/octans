using Octans.Core.Querying;

namespace Octans.Core;

public static class ListExtensions
{
    public static (List<T1>, List<T2>, List<T3>) Partition<T1, T2, T3>(this IEnumerable<IPredicate> source)
        where T1 : IPredicate
        where T2 : IPredicate
        where T3 : IPredicate
    {
        var src = source.ToArray();
        
        return (
            src.OfType<T1>().ToList(),
            src.OfType<T2>().ToList(),
            src.OfType<T3>().ToList()
        );
    }
}