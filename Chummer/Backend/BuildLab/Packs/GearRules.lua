function CalculateTotalCost(ownCostPreMultipliers, quantity, parentMultiplier, costFor, pluginCost)
    return (ownCostPreMultipliers * quantity * parentMultiplier / costFor) + (pluginCost * quantity)
end
