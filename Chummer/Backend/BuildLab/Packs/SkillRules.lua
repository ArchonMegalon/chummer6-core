function CalculateSkillKarmaCost(currentRating, targetRating)
    if currentRating >= targetRating then
        return 0
    end
    local levelsModded = targetRating * (targetRating + 1)
    levelsModded = levelsModded - (currentRating * (currentRating + 1))
    levelsModded = levelsModded / 2
    return levelsModded
end
