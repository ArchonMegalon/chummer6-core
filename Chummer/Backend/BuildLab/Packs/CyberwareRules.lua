function CalculateTotalCost(baseCost, isSuite)
    local cost = baseCost
    if isSuite then
        cost = cost * 0.9
    end
    return cost
end
