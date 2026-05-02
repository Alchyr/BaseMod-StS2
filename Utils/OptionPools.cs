using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Utils;

public class OptionPools
{
    private WeightedList<AncientOption>[] _pools; 
    
    /// <summary>
    /// Constructor for an ancient's options that uses a separate pool for each option.
    /// </summary>
    public OptionPools(WeightedList<AncientOption> pool1, WeightedList<AncientOption> pool2, WeightedList<AncientOption> pool3)
    {
        _pools = [pool1, pool2, pool3];
    }
    
    /// <summary>
    /// Constructor for an ancient's options that uses one pool for its first two options and a second pool for its third option.
    /// </summary>
    public OptionPools(WeightedList<AncientOption> pool12, WeightedList<AncientOption> pool3)
    {
        _pools = [pool12, pool12, pool3];
    }

    /// <summary>
    /// Constructor for an ancient's options using one pool for all its options.
    /// </summary>
    public OptionPools(WeightedList<AncientOption> pool)
    {
        _pools = [pool, pool, pool];
    }
    
    public IEnumerable<AncientOption> AllOptions => _pools.SelectMany(pool => pool);
    
    public List<AncientOption> Roll(Rng rng, IRunState runState)
    {
        List<AncientOption> result = [];
        
        var pool = _pools[0];
        WeightedList<AncientOption> rollPool = [..pool.Where(option => option.ModelForOption.IsAllowed(runState))];
        
        result.Add(rollPool.GetRandom(rng, true));

        if (pool != _pools[1])
        {
            pool = _pools[1];
            rollPool = [..pool.Where(option => option.ModelForOption.IsAllowed(runState))];
        }
        result.Add(rollPool.GetRandom(rng, true));

        if (pool != _pools[2])
        {
            pool = _pools[2];
            rollPool = [..pool.Where(option => option.ModelForOption.IsAllowed(runState))];
        }
        result.Add(rollPool.GetRandom(rng, true));

        return result;
    }
}