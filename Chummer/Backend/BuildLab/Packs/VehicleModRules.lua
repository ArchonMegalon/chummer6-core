function CalculateOwnCost(baseCost, discountCost)
    local cost = baseCost
    if discountCost then
        cost = cost * 0.9
    end
    return cost
end
