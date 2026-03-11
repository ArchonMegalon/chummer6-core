function CalculateKarma(metatype, isAwakened)
    local cost = 0
    if metatype == "Troll" then 
        cost = 40
    elseif metatype == "Elf" then 
        cost = 30
    elseif metatype == "Dwarf" or metatype == "Ork" then 
        cost = 20
    else 
        cost = 0 
    end

    if isAwakened then 
        cost = cost + 15 
    end
    
    return cost
end
