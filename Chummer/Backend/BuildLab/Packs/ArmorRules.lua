function CalculateMatrixCM(baseBoxes, deviceRating, bonusBoxes)
    -- DivAwayFromZero(2) is equivalent to math.ceil(deviceRating / 2) for positive numbers
    local halfRating = math.ceil(deviceRating / 2)
    return baseBoxes + halfRating + bonusBoxes
end
